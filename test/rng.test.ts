import { describe, expect, it } from 'vitest';
import { chance, cloneRng, nextFloat, nextInt, seedRng, shuffle, weightedPick } from '../src/core/rng.js';

describe('rng', () => {
  it('produces the same sequence for the same seed', () => {
    const a = seedRng('battle-42');
    const b = seedRng('battle-42');
    const left = Array.from({ length: 20 }, () => nextFloat(a));
    const right = Array.from({ length: 20 }, () => nextFloat(b));
    expect(left).toEqual(right);
  });

  it('produces different sequences for different seeds', () => {
    const a = seedRng('one');
    const b = seedRng('two');
    expect(nextFloat(a)).not.toBe(nextFloat(b));
  });

  it('keeps floats in [0, 1)', () => {
    const rng = seedRng(7);
    for (let i = 0; i < 500; i++) {
      const value = nextFloat(rng);
      expect(value).toBeGreaterThanOrEqual(0);
      expect(value).toBeLessThan(1);
    }
  });

  it('keeps integers within the inclusive range', () => {
    const rng = seedRng('ints');
    const seen = new Set<number>();
    for (let i = 0; i < 400; i++) seen.add(nextInt(rng, 3, 6));
    expect([...seen].sort()).toEqual([3, 4, 5, 6]);
  });

  it('cloning branches a timeline without disturbing the original', () => {
    const rng = seedRng('branch');
    nextFloat(rng);
    const forked = cloneRng(rng);
    expect(nextFloat(forked)).toBe(nextFloat(rng));
  });

  it('treats probability 0 and 1 as certainties', () => {
    const rng = seedRng('bounds');
    expect(chance(rng, 0)).toBe(false);
    expect(chance(rng, 1)).toBe(true);
  });

  it('shuffles without mutating the input', () => {
    const rng = seedRng('shuffle');
    const source = [1, 2, 3, 4, 5, 6];
    const result = shuffle(rng, source);
    expect(source).toEqual([1, 2, 3, 4, 5, 6]);
    expect(result.slice().sort()).toEqual(source);
  });

  it('never picks a zero-weight entry', () => {
    const rng = seedRng('weights');
    const items = [
      { id: 'never', weight: 0 },
      { id: 'always', weight: 5 },
    ];
    for (let i = 0; i < 100; i++) {
      expect(weightedPick(rng, items, (item) => item.weight)?.id).toBe('always');
    }
  });

  it('returns undefined when every weight is zero', () => {
    const rng = seedRng('zero');
    expect(weightedPick(rng, [{ w: 0 }], (i) => i.w)).toBeUndefined();
  });
});
