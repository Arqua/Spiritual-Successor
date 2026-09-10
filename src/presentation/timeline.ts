/**
 * Event stream to animation timeline.
 *
 * `resolveRound()` returns an entire round instantly as an ordered event list.
 * This module turns that list into a *timed* schedule: a flat array of steps,
 * each with a start time, a duration, and a track. Playback then becomes a
 * matter of asking "which steps are active at time T", which is trivial to
 * drive from any engine's frame loop.
 *
 * This is the load-bearing piece of the presentation contract, and it is
 * deliberately pure - no canvas, no engine types, no clock. Given the same
 * events it produces the same schedule every time, which means it can be unit
 * tested and ported without a renderer attached.
 *
 * Concurrency is expressed by giving steps overlapping time ranges. An
 * all-foes spell hits every target in the same beat rather than sequentially,
 * because the cursor only advances between beats.
 */

import type { GameEvent } from '../core/events.js';
import type { Element } from '../domain/elements.js';

export const GLOBAL_TRACK = '@global';

export type StepAction =
  /** Play a named pose on an actor. */
  | { kind: 'pose'; pose: string }
  /** Lunge toward a target and return. */
  | { kind: 'lunge'; towardId: string }
  /** Tint flash, used for impacts. */
  | { kind: 'flash'; color: string; intensity: number }
  /** Positional shake. */
  | { kind: 'shake'; intensity: number }
  /** A floating number or word. */
  | { kind: 'numeral'; text: string; style: NumeralStyle }
  /** Animate a stat bar between two values. */
  | { kind: 'bar'; stat: 'hp' | 'aether'; from: number; to: number; max: number }
  /** Freeze the whole scene briefly to sell an impact. */
  | { kind: 'hitstop' }
  /** Full-width text banner. */
  | { kind: 'banner'; text: string; tone: 'neutral' | 'good' | 'bad' | 'grand' }
  /** An elemental effect burst at an entity. */
  | { kind: 'vfx'; effect: string; element: Element | null }
  /** Point the camera at an entity. */
  | { kind: 'camera'; focusId: string }
  /** Fade an entity out as it falls. */
  | { kind: 'collapse' }
  /** Fade an entity back in. */
  | { kind: 'rise' }
  /** Screen-wide flash, for summons. */
  | { kind: 'screen-flash'; color: string }
  /** Named sound cue; the host decides what it maps to. */
  | { kind: 'sfx'; cue: string };

export type NumeralStyle = 'damage' | 'critical' | 'weak' | 'strong' | 'heal' | 'miss' | 'status' | 'aether';

export interface AnimationStep {
  /** Entity id, or GLOBAL_TRACK for scene-wide steps. */
  track: string;
  startMs: number;
  durationMs: number;
  action: StepAction;
}

export interface Timeline {
  steps: AnimationStep[];
  durationMs: number;
}

/** Beat lengths, in milliseconds. Tune these to change the whole feel. */
export interface TimelineTiming {
  turnStart: number;
  windUp: number;
  impact: number;
  hitstop: number;
  numeralFloat: number;
  betweenHits: number;
  statusApply: number;
  moteUnleash: number;
  classChange: number;
  summonCutaway: number;
  collapse: number;
  banner: number;
  roundGap: number;
}

export const DEFAULT_TIMING: TimelineTiming = Object.freeze({
  turnStart: 160,
  windUp: 240,
  impact: 300,
  hitstop: 90,
  numeralFloat: 700,
  betweenHits: 130,
  statusApply: 320,
  moteUnleash: 520,
  classChange: 600,
  summonCutaway: 1400,
  collapse: 520,
  banner: 900,
  roundGap: 120,
});

/** Vitals at the start of the round, so bars can animate from the right value. */
export interface VitalsSnapshot {
  [combatantId: string]: { hp: number; maxHp: number; aether: number; maxAether: number };
}

export interface TimelineOptions {
  timing?: Partial<TimelineTiming>;
  vitals?: VitalsSnapshot;
  /** Names for banners and numerals, keyed by id. */
  labels?: Record<string, string>;
}

/**
 * Build a timeline from one round's events.
 *
 * The cursor advances beat by beat. Steps that should be simultaneous are
 * emitted at the same cursor value; the cursor only moves when the next thing
 * genuinely follows the last.
 */
export function buildTimeline(events: readonly GameEvent[], options: TimelineOptions = {}): Timeline {
  const timing: TimelineTiming = { ...DEFAULT_TIMING, ...options.timing };
  const labels = options.labels ?? {};
  const steps: AnimationStep[] = [];

  // Track vitals as we walk so bar steps know their start value.
  const vitals: VitalsSnapshot = {};
  for (const [id, value] of Object.entries(options.vitals ?? {})) vitals[id] = { ...value };

  let cursor = 0;
  const emit = (step: AnimationStep): void => {
    steps.push(step);
  };
  const label = (id: string): string => labels[id] ?? id;

  for (const event of events) {
    switch (event.type) {
      case 'round-start':
        break;

      case 'turn-start': {
        emit({ track: GLOBAL_TRACK, startMs: cursor, durationMs: timing.turnStart, action: { kind: 'camera', focusId: event.combatantId } });
        cursor += timing.turnStart;
        break;
      }

      case 'turn-skipped': {
        emit({ track: event.combatantId, startMs: cursor, durationMs: timing.numeralFloat, action: { kind: 'numeral', text: readableReason(event.reason), style: 'status' } });
        cursor += timing.statusApply;
        break;
      }

      case 'action-declared': {
        const isAttack = event.action === 'attack';
        emit({ track: event.combatantId, startMs: cursor, durationMs: timing.windUp, action: { kind: 'pose', pose: isAttack ? 'attack' : 'cast' } });
        if (isAttack && event.targetIds[0]) {
          emit({ track: event.combatantId, startMs: cursor, durationMs: timing.windUp, action: { kind: 'lunge', towardId: event.targetIds[0] } });
        }
        if (!isAttack && event.action.startsWith('art:')) {
          emit({ track: GLOBAL_TRACK, startMs: cursor, durationMs: timing.banner, action: { kind: 'banner', text: label(event.action.slice(4)), tone: 'neutral' } });
        }
        cursor += timing.windUp;
        break;
      }

      case 'damage': {
        const style = damageStyle(event.critical, event.effective);
        emit({ track: event.targetId, startMs: cursor, durationMs: timing.impact, action: { kind: 'flash', color: '#ffffff', intensity: event.critical ? 1 : 0.75 } });
        emit({ track: event.targetId, startMs: cursor, durationMs: timing.impact, action: { kind: 'shake', intensity: event.critical ? 6 : 3 } });
        emit({ track: event.targetId, startMs: cursor, durationMs: timing.impact, action: { kind: 'pose', pose: 'hurt' } });
        emit({ track: event.targetId, startMs: cursor, durationMs: timing.impact, action: { kind: 'vfx', effect: 'impact', element: event.element } });
        emit({ track: event.targetId, startMs: cursor, durationMs: timing.numeralFloat, action: { kind: 'numeral', text: String(event.amount), style } });
        emit({ track: GLOBAL_TRACK, startMs: cursor, durationMs: timing.hitstop, action: { kind: 'hitstop' } });
        emit({ track: GLOBAL_TRACK, startMs: cursor, durationMs: 1, action: { kind: 'sfx', cue: event.critical ? 'hit-critical' : 'hit' } });
        emitBar(emit, vitals, event.targetId, 'hp', -event.amount, cursor, timing.impact);
        cursor += timing.impact + timing.betweenHits;
        break;
      }

      case 'heal': {
        emit({ track: event.targetId, startMs: cursor, durationMs: timing.impact, action: { kind: 'vfx', effect: 'heal', element: null } });
        emit({ track: event.targetId, startMs: cursor, durationMs: timing.numeralFloat, action: { kind: 'numeral', text: String(event.amount), style: 'heal' } });
        emit({ track: GLOBAL_TRACK, startMs: cursor, durationMs: 1, action: { kind: 'sfx', cue: 'heal' } });
        emitBar(emit, vitals, event.targetId, 'hp', event.amount, cursor, timing.impact);
        cursor += timing.impact;
        break;
      }

      case 'miss': {
        emit({ track: event.targetId, startMs: cursor, durationMs: timing.numeralFloat, action: { kind: 'numeral', text: 'MISS', style: 'miss' } });
        emit({ track: GLOBAL_TRACK, startMs: cursor, durationMs: 1, action: { kind: 'sfx', cue: 'miss' } });
        cursor += timing.impact;
        break;
      }

      case 'aether-spent':
        emitBar(emit, vitals, event.combatantId, 'aether', -event.amount, cursor, timing.impact);
        break;

      case 'aether-restored': {
        emit({ track: event.combatantId, startMs: cursor, durationMs: timing.numeralFloat, action: { kind: 'numeral', text: `+${event.amount}`, style: 'aether' } });
        emitBar(emit, vitals, event.combatantId, 'aether', event.amount, cursor, timing.impact);
        cursor += timing.impact;
        break;
      }

      case 'status-applied': {
        emit({ track: event.targetId, startMs: cursor, durationMs: timing.statusApply, action: { kind: 'vfx', effect: `status:${event.statusId}`, element: null } });
        emit({ track: event.targetId, startMs: cursor, durationMs: timing.numeralFloat, action: { kind: 'numeral', text: label(event.statusId).toUpperCase(), style: 'status' } });
        cursor += timing.statusApply;
        break;
      }

      case 'status-resisted': {
        emit({ track: event.targetId, startMs: cursor, durationMs: timing.numeralFloat, action: { kind: 'numeral', text: 'RESIST', style: 'miss' } });
        cursor += timing.statusApply;
        break;
      }

      case 'status-expired':
        emit({ track: event.targetId, startMs: cursor, durationMs: timing.statusApply, action: { kind: 'vfx', effect: 'status-clear', element: null } });
        break;

      case 'mote-unleashed': {
        emit({ track: event.combatantId, startMs: cursor, durationMs: timing.moteUnleash, action: { kind: 'pose', pose: 'cast' } });
        emit({ track: event.combatantId, startMs: cursor, durationMs: timing.moteUnleash, action: { kind: 'vfx', effect: 'mote-unleash', element: event.element } });
        emit({ track: GLOBAL_TRACK, startMs: cursor, durationMs: timing.banner, action: { kind: 'banner', text: label(event.moteId), tone: 'good' } });
        emit({ track: GLOBAL_TRACK, startMs: cursor, durationMs: 1, action: { kind: 'sfx', cue: 'mote' } });
        cursor += timing.moteUnleash;
        break;
      }

      case 'mote-recovered':
        emit({ track: event.combatantId, startMs: cursor, durationMs: timing.statusApply, action: { kind: 'vfx', effect: 'mote-return', element: null } });
        break;

      case 'class-changed': {
        emit({ track: event.combatantId, startMs: cursor, durationMs: timing.classChange, action: { kind: 'vfx', effect: 'class-change', element: null } });
        emit({ track: GLOBAL_TRACK, startMs: cursor, durationMs: timing.banner, action: { kind: 'banner', text: label(event.toClassId), tone: 'grand' } });
        cursor += timing.classChange;
        break;
      }

      case 'summon-called': {
        emit({ track: GLOBAL_TRACK, startMs: cursor, durationMs: timing.summonCutaway, action: { kind: 'banner', text: label(event.summonId), tone: 'grand' } });
        emit({ track: GLOBAL_TRACK, startMs: cursor, durationMs: timing.summonCutaway, action: { kind: 'screen-flash', color: '#ffffff' } });
        emit({ track: event.combatantId, startMs: cursor, durationMs: timing.summonCutaway, action: { kind: 'pose', pose: 'cast' } });
        emit({ track: GLOBAL_TRACK, startMs: cursor, durationMs: 1, action: { kind: 'sfx', cue: 'summon' } });
        cursor += timing.summonCutaway;
        break;
      }

      case 'downed': {
        emit({ track: event.combatantId, startMs: cursor, durationMs: timing.collapse, action: { kind: 'pose', pose: 'down' } });
        emit({ track: event.combatantId, startMs: cursor, durationMs: timing.collapse, action: { kind: 'collapse' } });
        emit({ track: GLOBAL_TRACK, startMs: cursor, durationMs: 1, action: { kind: 'sfx', cue: 'down' } });
        cursor += timing.collapse;
        break;
      }

      case 'revived': {
        emit({ track: event.combatantId, startMs: cursor, durationMs: timing.collapse, action: { kind: 'rise' } });
        emit({ track: event.combatantId, startMs: cursor, durationMs: timing.numeralFloat, action: { kind: 'numeral', text: String(event.hp), style: 'heal' } });
        setVital(vitals, event.combatantId, 'hp', event.hp);
        cursor += timing.collapse;
        break;
      }

      case 'flee-succeeded':
        emit({ track: GLOBAL_TRACK, startMs: cursor, durationMs: timing.banner, action: { kind: 'banner', text: 'Escaped', tone: 'neutral' } });
        cursor += timing.banner;
        break;

      case 'flee-failed':
        emit({ track: GLOBAL_TRACK, startMs: cursor, durationMs: timing.banner, action: { kind: 'banner', text: "Couldn't escape", tone: 'bad' } });
        cursor += timing.banner;
        break;

      case 'battle-ended': {
        const tone = event.outcome === 'victory' ? 'good' : event.outcome === 'defeat' ? 'bad' : 'neutral';
        emit({ track: GLOBAL_TRACK, startMs: cursor, durationMs: timing.banner, action: { kind: 'banner', text: outcomeText(event.outcome), tone } });
        cursor += timing.banner;
        break;
      }

      case 'reward':
        emit({ track: GLOBAL_TRACK, startMs: cursor, durationMs: timing.banner, action: { kind: 'banner', text: `+${event.xp} XP   +${event.coin}`, tone: 'good' } });
        cursor += timing.banner;
        break;

      case 'message':
        emit({ track: GLOBAL_TRACK, startMs: cursor, durationMs: timing.banner, action: { kind: 'banner', text: event.text, tone: 'neutral' } });
        cursor += timing.banner;
        break;

      case 'round-end':
        cursor += timing.roundGap;
        break;

      default:
        break;
    }
  }

  const durationMs = steps.reduce((max, step) => Math.max(max, step.startMs + step.durationMs), 0);
  return { steps, durationMs };
}

function emitBar(
  emit: (step: AnimationStep) => void,
  vitals: VitalsSnapshot,
  id: string,
  stat: 'hp' | 'aether',
  delta: number,
  startMs: number,
  durationMs: number,
): void {
  const entry = vitals[id];
  if (!entry) return;
  const max = stat === 'hp' ? entry.maxHp : entry.maxAether;
  const from = stat === 'hp' ? entry.hp : entry.aether;
  const to = Math.max(0, Math.min(max, from + delta));
  if (stat === 'hp') entry.hp = to;
  else entry.aether = to;
  emit({ track: id, startMs, durationMs, action: { kind: 'bar', stat, from, to, max } });
}

function setVital(vitals: VitalsSnapshot, id: string, stat: 'hp' | 'aether', value: number): void {
  const entry = vitals[id];
  if (!entry) return;
  if (stat === 'hp') entry.hp = value;
  else entry.aether = value;
}

/**
 * Numeral styling from the damage event's own data.
 *
 * `effective` is the elemental multiplier the rules layer already computed,
 * which is exactly why it is on the event: the presentation layer should not
 * be recomputing affinity to decide how a hit reads.
 */
export function damageStyle(critical: boolean, effective: number): NumeralStyle {
  if (critical) return 'critical';
  if (effective >= 1.2) return 'strong';
  if (effective <= 0.8) return 'weak';
  return 'damage';
}

function outcomeText(outcome: 'victory' | 'defeat' | 'fled'): string {
  if (outcome === 'victory') return 'Victory';
  if (outcome === 'defeat') return 'Defeat';
  return 'Escaped';
}

function readableReason(reason: string): string {
  switch (reason) {
    case 'insufficient-aether':
      return 'NO AETHER';
    case 'insufficient-motes':
      return 'NO MOTES';
    case 'arts-sealed':
      return 'SEALED';
    default:
      return reason.replace(/-/g, ' ').toUpperCase();
  }
}
