/**
 * Damage formulas.
 *
 * Every number a player sees comes out of this file, so the tuning constants
 * are gathered into one config object rather than sprinkled through the
 * resolver. All functions are pure and take an explicit RNG state, which makes
 * a damage roll reproducible and unit-testable.
 *
 * Two families:
 *
 *   physical - scales off attack, then is mitigated by defense.
 *   aetheric - driven by the caster's elemental power against the target's
 *              resistance, and only lightly reduced by defense. A tank in
 *              heavy armour is not safe from a well-aimed spell.
 *
 * Mitigation is asymptotic rather than subtractive: defense divides damage
 * through `K / (K + defense)` instead of being taken off the top. A subtractive
 * formula has a cliff - once defense exceeds attack, damage collapses to the
 * minimum and armour becomes flat immunity, which makes low-level parties in
 * basic gear unkillable by high-attack enemies. The asymptotic form gives
 * defense diminishing returns without ever reaching a wall, so every point of
 * attack and every point of defense keeps mattering at any scale.
 */

import { affinityFactor, DEFAULT_AFFINITY, type AffinityConfig, type Element } from '../domain/elements.js';
import type { StatBlock } from '../domain/stats.js';
import { chance, nextFloat, variance, type RngState } from '../core/rng.js';

export interface DamageConfig {
  affinity: AffinityConfig;
  /** Random spread applied to every damage roll. */
  variance: number;
  /** Physical: fraction of raw attack that a power-1.0 move delivers. */
  physicalScale: number;
  /** Aetheric: fraction of target defense that applies to spell damage. */
  aethericDefenseWeight: number;
  /**
   * Controls how quickly defense pays off. Damage is multiplied by
   * `softening / (softening + defense)`, so a defense equal to this value
   * halves incoming damage. Raise it to make defense weaker, lower it to make
   * defense stronger.
   */
  defenseSoftening: number;
  /** Base critical chance before luck. */
  baseCritChance: number;
  /** Luck points per additional percentage point of crit. */
  critLuckDivisor: number;
  /** Damage multiplier on a critical hit. */
  critMultiplier: number;
  /** Multiplier applied while the target is defending. */
  defendMultiplier: number;
  /** Minimum damage from any connecting hit. */
  minimumDamage: number;
  /** Base evasion derived from the agility gap. */
  agilityEvasionDivisor: number;
  /** Hard cap on evasion chance. */
  maxEvasion: number;
}

export const DEFAULT_DAMAGE_CONFIG: DamageConfig = Object.freeze({
  affinity: DEFAULT_AFFINITY,
  variance: 0.08,
  physicalScale: 0.9,
  aethericDefenseWeight: 0.35,
  defenseSoftening: 100,
  baseCritChance: 0.03,
  critLuckDivisor: 900,
  critMultiplier: 1.6,
  defendMultiplier: 0.5,
  minimumDamage: 1,
  agilityEvasionDivisor: 1200,
  maxEvasion: 0.25,
});

export interface DamageInput {
  attacker: StatBlock;
  defender: StatBlock;
  /** Element of the strike; null for typeless. */
  element: Element | null;
  /** The defender's own alignment, used for opposed-element bonuses. */
  defenderInnate: Element | null;
  /** Move power. For physical this multiplies the gap; for aetheric it is the base. */
  power: number;
  kind: 'physical' | 'aetheric' | 'fixed';
  defending: boolean;
  /** Extra multiplier from spread falloff, procs, buffs. */
  multiplier?: number;
  /** Skip the crit roll (summons, damage-over-time). */
  canCrit?: boolean;
}

export interface DamageResult {
  amount: number;
  critical: boolean;
  /** The affinity multiplier that applied, for UI feedback. */
  effectiveness: number;
}

export function rollDamage(rng: RngState, input: DamageInput, config: DamageConfig = DEFAULT_DAMAGE_CONFIG): DamageResult {
  const { attacker, defender, element, power, kind } = input;

  const effectiveness =
    element === null
      ? 1
      : affinityFactor(element, attacker.power[element], defender.resist[element], input.defenderInnate, config.affinity);

  let base: number;
  if (kind === 'fixed') {
    base = power;
  } else if (kind === 'physical') {
    const raw = Math.max(0, attacker.attack) * config.physicalScale * Math.max(0, power);
    base = raw * mitigation(defender.defense, config);
  } else {
    const raw = Math.max(0, power);
    base = raw * mitigation(defender.defense * config.aethericDefenseWeight, config);
  }

  base *= effectiveness;
  base *= input.multiplier ?? 1;

  const critical = (input.canCrit ?? true) && rollCritical(rng, attacker.luck, config);
  if (critical) base *= config.critMultiplier;
  if (input.defending) base *= config.defendMultiplier;

  base *= variance(rng, config.variance);

  const amount = Math.max(config.minimumDamage, Math.round(base));
  return { amount, critical, effectiveness };
}

/**
 * Asymptotic damage mitigation. Approaches zero as defense grows but never
 * reaches it, so armour is always worth something and never a hard wall.
 */
export function mitigation(defense: number, config: DamageConfig = DEFAULT_DAMAGE_CONFIG): number {
  const effective = Math.max(0, defense);
  return config.defenseSoftening / (config.defenseSoftening + effective);
}

export function rollCritical(rng: RngState, luck: number, config: DamageConfig = DEFAULT_DAMAGE_CONFIG): boolean {
  const critChance = config.baseCritChance + Math.max(0, luck) / config.critLuckDivisor;
  return chance(rng, Math.min(0.5, critChance));
}

/**
 * Evasion. A faster defender dodges more often, but the effect is capped so
 * agility stacking cannot make a character untouchable.
 */
export function rollEvaded(
  rng: RngState,
  attacker: StatBlock,
  defender: StatBlock,
  accuracy = 1,
  config: DamageConfig = DEFAULT_DAMAGE_CONFIG,
): boolean {
  const agilityGap = Math.max(0, defender.agility - attacker.agility);
  const evasion = Math.min(config.maxEvasion, agilityGap / config.agilityEvasionDivisor);
  const hitChance = Math.max(0, Math.min(1, accuracy - evasion));
  return nextFloat(rng) >= hitChance;
}

/** Healing shares the variance roll so numbers feel consistent with damage. */
export function rollHeal(
  rng: RngState,
  power: number,
  casterPower: number,
  maxHp: number,
  fraction = 0,
  config: DamageConfig = DEFAULT_DAMAGE_CONFIG,
): number {
  const flat = power * (1 + Math.max(0, casterPower) / config.affinity.divisor);
  const fromFraction = maxHp * Math.max(0, fraction);
  return Math.max(1, Math.round((flat + fromFraction) * variance(rng, config.variance)));
}
