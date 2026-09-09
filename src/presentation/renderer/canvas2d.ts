/**
 * Canvas 2D backend.
 *
 * This is the one web-specific module in the presentation layer. Everything it
 * depends on - the timeline, the playback clock, the projection, the sprite
 * orientation - is pure and portable; only the drawing calls below assume a
 * browser. A Godot or Unity backend replaces this file and nothing else.
 *
 * Characters are drawn procedurally rather than from a sprite sheet, because
 * the library ships no art. Swap `drawActor` for a sheet blit and the rest of
 * the pipeline is unchanged.
 */

import type { Element } from '../../domain/elements.js';
import type { Facing } from '../../field/grid.js';
import { project, snapToPixel, type CameraConfig, type Vec3 } from '../camera.js';
import { idleBob, phaseFromId, spriteDirection } from '../sprites.js';
import { elementColor, type Theme } from '../theme.js';
import { foldRenderState, foldSceneState, type EntityRenderState, type Playback } from '../playback.js';

export interface RenderEntity {
  id: string;
  world: Vec3;
  facing: Facing;
  side: 'party' | 'foe';
  element: Element;
  name: string;
  downed: boolean;
  hp: number;
  maxHp: number;
  aether: number;
  maxAether: number;
}

export interface SceneModel {
  entities: RenderEntity[];
  /** Tile extent of the arena floor. */
  floor: { width: number; height: number };
  camera: CameraConfig;
  theme: Theme;
}

const SPRITE_HEIGHT = 46;
const SPRITE_WIDTH = 26;

export class CanvasRenderer {
  private readonly ctx: CanvasRenderingContext2D;

  constructor(private readonly canvas: HTMLCanvasElement) {
    const ctx = canvas.getContext('2d');
    if (!ctx) throw new Error('Canvas 2D context unavailable');
    this.ctx = ctx;
    // Point filtering keeps procedural and pixel art crisp when scaled.
    this.ctx.imageSmoothingEnabled = false;
  }

  draw(scene: SceneModel, playback: Playback, timeMs: number): void {
    const { ctx } = this;
    const { theme, camera } = scene;
    const activeAll = playback.activeAt(playback.now);
    const sceneState = foldSceneState(activeAll);

    ctx.save();
    ctx.imageSmoothingEnabled = false;

    this.drawBackground(theme, camera);
    this.drawFloor(scene);

    // Painter's algorithm: sort by projected depth so nearer things overlap.
    const drawables = scene.entities
      .map((entity) => ({ entity, point: snapToPixel(project(entity.world, camera)) }))
      .sort((a, b) => a.point.depth - b.point.depth);

    for (const { entity, point } of drawables) {
      const state = foldRenderState(playback.activeForTrack(entity.id, playback.now));
      this.drawShadow(entity, point, theme, camera);
      this.drawActor(scene, entity, point, state, timeMs);
    }

    // Overlays draw after every sprite so they are never occluded.
    for (const { entity, point } of drawables) {
      const state = foldRenderState(playback.activeForTrack(entity.id, playback.now));
      this.drawVitals(scene, entity, point, state);
      this.drawNumerals(scene, point, state);
    }

    if (sceneState.screenFlash > 0) {
      ctx.fillStyle = `rgba(255, 255, 255, ${sceneState.screenFlash * 0.55})`;
      ctx.fillRect(0, 0, camera.viewportWidth, camera.viewportHeight);
    }
    if (sceneState.banner) this.drawBanner(theme, camera, sceneState.banner);

    ctx.restore();
  }

  private drawBackground(theme: Theme, camera: CameraConfig): void {
    const { ctx } = this;
    const gradient = ctx.createLinearGradient(0, 0, 0, camera.viewportHeight);
    gradient.addColorStop(0, theme.background[0]);
    gradient.addColorStop(1, theme.background[1]);
    ctx.fillStyle = gradient;
    ctx.fillRect(0, 0, camera.viewportWidth, camera.viewportHeight);
  }

  /** The arena floor, drawn as projected diamonds so elevation reads clearly. */
  private drawFloor(scene: SceneModel): void {
    const { ctx } = this;
    const { camera, theme, floor } = scene;

    for (let y = 0; y < floor.height; y++) {
      for (let x = 0; x < floor.width; x++) {
        const world: Vec3 = { x: x - floor.width / 2, y: y - floor.height / 2, z: 0 };
        const centre = project(world, camera);
        const hw = camera.tileHalfWidth * camera.zoom;
        const hh = camera.tileHalfHeight * camera.zoom;

        ctx.beginPath();
        ctx.moveTo(centre.x, centre.y - hh);
        ctx.lineTo(centre.x + hw, centre.y);
        ctx.lineTo(centre.x, centre.y + hh);
        ctx.lineTo(centre.x - hw, centre.y);
        ctx.closePath();

        ctx.fillStyle = (x + y) % 2 === 0 ? theme.floor : theme.floorHighlight;
        ctx.fill();
        ctx.strokeStyle = theme.gridLine;
        ctx.lineWidth = 1;
        ctx.stroke();
      }
    }
  }

  private drawShadow(entity: RenderEntity, point: { x: number; y: number }, theme: Theme, camera: CameraConfig): void {
    const { ctx } = this;
    ctx.save();
    ctx.fillStyle = theme.shadow;
    ctx.beginPath();
    ctx.ellipse(point.x, point.y, 13 * camera.zoom, 6 * camera.zoom, 0, 0, Math.PI * 2);
    ctx.fill();
    ctx.restore();
  }

  /**
   * A procedural stand-in character.
   *
   * Replace this with a sprite-sheet blit using `spriteDirection()` to choose
   * the row and `frameAt()` to choose the column; nothing else changes.
   */
  private drawActor(
    scene: SceneModel,
    entity: RenderEntity,
    point: { x: number; y: number },
    state: EntityRenderState,
    timeMs: number,
  ): void {
    const { ctx } = this;
    const { theme, camera } = scene;
    const zoom = camera.zoom;

    // Lunge toward the target, then back, driven by the folded step state.
    let offsetX = 0;
    let offsetY = 0;
    if (state.lungeToward) {
      const target = scene.entities.find((e) => e.id === state.lungeToward);
      if (target) {
        const targetPoint = project(target.world, camera);
        offsetX = (targetPoint.x - point.x) * 0.22 * state.lungeAmount;
        offsetY = (targetPoint.y - point.y) * 0.22 * state.lungeAmount;
      }
    }

    const bob = entity.downed ? 0 : idleBob(timeMs, phaseFromId(entity.id));
    const x = Math.round(point.x + offsetX + state.shakeOffset);
    const y = Math.round(point.y + offsetY + bob);

    ctx.save();
    ctx.globalAlpha = state.opacity;

    // Downed characters collapse in place. Squashing toward the ground point
    // reads as falling; rotating about that point would swing the sprite off
    // to one side instead, which looks like it slid rather than fell.
    if (entity.downed || state.pose === 'down') {
      ctx.translate(x, y);
      ctx.scale(1, 0.3);
      ctx.translate(-x, -y);
      ctx.globalAlpha = state.opacity * 0.75;
    }

    const body = elementColor(theme, entity.element);
    const height = SPRITE_HEIGHT * zoom;
    const width = SPRITE_WIDTH * zoom;
    const top = y - height;

    // Body
    ctx.fillStyle = body;
    ctx.fillRect(x - width / 2, top + height * 0.34, width, height * 0.66);

    // Head
    ctx.fillStyle = '#f0d9c0';
    const headSize = width * 0.62;
    ctx.fillRect(x - headSize / 2, top, headSize, headSize);

    // A darker band suggests a facing direction without needing real art.
    const direction = spriteDirection(entity.facing);
    ctx.fillStyle = 'rgba(0,0,0,0.28)';
    if (direction === 'n' || direction === 'ne' || direction === 'nw') {
      ctx.fillRect(x - headSize / 2, top, headSize, headSize * 0.45);
    } else {
      ctx.fillRect(x - headSize / 2, top + headSize * 0.55, headSize, headSize * 0.2);
    }

    // Outline reads the side: allies light, foes dark.
    ctx.strokeStyle = entity.side === 'party' ? '#ffffff' : '#1b1630';
    ctx.lineWidth = Math.max(1, zoom);
    ctx.strokeRect(x - width / 2, top + height * 0.34, width, height * 0.66);

    // Casting and attacking poses get a simple tell.
    if (state.pose === 'cast') {
      ctx.strokeStyle = body;
      ctx.globalAlpha = state.opacity * 0.8;
      ctx.beginPath();
      ctx.arc(x, y - height * 0.5, width * (0.8 + Math.sin(timeMs / 90) * 0.08), 0, Math.PI * 2);
      ctx.stroke();
    }

    // Impact flash paints over the whole sprite.
    if (state.flashIntensity > 0) {
      ctx.globalAlpha = state.opacity * state.flashIntensity;
      ctx.fillStyle = state.flashColor;
      ctx.fillRect(x - width / 2, top, width, height);
    }

    ctx.restore();

    for (const vfx of state.vfx) this.drawVfx(theme, x, y - height * 0.5, vfx, zoom);
  }

  private drawVfx(
    theme: Theme,
    x: number,
    y: number,
    vfx: { effect: string; element: string | null; t: number },
    zoom: number,
  ): void {
    const { ctx } = this;
    const colour = vfx.element ? elementColor(theme, vfx.element) : vfx.effect === 'heal' ? theme.hpBar : '#ffffff';

    ctx.save();
    ctx.globalAlpha = Math.max(0, 1 - vfx.t);

    if (vfx.effect === 'heal') {
      // Motes of light rising.
      for (let i = 0; i < 6; i++) {
        const angle = (i / 6) * Math.PI * 2;
        const radius = 14 * zoom;
        ctx.fillStyle = colour;
        ctx.fillRect(
          x + Math.cos(angle) * radius - 2,
          y - vfx.t * 34 * zoom + Math.sin(angle) * radius * 0.4,
          3 * zoom,
          3 * zoom,
        );
      }
    } else if (vfx.effect === 'mote-unleash' || vfx.effect === 'class-change') {
      ctx.strokeStyle = colour;
      ctx.lineWidth = 2 * zoom;
      ctx.beginPath();
      ctx.arc(x, y, (10 + vfx.t * 46) * zoom, 0, Math.PI * 2);
      ctx.stroke();
    } else {
      // Impact: an expanding ring plus a few shards.
      ctx.strokeStyle = colour;
      ctx.lineWidth = 3 * zoom * (1 - vfx.t);
      ctx.beginPath();
      ctx.arc(x, y, (6 + vfx.t * 30) * zoom, 0, Math.PI * 2);
      ctx.stroke();
      ctx.fillStyle = colour;
      for (let i = 0; i < 4; i++) {
        const angle = (i / 4) * Math.PI * 2 + 0.4;
        const dist = vfx.t * 30 * zoom;
        ctx.fillRect(x + Math.cos(angle) * dist, y + Math.sin(angle) * dist, 3 * zoom, 3 * zoom);
      }
    }
    ctx.restore();
  }

  private drawVitals(
    scene: SceneModel,
    entity: RenderEntity,
    point: { x: number; y: number },
    state: EntityRenderState,
  ): void {
    const { ctx } = this;
    const { theme, camera } = scene;
    const zoom = camera.zoom;
    const width = 44 * zoom;
    const height = 4 * zoom;
    const x = Math.round(point.x - width / 2);
    const y = Math.round(point.y - SPRITE_HEIGHT * zoom - 16 * zoom);

    // Prefer the animating value from the timeline; fall back to live state.
    if (entity.downed && state.bars.length === 0) return;

    const hpBar = state.bars.find((b) => b.stat === 'hp');
    const hp = hpBar ? hpBar.value : entity.hp;
    const hpMax = hpBar ? hpBar.max : entity.maxHp;
    const hpFraction = hpMax > 0 ? Math.max(0, Math.min(1, hp / hpMax)) : 0;

    ctx.fillStyle = theme.barTrack;
    ctx.fillRect(x, y, width, height);
    ctx.fillStyle = hpFraction < 0.3 ? theme.hpBarLow : theme.hpBar;
    ctx.fillRect(x, y, Math.round(width * hpFraction), height);

    if (entity.maxAether > 0) {
      const aetherBar = state.bars.find((b) => b.stat === 'aether');
      const aether = aetherBar ? aetherBar.value : entity.aether;
      const aetherMax = aetherBar ? aetherBar.max : entity.maxAether;
      const fraction = aetherMax > 0 ? Math.max(0, Math.min(1, aether / aetherMax)) : 0;
      ctx.fillStyle = theme.barTrack;
      ctx.fillRect(x, y + height + 1, width, height - 1);
      ctx.fillStyle = theme.aetherBar;
      ctx.fillRect(x, y + height + 1, Math.round(width * fraction), height - 1);
    }

    ctx.fillStyle = theme.textDim;
    ctx.font = `${Math.round(9 * zoom)}px ui-monospace, monospace`;
    ctx.textAlign = 'center';
    ctx.fillText(entity.name, point.x, y - 4 * zoom);
  }

  private drawNumerals(scene: SceneModel, point: { x: number; y: number }, state: EntityRenderState): void {
    const { ctx } = this;
    const { theme, camera } = scene;
    const zoom = camera.zoom;

    state.numerals.forEach((numeral, index) => {
      const rise = numeral.t * 34 * zoom;
      const alpha = numeral.t > 0.75 ? 1 - (numeral.t - 0.75) / 0.25 : 1;
      const colour = theme.numerals[numeral.style as keyof typeof theme.numerals] ?? '#ffffff';
      const size = numeral.style === 'critical' ? 20 : 15;

      ctx.save();
      ctx.globalAlpha = Math.max(0, alpha);
      ctx.font = `bold ${Math.round(size * zoom)}px ui-monospace, monospace`;
      ctx.textAlign = 'center';
      ctx.lineWidth = 3 * zoom;
      ctx.strokeStyle = 'rgba(10, 8, 20, 0.9)';
      const x = point.x + index * 14 * zoom;
      const y = point.y - SPRITE_HEIGHT * zoom - 24 * zoom - rise;
      ctx.strokeText(numeral.text, x, y);
      ctx.fillStyle = colour;
      ctx.fillText(numeral.text, x, y);
      ctx.restore();
    });
  }

  private drawBanner(theme: Theme, camera: CameraConfig, banner: { text: string; tone: string; t: number }): void {
    const { ctx } = this;
    // Slide in, hold, fade out.
    const alpha = banner.t < 0.15 ? banner.t / 0.15 : banner.t > 0.8 ? (1 - banner.t) / 0.2 : 1;

    ctx.save();
    ctx.globalAlpha = Math.max(0, Math.min(1, alpha));
    const height = 34;
    const y = 26;
    ctx.fillStyle = theme.panel;
    ctx.fillRect(0, y, camera.viewportWidth, height);
    ctx.fillStyle = theme.banner[banner.tone] ?? theme.text;
    ctx.fillRect(0, y, camera.viewportWidth, 2);
    ctx.font = 'bold 16px ui-monospace, monospace';
    ctx.textAlign = 'center';
    ctx.fillText(banner.text, camera.viewportWidth / 2, y + 23);
    ctx.restore();
  }
}
