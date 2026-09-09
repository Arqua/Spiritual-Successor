/**
 * Sprite orientation and framing.
 *
 * Pixel-art characters in a projected world have to decide which way they are
 * facing relative to the camera. This module owns that decision and the frame
 * timing that goes with it. Pure functions, no engine types.
 */

import type { Facing } from '../field/grid.js';

/** Eight compass directions, clockwise from south (toward the viewer). */
export const DIRECTIONS = ['s', 'sw', 'w', 'nw', 'n', 'ne', 'e', 'se'] as const;
export type Direction = (typeof DIRECTIONS)[number];

/** World-space angle in degrees for each cardinal facing. */
const FACING_ANGLES: Record<Facing, number> = {
  south: 0,
  west: 90,
  north: 180,
  east: 270,
};

/**
 * Pick the sprite direction for an actor facing `facing` when the camera is
 * rotated by `cameraYaw` degrees.
 *
 * With 8-directional art this is what keeps characters looking correct as the
 * camera orbits. With a single-direction sprite sheet, ignore the result and
 * billboard instead.
 */
export function spriteDirection(facing: Facing, cameraYaw = 0): Direction {
  const relative = ((FACING_ANGLES[facing] - cameraYaw) % 360 + 360) % 360;
  const index = Math.round(relative / 45) % 8;
  return DIRECTIONS[index] as Direction;
}

/** Whether a direction faces away from the camera, for back-sprite selection. */
export function facesAway(direction: Direction): boolean {
  return direction === 'n' || direction === 'ne' || direction === 'nw';
}

/**
 * Y-axis billboarding angle.
 *
 * Returns the rotation a sprite quad needs so it faces the camera while
 * staying upright. This is the recommended mode: full billboarding makes
 * sprites appear to swivel and lifts their feet off the ground.
 */
export function billboardYaw(cameraYaw: number): number {
  return -cameraYaw;
}

export interface AnimationClip {
  /** Frame indices into the sprite sheet row. */
  frames: number[];
  /** Milliseconds per frame. */
  frameMs: number;
  loop: boolean;
}

export type ActorPose = 'idle' | 'walk' | 'attack' | 'cast' | 'hurt' | 'down' | 'victory';

/** Default clip timings. Content can override per actor. */
export const DEFAULT_CLIPS: Record<ActorPose, AnimationClip> = {
  idle: { frames: [0, 1, 2, 1], frameMs: 180, loop: true },
  walk: { frames: [0, 1, 2, 3], frameMs: 110, loop: true },
  attack: { frames: [0, 1, 2], frameMs: 70, loop: false },
  cast: { frames: [0, 1], frameMs: 120, loop: false },
  hurt: { frames: [0], frameMs: 200, loop: false },
  down: { frames: [0], frameMs: 1, loop: false },
  victory: { frames: [0, 1], frameMs: 240, loop: true },
};

/** Current frame index for a clip at a given elapsed time. */
export function frameAt(clip: AnimationClip, elapsedMs: number): number {
  if (clip.frames.length === 0) return 0;
  const step = Math.floor(Math.max(0, elapsedMs) / clip.frameMs);
  const index = clip.loop ? step % clip.frames.length : Math.min(step, clip.frames.length - 1);
  return clip.frames[index] as number;
}

export function clipDuration(clip: AnimationClip): number {
  return clip.frames.length * clip.frameMs;
}

/**
 * Idle bob offset in pixels.
 *
 * A tiny vertical oscillation stops a static sprite from looking dead. Driven
 * by absolute time plus a per-entity phase so a row of characters does not
 * bob in lockstep.
 */
export function idleBob(timeMs: number, phase: number, amplitude = 1.5, periodMs = 1600): number {
  return Math.sin((timeMs / periodMs + phase) * Math.PI * 2) * amplitude;
}

/** Stable phase offset derived from an id, so it survives reloads. */
export function phaseFromId(id: string): number {
  let hash = 0;
  for (let i = 0; i < id.length; i++) hash = (hash * 31 + id.charCodeAt(i)) >>> 0;
  return (hash % 1000) / 1000;
}
