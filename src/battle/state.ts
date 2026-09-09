/**
 * Battle state.
 *
 * The whole fight is one serializable object: combatants, round number, RNG
 * state. Resolving a round is a function from (state, commands) to (state,
 * events) - the caller decides whether to mutate in place or keep the previous
 * state around for rollback. Because the RNG lives inside the state, saving
 * mid-battle and reloading reproduces the same rolls.
 */

import type { RngState } from '../core/rng.js';
import type { ActorState } from '../domain/actor.js';
import type { StatBlock } from '../domain/stats.js';
import type { Element } from '../domain/elements.js';

export type Side = 'party' | 'foe';

export interface Combatant {
  /** Unique within the battle. */
  id: string;
  side: Side;
  /** Formation slot; adjacency drives spread damage falloff. */
  slot: number;
  /** Party members carry a full actor state; enemies carry a stat block. */
  actor?: ActorState;
  enemyId?: string;
  name: string;
  innate: Element;
  hp: number;
  maxHp: number;
  aether: number;
  maxAether: number;
  /** Enemy-only cached stats; party stats derive from the actor each read. */
  enemyStats?: StatBlock;
  statuses: { statusId: string; remaining: number; sourceId?: string | null }[];
  defending: boolean;
  downed: boolean;
  /** Rounds until each move may be used again, keyed by art id. */
  cooldowns: Record<string, number>;
  tempModifiers: { modifier: import('../domain/stats.js').StatModifier; rounds: number }[];
}

export type BattlePhase = 'command' | 'resolving' | 'ended';

export interface BattleState {
  round: number;
  phase: BattlePhase;
  rng: RngState;
  combatants: Combatant[];
  outcome: 'victory' | 'defeat' | 'fled' | null;
  /** Accumulated rewards, filled in on victory. */
  rewards: { xp: number; coin: number; itemIds: string[] };
  /** Set when the encounter forbids fleeing. */
  noFlee: boolean;
}

export type BattleCommand =
  | { kind: 'attack'; combatantId: string; targetId: string }
  | { kind: 'art'; combatantId: string; artId: string; targetId?: string }
  | { kind: 'unleash'; combatantId: string; moteId: string; targetId?: string }
  | { kind: 'summon'; combatantId: string; summonId: string; targetId?: string }
  | { kind: 'defend'; combatantId: string }
  | { kind: 'item'; combatantId: string; itemId: string; targetId?: string }
  | { kind: 'flee'; combatantId: string };

export function livingCombatants(state: BattleState, side: Side): Combatant[] {
  return state.combatants.filter((c) => c.side === side && !c.downed);
}

export function findCombatant(state: BattleState, id: string): Combatant | undefined {
  return state.combatants.find((c) => c.id === id);
}

export function opposingSide(side: Side): Side {
  return side === 'party' ? 'foe' : 'party';
}

/** Slot distance, used for spread damage falloff. */
export function slotDistance(a: Combatant, b: Combatant): number {
  return Math.abs(a.slot - b.slot);
}

export function isBattleOver(state: BattleState): boolean {
  return livingCombatants(state, 'party').length === 0 || livingCombatants(state, 'foe').length === 0;
}
