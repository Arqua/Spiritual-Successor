/**
 * Status effects.
 *
 * A definition describes what a status does; an instance tracks how long a
 * particular actor has had it. Definitions live in content packs so designers
 * can add ailments without touching the resolver.
 */

import type { Element } from './elements.js';
import type { StatModifier } from './stats.js';

/** How a status ends. */
export type StatusDuration =
  /** Ticks down at the end of each round. */
  | { kind: 'rounds'; rounds: number }
  /** Lasts until the battle ends. */
  | { kind: 'battle' }
  /** Persists on the field until cured. */
  | { kind: 'persistent' };

export interface StatusDef {
  id: string;
  name: string;
  /** Shown to the player; the presentation layer owns icons and colours. */
  description?: string;
  element?: Element;
  duration: StatusDuration;
  /** Only one instance of a group can be active; a new one replaces the old. */
  exclusiveGroup?: string;
  /** Flat/elemental stat changes while active. */
  modifier?: StatModifier;
  /** Damage dealt at end of round, as a fraction of max HP. */
  degenPerRound?: number;
  /** Healing at end of round, as a fraction of max HP. */
  regenPerRound?: number;
  /** Blocks the actor's turn entirely (stun-likes). */
  preventsAction?: boolean;
  /** Blocks aether-costing arts but allows physical attacks. */
  preventsArts?: boolean;
  /** Per-turn probability the actor shakes it off before acting. */
  shakeOffChance?: number;
  /** Base probability the status is resisted outright, before luck. */
  baseResistChance?: number;
  /** Cleared when the bearer takes damage. */
  breaksOnDamage?: boolean;
  /** Marks the bearer as out of the fight. */
  incapacitates?: boolean;
}

export interface StatusInstance {
  statusId: string;
  /** Remaining rounds for `rounds` durations; ignored otherwise. */
  remaining: number;
  /** Who applied it, for attribution in the log. */
  sourceId?: string | null;
}

export function makeStatusInstance(def: StatusDef, sourceId: string | null = null): StatusInstance {
  return {
    statusId: def.id,
    remaining: def.duration.kind === 'rounds' ? def.duration.rounds : Number.POSITIVE_INFINITY,
    sourceId,
  };
}

/** Statuses whose durations are finite tick down; expired ids are returned. */
export function tickStatuses(instances: StatusInstance[]): { remaining: StatusInstance[]; expired: string[] } {
  const expired: string[] = [];
  const remaining: StatusInstance[] = [];
  for (const instance of instances) {
    if (!Number.isFinite(instance.remaining)) {
      remaining.push(instance);
      continue;
    }
    const next = instance.remaining - 1;
    if (next <= 0) expired.push(instance.statusId);
    else remaining.push({ ...instance, remaining: next });
  }
  return { remaining, expired };
}

/**
 * Landing chance for a status, tempered by the target's luck.
 *
 * Luck is a soft counter rather than an immunity: at 0 luck the base chance
 * applies unchanged, and each point of luck shaves a small slice off.
 */
export function statusLandChance(
  baseChance: number,
  def: StatusDef | undefined,
  targetLuck: number,
  luckDivisor = 400,
): number {
  const resisted = def?.baseResistChance ?? 0;
  const luckFactor = 1 - Math.min(0.75, Math.max(0, targetLuck) / luckDivisor);
  const chance = baseChance * (1 - resisted) * luckFactor;
  return Math.min(1, Math.max(0, chance));
}
