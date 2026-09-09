/**
 * Motes: bindable elemental spirits.
 *
 * A mote sits in one of three states.
 *
 *   set        - bound to an actor. Contributes its stat bonus and counts
 *                toward the actor's class.
 *   standby    - it has been unleashed this battle. Its bonus and class
 *                contribution are gone until it returns, but it can be spent
 *                to call a summon.
 *   recovering - spent on a summon. Returns to `set` after a few rounds.
 *
 * The loop is the interesting part: unleashing a mote is an immediate tactical
 * gain that costs you stats and can knock you out of your class mid-fight, and
 * summoning cashes those in for a large hit at the cost of a longer downtime.
 * Every state change here is reversible and fully serializable.
 */

import type { Element } from './elements.js';
import type { StatModifier } from './stats.js';

export type MoteState = 'set' | 'standby' | 'recovering';

/** What happens when a mote is unleashed in battle. */
export type MoteEffect =
  | { kind: 'damage'; power: number; targeting: MoteTargeting; ignoresDefense?: boolean }
  | { kind: 'heal'; power: number; targeting: MoteTargeting }
  | { kind: 'revive'; hpFraction: number }
  | { kind: 'status'; statusId: string; chance: number; targeting: MoteTargeting }
  | { kind: 'buff'; modifier: StatModifier; rounds: number; targeting: MoteTargeting }
  | { kind: 'restore-aether'; amount: number; targeting: MoteTargeting }
  | { kind: 'utility'; tag: string };

export type MoteTargeting = 'one-foe' | 'all-foes' | 'self' | 'one-ally' | 'all-allies';

export interface MoteDef {
  id: string;
  name: string;
  element: Element;
  description?: string;
  /** Applied to the bearer while the mote is `set`. */
  bonus?: StatModifier;
  /** Resolved when the mote is unleashed. */
  effect: MoteEffect;
  /** Rounds spent `recovering` after being spent on a summon. */
  recoveryRounds?: number;
  /** Optional field ability granted while set, e.g. for overworld puzzles. */
  fieldAbilityId?: string;
}

export interface MoteInstance {
  defId: string;
  state: MoteState;
  /** Rounds left in `recovering`. Zero in every other state. */
  recoveryLeft: number;
}

export const DEFAULT_RECOVERY_ROUNDS = 3;

export function makeMote(defId: string, state: MoteState = 'set'): MoteInstance {
  return { defId, state, recoveryLeft: 0 };
}

export function isSet(mote: MoteInstance): boolean {
  return mote.state === 'set';
}

export function isStandby(mote: MoteInstance): boolean {
  return mote.state === 'standby';
}

/** Motes currently contributing to class and stats, grouped by element. */
export function setCounts(
  motes: readonly MoteInstance[],
  defOf: (id: string) => MoteDef | undefined,
): Record<Element, number> {
  const counts = { terra: 0, pyre: 0, aeris: 0, rime: 0 } as Record<Element, number>;
  for (const mote of motes) {
    if (mote.state !== 'set') continue;
    const def = defOf(mote.defId);
    if (def) counts[def.element] += 1;
  }
  return counts;
}

/** Motes on standby, which are the currency for summons. */
export function standbyCounts(
  motes: readonly MoteInstance[],
  defOf: (id: string) => MoteDef | undefined,
): Record<Element, number> {
  const counts = { terra: 0, pyre: 0, aeris: 0, rime: 0 } as Record<Element, number>;
  for (const mote of motes) {
    if (mote.state !== 'standby') continue;
    const def = defOf(mote.defId);
    if (def) counts[def.element] += 1;
  }
  return counts;
}

/** Move a set mote to standby. Returns false if it was not available. */
export function unleashMote(motes: MoteInstance[], defId: string): boolean {
  const mote = motes.find((m) => m.defId === defId && m.state === 'set');
  if (!mote) return false;
  mote.state = 'standby';
  mote.recoveryLeft = 0;
  return true;
}

/**
 * Spend standby motes of a given element on a summon, moving them to
 * `recovering`. Returns the ids actually spent, which is empty if the actor
 * could not cover the cost.
 */
export function spendStandby(
  motes: MoteInstance[],
  element: Element,
  count: number,
  defOf: (id: string) => MoteDef | undefined,
): string[] {
  const available = motes.filter((m) => m.state === 'standby' && defOf(m.defId)?.element === element);
  if (available.length < count) return [];
  const spent: string[] = [];
  for (let i = 0; i < count; i++) {
    const mote = available[i] as MoteInstance;
    mote.state = 'recovering';
    mote.recoveryLeft = defOf(mote.defId)?.recoveryRounds ?? DEFAULT_RECOVERY_ROUNDS;
    spent.push(mote.defId);
  }
  return spent;
}

/** End-of-round recovery tick. Returns ids that returned to `set`. */
export function tickRecovery(motes: MoteInstance[]): string[] {
  const recovered: string[] = [];
  for (const mote of motes) {
    if (mote.state !== 'recovering') continue;
    mote.recoveryLeft -= 1;
    if (mote.recoveryLeft <= 0) {
      mote.state = 'set';
      mote.recoveryLeft = 0;
      recovered.push(mote.defId);
    }
  }
  return recovered;
}

/** Between battles every mote returns to its bearer. */
export function restoreAllMotes(motes: MoteInstance[]): void {
  for (const mote of motes) {
    mote.state = 'set';
    mote.recoveryLeft = 0;
  }
}
