/**
 * Deterministic, serializable pseudo-random number generator.
 *
 * Every random decision in the rules layer flows through this so that a battle
 * can be replayed exactly from a seed. That matters for netplay, for replays,
 * and for reproducing balance-test failures. The generator is a 128-bit
 * xoshiro-style state advanced with a 32-bit mixing step; it is not
 * cryptographically secure and is not intended to be.
 */

export interface RngState {
  a: number;
  b: number;
  c: number;
  d: number;
}

const UINT32 = 0x100000000;

function rotl(x: number, k: number): number {
  return ((x << k) | (x >>> (32 - k))) >>> 0;
}

/** Build a generator state from an arbitrary string or numeric seed. */
export function seedRng(seed: string | number): RngState {
  let h = 0x9e3779b9 >>> 0;
  const text = typeof seed === 'number' ? String(seed) : seed;
  for (let i = 0; i < text.length; i++) {
    h = Math.imul(h ^ text.charCodeAt(i), 0x85ebca6b) >>> 0;
    h = (h ^ (h >>> 13)) >>> 0;
  }
  const next = () => {
    h = (h + 0x6d2b79f5) >>> 0;
    let t = h;
    t = Math.imul(t ^ (t >>> 15), t | 1) >>> 0;
    t = (t ^ (t + Math.imul(t ^ (t >>> 7), t | 61))) >>> 0;
    return (t ^ (t >>> 14)) >>> 0;
  };
  const state: RngState = { a: next(), b: next(), c: next(), d: next() };
  // Guard against the all-zero state, which is a fixed point.
  if (!state.a && !state.b && !state.c && !state.d) state.a = 0x1a2b3c4d;
  return state;
}

/** Copy a state so callers can branch a timeline without mutating the original. */
export function cloneRng(state: RngState): RngState {
  return { a: state.a, b: state.b, c: state.c, d: state.d };
}

/** Advance the state and return a uint32. Mutates `state` in place. */
export function nextUint32(state: RngState): number {
  const t = (state.b << 9) >>> 0;
  state.c = (state.c ^ state.a) >>> 0;
  state.d = (state.d ^ state.b) >>> 0;
  state.b = (state.b ^ state.c) >>> 0;
  state.a = (state.a ^ state.d) >>> 0;
  state.c = (state.c ^ t) >>> 0;
  state.d = rotl(state.d, 11);
  const result = (rotl(Math.imul(state.b, 5) >>> 0, 7) * 9) >>> 0;
  return result;
}

/** Uniform float in [0, 1). */
export function nextFloat(state: RngState): number {
  return nextUint32(state) / UINT32;
}

/** Uniform integer in [min, max] inclusive. */
export function nextInt(state: RngState, min: number, max: number): number {
  if (max < min) throw new RangeError(`nextInt: max (${max}) < min (${min})`);
  const span = max - min + 1;
  return min + Math.floor(nextFloat(state) * span);
}

/** True with the given probability (0..1). */
export function chance(state: RngState, probability: number): boolean {
  if (probability <= 0) return false;
  if (probability >= 1) return true;
  return nextFloat(state) < probability;
}

/** Pick one element. Returns undefined for an empty list. */
export function pick<T>(state: RngState, items: readonly T[]): T | undefined {
  if (items.length === 0) return undefined;
  return items[nextInt(state, 0, items.length - 1)];
}

/** Fisher-Yates into a new array; the input is left untouched. */
export function shuffle<T>(state: RngState, items: readonly T[]): T[] {
  const out = items.slice();
  for (let i = out.length - 1; i > 0; i--) {
    const j = nextInt(state, 0, i);
    const a = out[i] as T;
    const b = out[j] as T;
    out[i] = b;
    out[j] = a;
  }
  return out;
}

/**
 * Weighted pick. Weights must be non-negative; entries with weight 0 are never
 * chosen. Returns undefined if every weight is 0 or the list is empty.
 */
export function weightedPick<T>(
  state: RngState,
  items: readonly T[],
  weightOf: (item: T) => number,
): T | undefined {
  let total = 0;
  for (const item of items) total += Math.max(0, weightOf(item));
  if (total <= 0) return undefined;
  let roll = nextFloat(state) * total;
  for (const item of items) {
    roll -= Math.max(0, weightOf(item));
    if (roll < 0) return item;
  }
  return items[items.length - 1];
}

/** Symmetric variance multiplier, e.g. spread 0.1 gives a factor in [0.9, 1.1). */
export function variance(state: RngState, spread: number): number {
  return 1 + (nextFloat(state) * 2 - 1) * spread;
}
