/**
 * Arts: the active abilities an actor spends aether on.
 *
 * One definition serves both battle and the overworld. An art with a
 * `fieldEffect` can be used outside combat to solve a puzzle; an art with a
 * battle `effect` can be used in a fight; a few have both, which is what makes
 * the overworld toolkit and the combat toolkit feel like one kit rather than
 * two disjoint lists.
 */

import type { Element } from './elements.js';
import type { StatModifier } from './stats.js';

export type ArtTargeting =
  | 'one-foe'
  | 'all-foes'
  /** Hits the primary target hard and its neighbours for less. */
  | 'spread-foes'
  | 'self'
  | 'one-ally'
  | 'all-allies'
  | 'downed-ally';

export type ArtKind =
  /** Scales off attack and is reduced by defense. */
  | 'physical'
  /** Scales off elemental power and ignores most defense. */
  | 'aetheric'
  | 'heal'
  | 'support'
  | 'field';

export interface ArtEffect {
  /** Base power. Interpreted per `kind`; see damage.ts. */
  power?: number;
  /** Multiple strikes, each rolled separately. */
  hits?: number;
  /** Falloff per step away from the primary target for `spread-foes`. */
  spreadFalloff?: number;
  /** Chance to inflict, before target luck. */
  statuses?: { statusId: string; chance: number }[];
  /** Timed stat changes applied to the target. */
  buff?: { modifier: StatModifier; rounds: number };
  /** Fraction of damage dealt returned to the user as HP. */
  drain?: number;
  /** Fraction of max HP restored, added on top of `power`. */
  healFraction?: number;
  /** Revives a downed ally at this fraction of max HP. */
  reviveFraction?: number;
  /** Aether restored to the target. */
  restoreAether?: number;
}

export interface ArtDef {
  id: string;
  name: string;
  description?: string;
  element: Element;
  kind: ArtKind;
  targeting: ArtTargeting;
  /** Aether cost. Zero for free actions. */
  cost: number;
  effect: ArtEffect;
  /** Higher acts earlier regardless of agility. Default 0. */
  priority?: number;
  /** Base hit chance before evasion. Default 1. */
  accuracy?: number;
  /** Field ability id, if this art also works on the overworld. */
  fieldEffect?: string;
  /** Usable outside battle (healing, cures). */
  usableOutOfBattle?: boolean;
  /** Cannot be used in battle; overworld only. */
  fieldOnly?: boolean;
  tags?: string[];
}

export function isOffensive(art: ArtDef): boolean {
  return art.targeting === 'one-foe' || art.targeting === 'all-foes' || art.targeting === 'spread-foes';
}

export function targetsAllies(art: ArtDef): boolean {
  return art.targeting === 'one-ally' || art.targeting === 'all-allies' || art.targeting === 'downed-ally' || art.targeting === 'self';
}

/** Spread damage falls off with distance from the primary target. */
export function spreadMultiplier(art: ArtDef, distance: number): number {
  if (distance <= 0) return 1;
  const falloff = art.effect.spreadFalloff ?? 0.4;
  return Math.max(0, Math.pow(1 - falloff, distance));
}
