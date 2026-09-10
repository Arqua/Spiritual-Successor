/**
 * Reference battle demo.
 *
 * Runs the real rules core - no mocks, no scripted animation - and renders the
 * event stream it produces. The point is to prove the contract end to end:
 * `resolveRound()` computes a whole round instantly, `buildTimeline()` schedules
 * it, and `Playback` paces it out across frames.
 *
 * Character art is procedural. The library ships no assets.
 */

import { ContentRegistry } from '../src/core/registry.js';
import { starterPack } from '../src/data/starterPack.js';
import { createActorState, restore, type ActorState } from '../src/domain/actor.js';
import { makeMote } from '../src/domain/motes.js';
import { canAfford } from '../src/domain/summons.js';
import { createBattle } from '../src/battle/setup.js';
import { enemyCommands } from '../src/battle/ai.js';
import { resolveRound } from '../src/battle/resolve.js';
import { livingCombatants, type BattleCommand, type BattleState } from '../src/battle/state.js';

import { defaultCamera, followCamera, type CameraConfig, type Vec3 } from '../src/presentation/camera.js';
import { buildTimeline, type VitalsSnapshot } from '../src/presentation/timeline.js';
import { Playback } from '../src/presentation/playback.js';
import { DEFAULT_THEME } from '../src/presentation/theme.js';
import { CanvasRenderer, type RenderEntity, type SceneModel } from '../src/presentation/renderer/canvas2d.js';

const registry = ContentRegistry.from(starterPack);
registry.assertValid();
const ctx = registry.actorContext();

const LEVEL = 16;

function buildParty(): ActorState[] {
  const spec: [string, string[], Record<string, string>][] = [
    ['rell', ['boulder', 'loam'], { weapon: 'iron-blade', armor: 'warded-mail', shield: 'oak-shield' }],
    ['sunna', ['cinder', 'flare'], { weapon: 'ember-brand', armor: 'leather-vest' }],
    ['kite', ['zephyr', 'squall'], { weapon: 'gale-edge', armor: 'leather-vest' }],
    ['maren', ['hoarfrost', 'brine'], { armor: 'leather-vest', trinket: 'clarity-charm' }],
  ];
  return spec.map(([id, motes, gear]) => {
    const actor = createActorState(registry.actorDef(id)!, LEVEL, ctx);
    actor.motes = motes.map((m) => makeMote(m));
    actor.equipment = gear as ActorState['equipment'];
    restore(actor, ctx);
    return actor;
  });
}

let party = buildParty();
let state: BattleState = newBattle();

function newBattle(): BattleState {
  party = buildParty();
  return createBattle(
    {
      party,
      encounter: {
        id: 'demo',
        members: [
          { enemyId: 'thicket-crawler', slot: 0 },
          { enemyId: 'ash-wisp', slot: 1 },
          { enemyId: 'ash-wisp', slot: 2 },
        ],
      },
      seed: `demo-${Math.floor(Math.random() * 100000)}`,
    },
    registry,
  );
}

/** Formation positions. Party to the near side, foes to the far side. */
function worldPosition(side: 'party' | 'foe', slot: number, count: number): Vec3 {
  const spread = 1.15;
  const offset = (slot - (count - 1) / 2) * spread;
  return side === 'party' ? { x: 2.2 + offset, y: 2.2 - offset, z: 0 } : { x: -2.2 + offset, y: -2.2 - offset, z: 0 };
}

function snapshotVitals(battle: BattleState): VitalsSnapshot {
  const vitals: VitalsSnapshot = {};
  for (const c of battle.combatants) {
    vitals[c.id] = { hp: c.hp, maxHp: c.maxHp, aether: c.aether, maxAether: c.maxAether };
  }
  return vitals;
}

/** Human-readable names for banners, pulled straight from content. */
function labels(): Record<string, string> {
  const out: Record<string, string> = {};
  for (const [id, def] of registry.arts) out[id] = def.name;
  for (const [id, def] of registry.motes) out[id] = def.name;
  for (const [id, def] of registry.summons) out[id] = def.name;
  for (const [id, def] of registry.classes) out[id] = def.name;
  for (const [id, def] of registry.statuses) out[id] = def.name;
  return out;
}

/** A simple scripted party, so the demo plays itself. */
function partyCommands(battle: BattleState): BattleCommand[] {
  const commands: BattleCommand[] = [];
  const foes = livingCombatants(battle, 'foe');
  if (foes.length === 0) return commands;
  const weakest = foes.slice().sort((a, b) => a.hp - b.hp)[0]!;

  for (const member of livingCombatants(battle, 'party')) {
    const actor = member.actor;
    if (!actor) continue;

    const hurt = livingCombatants(battle, 'party')
      .slice()
      .sort((a, b) => a.hp / a.maxHp - b.hp / b.maxHp)[0];

    if (actor.defId === 'maren' && hurt && hurt.hp / hurt.maxHp < 0.6 && member.aether >= 6) {
      commands.push({ kind: 'art', combatantId: member.id, artId: 'mend', targetId: hurt.id });
      continue;
    }

    const summon = registry.summonDef('conflagrant');
    if (summon && canAfford(summon, actor.motes, registry.moteDef)) {
      commands.push({ kind: 'summon', combatantId: member.id, summonId: summon.id });
      continue;
    }

    const setMote = actor.motes.find((m) => m.state === 'set');
    if (setMote && battle.round % 3 === 0) {
      commands.push({ kind: 'unleash', combatantId: member.id, moteId: setMote.defId, targetId: weakest.id });
      continue;
    }

    const arts = ['stone-fist', 'ember', 'gale-cut', 'frostbite'];
    const known = arts.find((a) => {
      const def = registry.artDef(a);
      return def && member.aether >= def.cost && battle.round % 2 === 0;
    });
    if (known) {
      commands.push({ kind: 'art', combatantId: member.id, artId: known, targetId: weakest.id });
      continue;
    }

    commands.push({ kind: 'attack', combatantId: member.id, targetId: weakest.id });
  }
  return commands;
}

// --- wiring --------------------------------------------------------------

const canvas = document.getElementById('stage') as HTMLCanvasElement;
const statusEl = document.getElementById('status') as HTMLElement;
const renderer = new CanvasRenderer(canvas);
const playback = new Playback();
// Nudge the world down so the characters sit in the middle of the frame
// rather than riding high with dead space beneath them.
let camera: CameraConfig = { ...defaultCamera(canvas.width, canvas.height), offsetY: 46 };
const nameLabels = labels();

function entities(battle: BattleState): RenderEntity[] {
  const bySide = { party: 0, foe: 0 };
  const counts = {
    party: battle.combatants.filter((c) => c.side === 'party').length,
    foe: battle.combatants.filter((c) => c.side === 'foe').length,
  };
  return battle.combatants.map((c) => {
    const slot = bySide[c.side]++;
    return {
      id: c.id,
      world: worldPosition(c.side, slot, counts[c.side]),
      facing: c.side === 'party' ? ('north' as const) : ('south' as const),
      side: c.side,
      element: c.innate,
      name: c.name,
      downed: c.downed,
      hp: c.hp,
      maxHp: c.maxHp,
      aether: c.aether,
      maxAether: c.maxAether,
    };
  });
}

function startRound(): void {
  if (state.phase === 'ended') {
    state = newBattle();
    return;
  }
  const vitals = snapshotVitals(state);
  const commands = [...partyCommands(state), ...enemyCommands(state, registry)];
  const { events } = resolveRound(state, commands, registry);
  playback.load(buildTimeline(events, { vitals, labels: nameLabels }));
}

let lastFrame = performance.now();
let idleMs = 0;
let cameraTarget: Vec3 = { x: 0, y: 0, z: 0 };

function frame(now: number): void {
  const dt = Math.min(64, now - lastFrame);
  lastFrame = now;

  const current = entities(state);

  if (playback.isPlaying) {
    const active = playback.advance(dt);
    // A hitstop step freezes the scene the moment it begins.
    for (const step of playback.justStarted()) {
      if (step.action.kind === 'hitstop') playback.requestHitstop(step.durationMs);
    }
    // Follow whoever the timeline is pointing at, drifting back to centre
    // between turns so the camera never sits hard on one side.
    for (const { step } of active) {
      if (step.action.kind === 'camera') {
        // Hoisted out of the callback: narrowing does not survive into a closure.
        const focusId = step.action.focusId;
        const focus = current.find((e) => e.id === focusId);
        if (focus) cameraTarget = { x: focus.world.x * 0.45, y: focus.world.y * 0.45, z: 0 };
      }
    }
    idleMs = 0;
  } else {
    cameraTarget = { x: 0, y: 0, z: 0 };
    idleMs += dt;
    if (idleMs > 450) {
      startRound();
      idleMs = 0;
    }
  }

  camera = followCamera(camera, cameraTarget, 0.06, dt);

  const scene: SceneModel = {
    entities: current,
    floor: { width: 9, height: 9 },
    camera,
    theme: DEFAULT_THEME,
  };
  renderer.draw(scene, playback, now);

  statusEl.textContent =
    `round ${state.round}  ·  ${state.phase}` +
    (state.outcome ? `  ·  ${state.outcome}` : '') +
    `  ·  timeline ${Math.round(playback.now)}/${Math.round(playback.totalMs)}ms`;

  requestAnimationFrame(frame);
}

document.getElementById('step')?.addEventListener('click', () => {
  if (!playback.isPlaying) startRound();
});
document.getElementById('skip')?.addEventListener('click', () => playback.skip());
document.getElementById('restart')?.addEventListener('click', () => {
  state = newBattle();
  playback.load({ steps: [], durationMs: 0 });
});
document.getElementById('speed')?.addEventListener('click', (event) => {
  playback.speed = playback.speed === 1 ? 2 : playback.speed === 2 ? 0.5 : 1;
  (event.target as HTMLElement).textContent = `speed ${playback.speed}x`;
});

// Expose for the screenshot harness so it can drive deterministic frames.
(window as unknown as Record<string, unknown>).__demo = {
  startRound,
  playback,
  getState: () => state,
};

startRound();
requestAnimationFrame(frame);
