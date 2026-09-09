/**
 * The four elemental affinities and how they interact.
 *
 * Affinity is not a rock-paper-scissors chart. Every actor carries a `power`
 * and a `resist` value per element; an attack's effectiveness is the gap
 * between the attacker's power in that element and the defender's resistance
 * to it. Opposed elements add a flat separation on top, so an actor aligned to
 * one element is naturally soft against its opposite without needing a table
 * lookup for every pairing.
 */

export const ELEMENTS = ['terra', 'pyre', 'aeris', 'rime'] as const;

export type Element = (typeof ELEMENTS)[number];

/** Each element's opposite. The pairing is symmetric. */
export const OPPOSED: Readonly<Record<Element, Element>> = Object.freeze({
  terra: 'aeris',
  aeris: 'terra',
  pyre: 'rime',
  rime: 'pyre',
});

/** A value carried per element. */
export type ElementTable<T> = Record<Element, T>;

export function isElement(value: unknown): value is Element {
  return typeof value === 'string' && (ELEMENTS as readonly string[]).includes(value);
}

export function elementTable<T>(fill: T | ((element: Element) => T)): ElementTable<T> {
  const make = typeof fill === 'function' ? (fill as (element: Element) => T) : () => fill;
  return { terra: make('terra'), pyre: make('pyre'), aeris: make('aeris'), rime: make('rime') };
}

export function mapElements<T, U>(table: ElementTable<T>, fn: (value: T, element: Element) => U): ElementTable<U> {
  return elementTable((element) => fn(table[element], element));
}

export function addElementTables(a: ElementTable<number>, b: ElementTable<number>): ElementTable<number> {
  return elementTable((element) => a[element] + b[element]);
}

export function sumElementTable(table: ElementTable<number>): number {
  return ELEMENTS.reduce((total, element) => total + table[element], 0);
}

/** Tuning constants for affinity math. Override per project if you retune. */
export interface AffinityConfig {
  /** Larger values flatten the influence of the power/resist gap. */
  divisor: number;
  /** Extra effective power when striking an actor's opposed element. */
  opposedBonus: number;
  /** Multiplier floor, so a heavily resisted hit still lands for something. */
  minFactor: number;
  /** Multiplier ceiling, so stacking power cannot run away. */
  maxFactor: number;
}

export const DEFAULT_AFFINITY: AffinityConfig = Object.freeze({
  divisor: 200,
  opposedBonus: 25,
  minFactor: 0.25,
  maxFactor: 2.0,
});

/**
 * Effectiveness multiplier for an attack of `element` from an actor with
 * `attackerPower` in that element against a defender with `defenderResist`.
 *
 * `defenderInnate` is the defender's own alignment; striking the element that
 * opposes it counts as extra attacker power. A result of 1.0 is neutral.
 */
export function affinityFactor(
  element: Element,
  attackerPower: number,
  defenderResist: number,
  defenderInnate: Element | null = null,
  config: AffinityConfig = DEFAULT_AFFINITY,
): number {
  const opposedEdge = defenderInnate !== null && OPPOSED[defenderInnate] === element ? config.opposedBonus : 0;
  const gap = attackerPower + opposedEdge - defenderResist;
  const factor = 1 + gap / config.divisor;
  return clamp(factor, config.minFactor, config.maxFactor);
}

export function clamp(value: number, min: number, max: number): number {
  return value < min ? min : value > max ? max : value;
}
