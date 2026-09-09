/**
 * Easing curves.
 *
 * Pure functions from normalized time (0..1) to normalized progress. No engine
 * dependency, so these port to any language unchanged.
 */

export type Easing = (t: number) => number;

const clamp01 = (t: number): number => (t < 0 ? 0 : t > 1 ? 1 : t);

export const linear: Easing = (t) => clamp01(t);

export const easeInQuad: Easing = (t) => {
  const x = clamp01(t);
  return x * x;
};

export const easeOutQuad: Easing = (t) => {
  const x = clamp01(t);
  return 1 - (1 - x) * (1 - x);
};

export const easeInOutQuad: Easing = (t) => {
  const x = clamp01(t);
  return x < 0.5 ? 2 * x * x : 1 - Math.pow(-2 * x + 2, 2) / 2;
};

export const easeOutCubic: Easing = (t) => {
  const x = clamp01(t);
  return 1 - Math.pow(1 - x, 3);
};

/** Overshoots past 1 then settles. Good for impacts and pop-in. */
export const easeOutBack: Easing = (t) => {
  const x = clamp01(t);
  const c1 = 1.70158;
  const c3 = c1 + 1;
  return 1 + c3 * Math.pow(x - 1, 3) + c1 * Math.pow(x - 1, 2);
};

/** Decaying bounce, for damage numerals and landing. */
export const easeOutBounce: Easing = (t) => {
  let x = clamp01(t);
  const n1 = 7.5625;
  const d1 = 2.75;
  if (x < 1 / d1) return n1 * x * x;
  if (x < 2 / d1) return n1 * (x -= 1.5 / d1) * x + 0.75;
  if (x < 2.5 / d1) return n1 * (x -= 2.25 / d1) * x + 0.9375;
  return n1 * (x -= 2.625 / d1) * x + 0.984375;
};

/** A single arc up and back down. Peaks at t=0.5. */
export const arc: Easing = (t) => {
  const x = clamp01(t);
  return Math.sin(x * Math.PI);
};

export const EASINGS: Record<string, Easing> = {
  linear,
  easeInQuad,
  easeOutQuad,
  easeInOutQuad,
  easeOutCubic,
  easeOutBack,
  easeOutBounce,
  arc,
};

export function lerp(from: number, to: number, t: number): number {
  return from + (to - from) * t;
}
