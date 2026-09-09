import { beforeEach, describe, expect, it } from 'vitest';
import { ContentRegistry } from '../src/core/registry.js';
import { starterPack } from '../src/data/starterPack.js';
import { createActorState, deriveClass, deriveStats, type ActorState } from '../src/domain/actor.js';
import { makeMote, setCounts, spendStandby, standbyCounts, tickRecovery, unleashMote } from '../src/domain/motes.js';
import { canAfford, paySummonCost } from '../src/domain/summons.js';

const registry = ContentRegistry.from(starterPack);
const ctx = registry.actorContext();

function actor(): ActorState {
  const def = registry.actorDef('rell');
  if (!def) throw new Error('missing actor');
  return createActorState(def, 10, ctx);
}

describe('motes', () => {
  let state: ActorState;
  beforeEach(() => {
    state = actor();
  });

  it('counts only set motes toward class', () => {
    state.motes = [makeMote('boulder'), makeMote('cinder', 'standby')];
    expect(setCounts(state.motes, registry.moteDef)).toMatchObject({ terra: 1, pyre: 0 });
    expect(standbyCounts(state.motes, registry.moteDef)).toMatchObject({ pyre: 1 });
  });

  it('applies set-mote bonuses to stats and drops them on unleash', () => {
    state.motes = [makeMote('boulder')];
    const withMote = deriveStats(state, ctx);
    unleashMote(state.motes, 'boulder');
    const withoutMote = deriveStats(state, ctx);
    expect(withMote.maxHp).toBeGreaterThan(withoutMote.maxHp);
    expect(withMote.defense).toBeGreaterThan(withoutMote.defense);
  });

  it('changes class when a second element is bound', () => {
    state.motes = [makeMote('boulder')];
    expect(deriveClass(state, ctx)?.id).toBe('geomancer');
    state.motes.push(makeMote('cinder'));
    expect(deriveClass(state, ctx)?.id).toBe('ashwarden');
  });

  it('drops out of a class when a mote is unleashed mid-battle', () => {
    state.motes = [makeMote('boulder'), makeMote('cinder')];
    expect(deriveClass(state, ctx)?.id).toBe('ashwarden');
    unleashMote(state.motes, 'cinder');
    expect(deriveClass(state, ctx)?.id).toBe('geomancer');
  });

  it('refuses to unleash a mote that is not set', () => {
    state.motes = [makeMote('boulder', 'standby')];
    expect(unleashMote(state.motes, 'boulder')).toBe(false);
  });

  it('spends standby motes and recovers them over rounds', () => {
    state.motes = [makeMote('boulder', 'standby'), makeMote('loam', 'standby')];
    const spent = spendStandby(state.motes, 'terra', 2, registry.moteDef);
    expect(spent).toHaveLength(2);
    expect(state.motes.every((m) => m.state === 'recovering')).toBe(true);

    // Default recovery is three rounds.
    expect(tickRecovery(state.motes)).toHaveLength(0);
    expect(tickRecovery(state.motes)).toHaveLength(0);
    expect(tickRecovery(state.motes)).toHaveLength(2);
    expect(state.motes.every((m) => m.state === 'set')).toBe(true);
  });

  it('will not spend more motes than are on standby', () => {
    state.motes = [makeMote('boulder', 'standby')];
    expect(spendStandby(state.motes, 'terra', 2, registry.moteDef)).toEqual([]);
    expect(state.motes[0]?.state).toBe('standby');
  });
});

describe('summons', () => {
  it('requires standby motes of the right element', () => {
    const state = actor();
    const summon = registry.summonDef('stonewarden');
    if (!summon) throw new Error('missing summon');

    state.motes = [makeMote('boulder'), makeMote('loam')];
    expect(canAfford(summon, state.motes, registry.moteDef)).toBe(false);

    unleashMote(state.motes, 'boulder');
    unleashMote(state.motes, 'loam');
    expect(canAfford(summon, state.motes, registry.moteDef)).toBe(true);
  });

  it('leaves motes untouched when the cost cannot be met', () => {
    const state = actor();
    const summon = registry.summonDef('worldsong');
    if (!summon) throw new Error('missing summon');
    state.motes = [makeMote('boulder', 'standby')];
    expect(paySummonCost(summon, state.motes, registry.moteDef)).toEqual([]);
    expect(state.motes[0]?.state).toBe('standby');
  });
});
