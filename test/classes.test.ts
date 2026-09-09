import { describe, expect, it } from 'vitest';
import { matchesRequirement, resolveClass, type ClassDef } from '../src/domain/classes.js';

const counts = (terra = 0, pyre = 0, aeris = 0, rime = 0) => ({ terra, pyre, aeris, rime });

const table: ClassDef[] = [
  { id: 'base', name: 'Base', requires: { minTotal: 0 }, priority: 0 },
  {
    id: 'pure-terra',
    name: 'Pure Terra',
    innate: ['terra'],
    requires: { minMotes: { terra: 1 }, maxMotes: { pyre: 0, aeris: 0, rime: 0 } },
    priority: 10,
  },
  { id: 'dual', name: 'Dual', innate: ['terra'], requires: { minMotes: { terra: 1, pyre: 1 } }, priority: 20 },
  {
    id: 'all-four',
    name: 'All Four',
    requires: { minMotes: { terra: 1, pyre: 1, aeris: 1, rime: 1 } },
    priority: 50,
  },
];

describe('class resolution', () => {
  it('falls back to the baseline class with no motes bound', () => {
    expect(resolveClass(table, 'terra', counts())?.id).toBe('base');
  });

  it('picks the pure class when only the innate element is bound', () => {
    expect(resolveClass(table, 'terra', counts(2))?.id).toBe('pure-terra');
  });

  it('prefers the higher-priority dual class once a second element appears', () => {
    expect(resolveClass(table, 'terra', counts(1, 1))?.id).toBe('dual');
  });

  it('reaches the capstone class only with all four elements', () => {
    expect(resolveClass(table, 'terra', counts(1, 1, 1, 1))?.id).toBe('all-four');
  });

  it('respects innate-element gating', () => {
    // A pyre-innate actor cannot become the terra-only class.
    expect(resolveClass(table, 'pyre', counts(2))?.id).toBe('base');
  });

  it('enforces maximum requirements', () => {
    expect(matchesRequirement({ maxMotes: { pyre: 0 } }, counts(1, 1))).toBe(false);
    expect(matchesRequirement({ maxMotes: { pyre: 1 } }, counts(1, 1))).toBe(true);
  });

  it('is deterministic when priority and specificity tie', () => {
    const tied: ClassDef[] = [
      { id: 'zebra', name: 'Z', requires: { minMotes: { terra: 1 } }, priority: 5 },
      { id: 'alpha', name: 'A', requires: { minMotes: { terra: 1 } }, priority: 5 },
    ];
    expect(resolveClass(tied, 'terra', counts(1))?.id).toBe('alpha');
  });
});
