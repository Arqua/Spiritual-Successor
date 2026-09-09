/**
 * Timeline playback.
 *
 * Owns the clock. Advance it with the frame delta and ask what is active; the
 * renderer draws whatever comes back. Nothing here touches a canvas, so the
 * same scheduler drives a 2.5D renderer, a text log, or a test.
 *
 * Hitstop is handled here rather than in the renderer because it must freeze
 * the whole scene consistently - if each entity decided on its own, a hit
 * would smear.
 */

import type { AnimationStep, Timeline } from './timeline.js';
import { GLOBAL_TRACK } from './timeline.js';

export interface ActiveStep {
  step: AnimationStep;
  /** Normalized progress through this step, 0..1. */
  t: number;
  elapsedMs: number;
}

export type PlaybackState = 'idle' | 'playing' | 'finished';

export class Playback {
  private timeline: Timeline = { steps: [], durationMs: 0 };
  private clockMs = 0;
  /**
   * Clock value before the last `advance`. Starts below zero so that steps
   * scheduled at exactly time 0 are reported by the first `justStarted` call
   * rather than being skipped by an exclusive lower bound.
   */
  private previousClockMs = -1;
  /** Whether `advance` has run since the timeline was loaded. */
  private hasAdvanced = false;
  private hitstopMs = 0;
  private state: PlaybackState = 'idle';
  /** Playback rate. 2 is double speed, used by a "fast battle" option. */
  speed = 1;

  load(timeline: Timeline): void {
    this.timeline = timeline;
    this.clockMs = 0;
    this.previousClockMs = -1;
    this.hasAdvanced = false;
    this.hitstopMs = 0;
    this.state = timeline.steps.length > 0 ? 'playing' : 'finished';
  }

  /** Advance the clock. Returns the steps active after advancing. */
  advance(dtMs: number): ActiveStep[] {
    if (this.state !== 'playing') return [];

    let delta = Math.max(0, dtMs) * this.speed;
    // On the very first advance the previous bound stays below zero, so steps
    // scheduled at exactly time 0 still fall inside the (previous, current]
    // window. Afterwards it tracks the clock as it was before this frame.
    this.previousClockMs = this.hasAdvanced ? this.clockMs : -1;
    this.hasAdvanced = true;

    // Hitstop eats frame time before the clock sees it, freezing everything.
    if (this.hitstopMs > 0) {
      const consumed = Math.min(this.hitstopMs, delta);
      this.hitstopMs -= consumed;
      delta -= consumed;
      if (delta <= 0) return this.activeAt(this.clockMs);
    }

    this.clockMs += delta;
    if (this.clockMs >= this.timeline.durationMs) {
      this.clockMs = this.timeline.durationMs;
      this.state = 'finished';
    }
    return this.activeAt(this.clockMs);
  }

  /** Steps overlapping a given time. */
  activeAt(timeMs: number): ActiveStep[] {
    const active: ActiveStep[] = [];
    for (const step of this.timeline.steps) {
      if (timeMs < step.startMs) continue;
      const elapsedMs = timeMs - step.startMs;
      if (elapsedMs > step.durationMs) continue;
      const t = step.durationMs <= 0 ? 1 : Math.min(1, elapsedMs / step.durationMs);
      active.push({ step, t, elapsedMs });
    }
    return active;
  }

  /** Steps active for one entity, which is what a sprite needs each frame. */
  activeForTrack(track: string, timeMs = this.clockMs): ActiveStep[] {
    return this.activeAt(timeMs).filter((entry) => entry.step.track === track);
  }

  /**
   * Steps that began during the most recent `advance`, for one-shot triggers
   * like sound cues.
   *
   * The window is (previous clock, current clock], measured from the actual
   * last advance rather than a caller-supplied duration. That guarantees every
   * step fires exactly once: a caller-supplied window either drops cues when
   * it is shorter than the frame delta, or repeats them when it is longer.
   */
  justStarted(): AnimationStep[] {
    return this.timeline.steps.filter(
      (step) => step.startMs > this.previousClockMs && step.startMs <= this.clockMs,
    );
  }

  /** Request a scene-wide freeze. Called when a hitstop step begins. */
  requestHitstop(durationMs: number): void {
    this.hitstopMs = Math.max(this.hitstopMs, durationMs);
  }

  /** Jump to the end. For a "skip animation" button. */
  skip(): void {
    this.previousClockMs = this.hasAdvanced ? this.clockMs : -1;
    this.hasAdvanced = true;
    this.clockMs = this.timeline.durationMs;
    this.hitstopMs = 0;
    this.state = 'finished';
  }

  get isFinished(): boolean {
    return this.state === 'finished';
  }

  get isPlaying(): boolean {
    return this.state === 'playing';
  }

  get now(): number {
    return this.clockMs;
  }

  get totalMs(): number {
    return this.timeline.durationMs;
  }

  /** 0..1 through the whole timeline, for a progress indicator. */
  get progress(): number {
    if (this.timeline.durationMs <= 0) return 1;
    return this.clockMs / this.timeline.durationMs;
  }
}

/**
 * Collapse the steps active for one entity into a render state.
 *
 * Several steps can target the same entity at once - a flash and a shake and a
 * pose - so this folds them into the single set of values a sprite needs. Last
 * writer wins for discrete values like pose; continuous values accumulate.
 */
export interface EntityRenderState {
  pose: string;
  flashIntensity: number;
  flashColor: string;
  shakeOffset: number;
  opacity: number;
  lungeToward: string | null;
  lungeAmount: number;
  numerals: { text: string; style: string; t: number }[];
  bars: { stat: 'hp' | 'aether'; value: number; max: number }[];
  vfx: { effect: string; element: string | null; t: number }[];
}

export function emptyRenderState(): EntityRenderState {
  return {
    pose: 'idle',
    flashIntensity: 0,
    flashColor: '#ffffff',
    shakeOffset: 0,
    opacity: 1,
    lungeToward: null,
    lungeAmount: 0,
    numerals: [],
    bars: [],
    vfx: [],
  };
}

export function foldRenderState(active: readonly ActiveStep[]): EntityRenderState {
  const out = emptyRenderState();

  for (const { step, t } of active) {
    const action = step.action;
    switch (action.kind) {
      case 'pose':
        out.pose = action.pose;
        break;
      case 'flash':
        // Flash decays across its own duration.
        out.flashIntensity = Math.max(out.flashIntensity, action.intensity * (1 - t));
        out.flashColor = action.color;
        break;
      case 'shake': {
        // Decaying oscillation, so the shake settles rather than cutting out.
        const decay = 1 - t;
        out.shakeOffset += Math.sin(t * Math.PI * 8) * action.intensity * decay;
        break;
      }
      case 'lunge':
        out.lungeToward = action.towardId;
        // Out and back within the step.
        out.lungeAmount = Math.sin(t * Math.PI);
        break;
      case 'collapse':
        out.opacity = Math.min(out.opacity, 1 - t * 0.65);
        out.pose = 'down';
        break;
      case 'rise':
        out.opacity = Math.max(out.opacity, t);
        break;
      case 'numeral':
        out.numerals.push({ text: action.text, style: action.style, t });
        break;
      case 'bar': {
        const value = action.from + (action.to - action.from) * easeBar(t);
        out.bars.push({ stat: action.stat, value, max: action.max });
        break;
      }
      case 'vfx':
        out.vfx.push({ effect: action.effect, element: action.element, t });
        break;
      default:
        break;
    }
  }
  return out;
}

/** Bars drain fast then settle, which reads better than a linear slide. */
function easeBar(t: number): number {
  return 1 - Math.pow(1 - t, 3);
}

/** Scene-wide state folded from the global track. */
export interface SceneRenderState {
  banner: { text: string; tone: string; t: number } | null;
  screenFlash: number;
  cameraFocusId: string | null;
  hitstopActive: boolean;
}

export function foldSceneState(active: readonly ActiveStep[]): SceneRenderState {
  const out: SceneRenderState = { banner: null, screenFlash: 0, cameraFocusId: null, hitstopActive: false };
  for (const { step, t } of active) {
    if (step.track !== GLOBAL_TRACK) continue;
    const action = step.action;
    switch (action.kind) {
      case 'banner':
        out.banner = { text: action.text, tone: action.tone, t };
        break;
      case 'screen-flash':
        out.screenFlash = Math.max(out.screenFlash, 1 - t);
        break;
      case 'camera':
        out.cameraFocusId = action.focusId;
        break;
      case 'hitstop':
        out.hitstopActive = true;
        break;
      default:
        break;
    }
  }
  return out;
}
