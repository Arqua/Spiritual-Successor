/**
 * Classes are derived, never assigned.
 *
 * An actor's class is a pure function of (innate element, set-mote counts).
 * Bind two pyre motes to a terra-aligned actor and they become something new;
 * unleash one mid-battle and they may drop back out of it, losing the stat
 * multipliers and the art list that came with it. Nothing stores the class, so
 * it can never fall out of sync with the motes.
 *
 * Resolution: collect every class whose requirements are satisfied, then take
 * the highest `priority`. Ties break toward the more specific requirement (the
 * one demanding more motes), then by id for determinism.
 */

import { ELEMENTS, type Element } from './elements.js';
import type { ScalarStat } from './stats.js';

export interface ClassRequirement {
  /** Minimum set motes of each element. Omitted elements require none. */
  minMotes?: Partial<Record<Element, number>>;
  /** Maximum set motes of each element, for classes that want purity. */
  maxMotes?: Partial<Record<Element, number>>;
  /** Minimum total set motes across all elements. */
  minTotal?: number;
}

export interface ClassDef {
  id: string;
  name: string;
  description?: string;
  /** Innate elements this class is available to. Empty or omitted means any. */
  innate?: Element[];
  requires: ClassRequirement;
  /** Higher wins when several classes match. */
  priority: number;
  /** Multipliers applied to the actor's level stats. 1.0 is unchanged. */
  statMultipliers?: Partial<Record<ScalarStat, number>>;
  /** Arts unlocked by this class, gated on actor level. */
  arts?: ClassArtGrant[];
}

export interface ClassArtGrant {
  artId: string;
  level: number;
}

export function requirementSpecificity(requirement: ClassRequirement): number {
  let total = requirement.minTotal ?? 0;
  for (const element of ELEMENTS) total += requirement.minMotes?.[element] ?? 0;
  return total;
}

export function matchesRequirement(
  requirement: ClassRequirement,
  counts: Record<Element, number>,
): boolean {
  let total = 0;
  for (const element of ELEMENTS) {
    const have = counts[element];
    total += have;
    const min = requirement.minMotes?.[element];
    if (typeof min === 'number' && have < min) return false;
    const max = requirement.maxMotes?.[element];
    if (typeof max === 'number' && have > max) return false;
  }
  if (typeof requirement.minTotal === 'number' && total < requirement.minTotal) return false;
  return true;
}

/**
 * Pick the class for an actor. `candidates` is normally the whole class table;
 * filtering happens here so callers do not have to pre-sort.
 */
export function resolveClass(
  candidates: readonly ClassDef[],
  innate: Element,
  counts: Record<Element, number>,
): ClassDef | undefined {
  let best: ClassDef | undefined;
  let bestSpecificity = -1;
  for (const candidate of candidates) {
    if (candidate.innate && candidate.innate.length > 0 && !candidate.innate.includes(innate)) continue;
    if (!matchesRequirement(candidate.requires, counts)) continue;
    const specificity = requirementSpecificity(candidate.requires);
    if (best === undefined) {
      best = candidate;
      bestSpecificity = specificity;
      continue;
    }
    if (candidate.priority > best.priority) {
      best = candidate;
      bestSpecificity = specificity;
    } else if (candidate.priority === best.priority) {
      if (specificity > bestSpecificity || (specificity === bestSpecificity && candidate.id < best.id)) {
        best = candidate;
        bestSpecificity = specificity;
      }
    }
  }
  return best;
}

/** Arts a class grants at or below the given level, in grant order. */
export function classArtsAtLevel(classDef: ClassDef | undefined, level: number): string[] {
  if (!classDef?.arts) return [];
  return classDef.arts.filter((grant) => grant.level <= level).map((grant) => grant.artId);
}
