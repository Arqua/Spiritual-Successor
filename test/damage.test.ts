import { describe, expect, it } from 'vitest';
import { seedRng } from '../src/core/rng.js';
import { DEFAULT_DAMAGE_CONFIG, rollDamage, rollEvaded, rollHeal } from '../src/battle/damage.js';
import { normalizeStats, type StatBlock } from '../src/domain/stats.js';

function stats(overrides: Partial<StatBlock> = {}): StatBlock {
  return normalizeStats({
    maxHp: 200,
    maxAether: 100,
    attack: 60,
    defense: 30,
    agility: 40,
    luck: 10,
    power: { terra: 20, pyre: 20, aeris: 20, rime: 20 },
    resist: { terra: 20, pyre: 20, aeris: 20, rime: 20 },
    ...overrides,
  });
}

// No-variance, no-crit config isolates the formula from the dice.
const flat = { ...DEFAULT_DAMAGE_CONFIG, variance: 0, baseCritChance: 0, critLuckDivisor: Number.MAX_SAFE_INTEGER };

describe('damage', () => {
  it('never deals less than the configured minimum', () => {
    const rng = seedRng('min');
    const result = rollDamage(rng, {
      attacker: stats({ attack: 1 }),
      defender: stats({ defense: 9999 }),
      element: null,
      defenderInnate: null,
      power: 1,
      kind: 'physical',
      defending: true,
    }, flat);
    expect(result.amount).toBeGreaterThanOrEqual(DEFAULT_DAMAGE_CONFIG.minimumDamage);
  });

  it('scales physical damage with the attacker attack stat', () => {
    const rng = seedRng('gap');
    const weak = rollDamage(rng, {
      attacker: stats({ attack: 40 }), defender: stats({ defense: 30 }),
      element: null, defenderInnate: null, power: 1, kind: 'physical', defending: false,
    }, flat);
    const strong = rollDamage(rng, {
      attacker: stats({ attack: 120 }), defender: stats({ defense: 30 }),
      element: null, defenderInnate: null, power: 1, kind: 'physical', defending: false,
    }, flat);
    expect(strong.amount).toBeGreaterThan(weak.amount);
  });

  it('lets defense blunt physical damage far more than aetheric', () => {
    const rng = seedRng('armor');
    const base = { element: null, defenderInnate: null, defending: false } as const;
    const physLight = rollDamage(rng, { ...base, attacker: stats(), defender: stats({ defense: 10 }), power: 1, kind: 'physical' }, flat).amount;
    const physHeavy = rollDamage(rng, { ...base, attacker: stats(), defender: stats({ defense: 50 }), power: 1, kind: 'physical' }, flat).amount;
    const magicLight = rollDamage(rng, { ...base, attacker: stats(), defender: stats({ defense: 10 }), power: 60, kind: 'aetheric' }, flat).amount;
    const magicHeavy = rollDamage(rng, { ...base, attacker: stats(), defender: stats({ defense: 50 }), power: 60, kind: 'aetheric' }, flat).amount;

    const physReduction = 1 - physHeavy / physLight;
    const magicReduction = 1 - magicHeavy / magicLight;
    expect(physReduction).toBeGreaterThan(magicReduction);
  });

  it('applies elemental affinity to the result', () => {
    const rng = seedRng('affinity');
    const shared = { defenderInnate: null, power: 60, kind: 'aetheric', defending: false } as const;
    const strong = rollDamage(rng, { ...shared, attacker: stats({ power: { terra: 0, pyre: 90, aeris: 0, rime: 0 } }), defender: stats({ resist: { terra: 0, pyre: 0, aeris: 0, rime: 0 } }), element: 'pyre' }, flat);
    const weak = rollDamage(rng, { ...shared, attacker: stats({ power: { terra: 0, pyre: 0, aeris: 0, rime: 0 } }), defender: stats({ resist: { terra: 0, pyre: 90, aeris: 0, rime: 0 } }), element: 'pyre' }, flat);
    expect(strong.effectiveness).toBeGreaterThan(1);
    expect(weak.effectiveness).toBeLessThan(1);
    expect(strong.amount).toBeGreaterThan(weak.amount);
  });

  it('halves damage against a defending target', () => {
    const rng = seedRng('defend');
    const shared = { attacker: stats(), defender: stats(), element: null, defenderInnate: null, power: 1, kind: 'physical' } as const;
    const open = rollDamage(rng, { ...shared, defending: false }, flat).amount;
    const guarded = rollDamage(rng, { ...shared, defending: true }, flat).amount;
    expect(guarded).toBeLessThan(open);
  });

  it('keeps variance within the configured spread', () => {
    const rng = seedRng('variance');
    const amounts: number[] = [];
    for (let i = 0; i < 300; i++) {
      amounts.push(rollDamage(rng, {
        attacker: stats(), defender: stats(), element: null, defenderInnate: null,
        power: 1, kind: 'physical', defending: false, canCrit: false,
      }).amount);
    }
    const min = Math.min(...amounts);
    const max = Math.max(...amounts);
    // 8% spread each way, plus rounding slack.
    expect(max / min).toBeLessThan(1.25);
  });

  it('caps evasion so a fast defender is never untouchable', () => {
    const rng = seedRng('evade');
    let evaded = 0;
    for (let i = 0; i < 1000; i++) {
      if (rollEvaded(rng, stats({ agility: 1 }), stats({ agility: 100_000 }))) evaded++;
    }
    expect(evaded / 1000).toBeLessThanOrEqual(DEFAULT_DAMAGE_CONFIG.maxEvasion + 0.05);
  });

  it('heals more with higher elemental power', () => {
    const rng = seedRng('heal');
    const low = rollHeal(rng, 40, 0, 200, 0, { ...DEFAULT_DAMAGE_CONFIG, variance: 0 });
    const high = rollHeal(rng, 40, 200, 200, 0, { ...DEFAULT_DAMAGE_CONFIG, variance: 0 });
    expect(high).toBeGreaterThan(low);
  });
});
