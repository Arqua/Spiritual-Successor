/**
 * Summons.
 *
 * A summon is bought with motes that are already on standby, so the cost was
 * paid a turn or more earlier by unleashing them. Damage scales off the
 * target's max HP plus a flat term per mote spent, which keeps summons
 * relevant against high-HP bosses without making them the answer to trash.
 *
 * The mote cost is per element, so a summon can demand a specific mix and the
 * party has to plan which motes to unleash and when.
 */

import { ELEMENTS, type Element } from './elements.js';
import type { MoteDef, MoteInstance } from './motes.js';
import { spendStandby, standbyCounts } from './motes.js';

export interface SummonDef {
  id: string;
  name: string;
  description?: string;
  element: Element;
  /** Standby motes required, per element. */
  cost: Partial<Record<Element, number>>;
  /** Flat damage before scaling. */
  basePower: number;
  /** Extra damage as a fraction of the target's max HP, per mote spent. */
  hpFractionPerMote?: number;
  /** Cap on the max-HP-scaled portion, so bosses are not deleted. */
  hpFractionCap?: number;
  /** Hits every foe when true. */
  hitsAll?: boolean;
  /** Temporary elemental power granted to the summoner afterwards. */
  afterglowPower?: number;
  /** Rounds the afterglow lasts. */
  afterglowRounds?: number;
}

export function summonCostTotal(def: SummonDef): number {
  let total = 0;
  for (const element of ELEMENTS) total += def.cost[element] ?? 0;
  return total;
}

/** Whether an actor's standby motes cover a summon's cost. */
export function canAfford(
  def: SummonDef,
  motes: readonly MoteInstance[],
  moteDefOf: (id: string) => MoteDef | undefined,
): boolean {
  const available = standbyCounts(motes, moteDefOf);
  for (const element of ELEMENTS) {
    const needed = def.cost[element] ?? 0;
    if (needed > 0 && available[element] < needed) return false;
  }
  return true;
}

/**
 * Pay a summon's cost, moving the motes to `recovering`. Returns the spent
 * mote ids, or an empty array if the cost could not be met (in which case
 * nothing is mutated).
 */
export function paySummonCost(
  def: SummonDef,
  motes: MoteInstance[],
  moteDefOf: (id: string) => MoteDef | undefined,
): string[] {
  if (!canAfford(def, motes, moteDefOf)) return [];
  const spent: string[] = [];
  for (const element of ELEMENTS) {
    const needed = def.cost[element] ?? 0;
    if (needed <= 0) continue;
    spent.push(...spendStandby(motes, element, needed, moteDefOf));
  }
  return spent;
}

/**
 * Raw summon damage before elemental affinity and variance, which the damage
 * module applies. `motesSpent` is the total across all elements.
 */
export function summonBaseDamage(def: SummonDef, motesSpent: number, targetMaxHp: number): number {
  const fraction = def.hpFractionPerMote ?? 0;
  const cap = def.hpFractionCap ?? 0.5;
  const scaled = Math.min(cap, fraction * motesSpent) * targetMaxHp;
  return def.basePower + scaled;
}
