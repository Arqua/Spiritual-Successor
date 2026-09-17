/**
 * Stat blocks and level growth.
 *
 * A stat block is a plain bag of numbers; nothing in the rules layer holds a
 * reference to a live object, so any state can be structured-cloned or written
 * straight to JSON. Derived stats are always recomputed from base + modifiers
 * rather than stored, which keeps equipment and class swaps from drifting.
 */

import { ELEMENTS, elementTable, type Element, type ElementTable } from './elements.js';

export const SCALAR_STATS = ['maxHp', 'maxAether', 'attack', 'defense', 'agility', 'luck'] as const;
export type ScalarStat = (typeof SCALAR_STATS)[number];

export interface StatBlock {
  maxHp: number;
  maxAether: number;
  attack: number;
  defense: number;
  agility: number;
  luck: number;
  /** Offensive strength per element. */
  power: ElementTable<number>;
  /** Damage reduction per element. */
  resist: ElementTable<number>;
}

export type StatModifier = Partial<Record<ScalarStat, number>> & {
  power?: Partial<ElementTable<number>>;
  resist?: Partial<ElementTable<number>>;
};

export function zeroStats(): StatBlock {
  return {
    maxHp: 0,
    maxAether: 0,
    attack: 0,
    defense: 0,
    agility: 0,
    luck: 0,
    power: elementTable(0),
    resist: elementTable(0),
  };
}

export function cloneStats(stats: StatBlock): StatBlock {
  return {
    maxHp: stats.maxHp,
    maxAether: stats.maxAether,
    attack: stats.attack,
    defense: stats.defense,
    agility: stats.agility,
    luck: stats.luck,
    power: { ...stats.power },
    resist: { ...stats.resist },
  };
}

/** Add a modifier into a stat block, returning a new block. */
export function applyModifier(base: StatBlock, mod: StatModifier | undefined): StatBlock {
  if (!mod) return cloneStats(base);
  const out = cloneStats(base);
  for (const stat of SCALAR_STATS) {
    const delta = mod[stat];
    if (typeof delta === 'number') out[stat] += delta;
  }
  for (const element of ELEMENTS) {
    const power = mod.power?.[element];
    if (typeof power === 'number') out.power[element] += power;
    const resist = mod.resist?.[element];
    if (typeof resist === 'number') out.resist[element] += resist;
  }
  return out;
}

export function applyModifiers(base: StatBlock, mods: readonly (StatModifier | undefined)[]): StatBlock {
  return mods.reduce<StatBlock>((acc, mod) => applyModifier(acc, mod), cloneStats(base));
}

/** Multiply the scalar stats of a block. Elemental power/resist are untouched. */
export function scaleStats(base: StatBlock, multipliers: Partial<Record<ScalarStat, number>>): StatBlock {
  const out = cloneStats(base);
  for (const stat of SCALAR_STATS) {
    const factor = multipliers[stat];
    if (typeof factor === 'number') out[stat] = Math.round(out[stat] * factor);
  }
  return out;
}

/**
 * A stat block that contributes nothing.
 *
 * Used for effects that must not scale off whoever triggered them - an item,
 * most obviously. A potion heals the same amount whoever drinks it, so the
 * healer being good at healing must not make the potion better.
 */
export function neutralStats(): StatBlock {
  return normalizeStats(zeroStats());
}

/** Clamp every stat to a sane floor so modifiers cannot drive a block negative. */
export function normalizeStats(stats: StatBlock): StatBlock {
  const out = cloneStats(stats);
  out.maxHp = Math.max(1, Math.round(out.maxHp));
  out.maxAether = Math.max(0, Math.round(out.maxAether));
  out.attack = Math.max(1, Math.round(out.attack));
  out.defense = Math.max(0, Math.round(out.defense));
  out.agility = Math.max(1, Math.round(out.agility));
  out.luck = Math.max(0, Math.round(out.luck));
  for (const element of ELEMENTS) {
    out.power[element] = Math.max(0, Math.round(out.power[element]));
    out.resist[element] = Math.max(0, Math.round(out.resist[element]));
  }
  return out;
}

/**
 * Per-stat growth curve.
 *
 * `value(level) = base + gain * (level - 1) ^ curve`
 *
 * A `curve` of 1 is linear. Below 1 it front-loads growth (early levels feel
 * generous), above 1 it back-loads it. Keeping growth as a formula rather than
 * a per-level table means content authors tune four numbers instead of ninety-
 * nine rows, and it stays readable in a JSON content pack.
 */
export interface GrowthCurve {
  base: number;
  gain: number;
  curve?: number;
}

export interface GrowthProfile {
  maxHp: GrowthCurve;
  maxAether: GrowthCurve;
  attack: GrowthCurve;
  defense: GrowthCurve;
  agility: GrowthCurve;
  luck: GrowthCurve;
  /** Elemental power/resist growth, defaulting to flat values when omitted. */
  power?: Partial<ElementTable<GrowthCurve>>;
  resist?: Partial<ElementTable<GrowthCurve>>;
}

export function curveAt(curve: GrowthCurve | undefined, level: number): number {
  if (!curve) return 0;
  const steps = Math.max(0, level - 1);
  const exponent = curve.curve ?? 1;
  return curve.base + curve.gain * Math.pow(steps, exponent);
}

export function statsAtLevel(profile: GrowthProfile, level: number): StatBlock {
  const lvl = Math.max(1, Math.floor(level));
  return normalizeStats({
    maxHp: curveAt(profile.maxHp, lvl),
    maxAether: curveAt(profile.maxAether, lvl),
    attack: curveAt(profile.attack, lvl),
    defense: curveAt(profile.defense, lvl),
    agility: curveAt(profile.agility, lvl),
    luck: curveAt(profile.luck, lvl),
    power: elementTable((element: Element) => curveAt(profile.power?.[element], lvl)),
    resist: elementTable((element: Element) => curveAt(profile.resist?.[element], lvl)),
  });
}

/**
 * Experience required to reach a level, as a smooth cubic-ish curve.
 * Level 1 costs nothing; each subsequent level costs progressively more.
 */
export function xpForLevel(level: number, scale = 30, exponent = 2.4): number {
  const lvl = Math.max(1, Math.floor(level));
  if (lvl === 1) return 0;
  return Math.round(scale * Math.pow(lvl - 1, exponent));
}

export function levelForXp(xp: number, maxLevel = 99, scale = 30, exponent = 2.4): number {
  let level = 1;
  while (level < maxLevel && xp >= xpForLevel(level + 1, scale, exponent)) level++;
  return level;
}
