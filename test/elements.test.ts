import { describe, expect, it } from 'vitest';
import { affinityFactor, ELEMENTS, elementTable, OPPOSED } from '../src/domain/elements.js';

describe('elements', () => {
  it('pairs opposites symmetrically', () => {
    for (const element of ELEMENTS) {
      expect(OPPOSED[OPPOSED[element]]).toBe(element);
    }
  });

  it('is neutral when power and resistance match', () => {
    expect(affinityFactor('pyre', 50, 50)).toBe(1);
  });

  it('rewards a power advantage and punishes resistance', () => {
    expect(affinityFactor('pyre', 100, 50)).toBeGreaterThan(1);
    expect(affinityFactor('pyre', 20, 90)).toBeLessThan(1);
  });

  it('grants an edge when striking a defender opposed element', () => {
    const neutral = affinityFactor('pyre', 50, 50, 'terra');
    const opposed = affinityFactor('pyre', 50, 50, 'rime');
    expect(opposed).toBeGreaterThan(neutral);
  });

  it('clamps effectiveness into the configured band', () => {
    expect(affinityFactor('rime', 10_000, 0)).toBeLessThanOrEqual(2);
    expect(affinityFactor('rime', 0, 10_000)).toBeGreaterThanOrEqual(0.25);
  });

  it('builds a table for every element', () => {
    const table = elementTable(0);
    expect(Object.keys(table).sort()).toEqual([...ELEMENTS].sort());
  });
});
