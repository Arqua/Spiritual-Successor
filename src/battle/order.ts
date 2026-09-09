/**
 * Turn order.
 *
 * Commands for the whole round are chosen first, then everyone acts in one
 * ordered pass. Order is agility plus a small deterministic jitter, with
 * action priority tiers layered on top, so a fast healer is usually first but
 * never reliably so - and a priority move beats raw speed outright.
 *
 * Recomputing per round (rather than a persistent turn queue) means a speed
 * buff applied this round is felt next round, which is easy to reason about.
 */

import { nextFloat, type RngState } from '../core/rng.js';

export interface OrderEntry {
  combatantId: string;
  agility: number;
  priority: number;
  /** Resolved sort key, exposed for debugging and tests. */
  score: number;
}

export interface OrderInput {
  combatantId: string;
  agility: number;
  priority?: number;
}

/** Fraction of agility that the random jitter can swing. */
export const AGILITY_JITTER = 0.15;

export function buildTurnOrder(rng: RngState, inputs: readonly OrderInput[]): OrderEntry[] {
  const entries: OrderEntry[] = inputs.map((input) => {
    const jitter = 1 + (nextFloat(rng) * 2 - 1) * AGILITY_JITTER;
    return {
      combatantId: input.combatantId,
      agility: input.agility,
      priority: input.priority ?? 0,
      score: Math.max(0, input.agility) * jitter,
    };
  });

  entries.sort((a, b) => {
    if (b.priority !== a.priority) return b.priority - a.priority;
    if (b.score !== a.score) return b.score - a.score;
    return a.combatantId < b.combatantId ? -1 : a.combatantId > b.combatantId ? 1 : 0;
  });

  return entries;
}
