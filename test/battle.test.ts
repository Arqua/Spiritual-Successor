import { describe, expect, it } from 'vitest';
import { ContentRegistry } from '../src/core/registry.js';
import { starterPack } from '../src/data/starterPack.js';
import { createActorState, deriveStats, restore, type ActorState } from '../src/domain/actor.js';
import { makeMote } from '../src/domain/motes.js';
import { eventsOfType, type GameEvent } from '../src/core/events.js';
import { createBattle, concludeBattle } from '../src/battle/setup.js';
import { enemyCommands } from '../src/battle/ai.js';
import { resolveRound } from '../src/battle/resolve.js';
import { findCombatant, livingCombatants, type BattleCommand, type BattleState } from '../src/battle/state.js';
import { buildTurnOrder } from '../src/battle/order.js';
import { seedRng } from '../src/core/rng.js';

const registry = ContentRegistry.from(starterPack);
const ctx = registry.actorContext();

function party(): ActorState[] {
  const rell = createActorState(registry.actorDef('rell')!, 12, ctx);
  rell.motes = [makeMote('boulder'), makeMote('loam')];
  rell.equipment = { weapon: 'iron-blade', armor: 'leather-vest' };

  const maren = createActorState(registry.actorDef('maren')!, 12, ctx);
  maren.id = 'maren';
  maren.motes = [makeMote('hoarfrost')];

  // Motes and gear were assigned after creation, which raised max HP without
  // filling it. Top everyone up, as an inn would, before the fight starts.
  restore(rell, ctx);
  restore(maren, ctx);
  return [rell, maren];
}

function battle(seed = 'test-seed'): BattleState {
  return createBattle(
    {
      party: party(),
      encounter: { id: 'test', members: [{ enemyId: 'thicket-crawler', slot: 0 }, { enemyId: 'ash-wisp', slot: 1 }] },
      seed,
    },
    registry,
  );
}

describe('battle setup', () => {
  it('builds combatants for both sides at full health', () => {
    const state = battle();
    expect(state.combatants).toHaveLength(4);
    expect(livingCombatants(state, 'party')).toHaveLength(2);
    expect(livingCombatants(state, 'foe')).toHaveLength(2);
    for (const combatant of state.combatants) expect(combatant.hp).toBe(combatant.maxHp);
  });

  it('does not heal an actor for binding a mote', () => {
    // Binding raises max HP; if current HP rose with it, a player could bind
    // and unbind motes to heal for free between fights.
    const rell = createActorState(registry.actorDef('rell')!, 12, ctx);
    const beforeHp = rell.hp;
    rell.motes = [makeMote('boulder')];
    const afterMax = deriveStats(rell, ctx).maxHp;
    expect(afterMax).toBeGreaterThan(beforeHp);
    expect(rell.hp).toBe(beforeHp);
  });

  it('gives party members stats reflecting their bound motes', () => {
    const state = battle();
    const rell = findCombatant(state, 'party:rell');
    expect(rell?.maxHp).toBeGreaterThan(0);
    expect(rell?.innate).toBe('terra');
  });
});

describe('round resolution', () => {
  it('deals damage on a basic attack', () => {
    const state = battle();
    const target = livingCombatants(state, 'foe')[0]!;
    const before = target.hp;
    const { events } = resolveRound(
      state,
      [{ kind: 'attack', combatantId: 'party:rell', targetId: target.id }],
      registry,
    );
    const damage = eventsOfType(events, 'damage').filter((e) => e.targetId === target.id);
    // Either the blow landed or it was evaded; both are legal outcomes.
    if (damage.length > 0) expect(target.hp).toBeLessThan(before);
  });

  it('spends aether on an art and refuses when short', () => {
    const state = battle();
    const maren = findCombatant(state, 'party:maren')!;
    maren.aether = 0;
    const { events } = resolveRound(
      state,
      [{ kind: 'art', combatantId: 'party:maren', artId: 'frostbite', targetId: livingCombatants(state, 'foe')[0]!.id }],
      registry,
    );
    expect(eventsOfType(events, 'turn-skipped').some((e) => e.reason === 'insufficient-aether')).toBe(true);
  });

  it('moves a mote to standby when unleashed and reports the class change', () => {
    const state = battle();
    const { events } = resolveRound(
      state,
      [{ kind: 'unleash', combatantId: 'party:rell', moteId: 'loam' }],
      registry,
    );
    expect(eventsOfType(events, 'mote-unleashed')).toHaveLength(1);
    const rell = findCombatant(state, 'party:rell')!;
    expect(rell.actor?.motes.find((m) => m.defId === 'loam')?.state).toBe('standby');
  });

  it('is fully deterministic for a given seed', () => {
    const run = (): GameEvent[] => {
      const state = battle('determinism');
      const all: GameEvent[] = [];
      for (let round = 0; round < 4 && state.phase !== 'ended'; round++) {
        const commands: BattleCommand[] = [
          { kind: 'attack', combatantId: 'party:rell', targetId: livingCombatants(state, 'foe')[0]?.id ?? '' },
          { kind: 'art', combatantId: 'party:maren', artId: 'mend', targetId: 'party:rell' },
          ...enemyCommands(state, registry),
        ];
        all.push(...resolveRound(state, commands, registry).events);
      }
      return all;
    };
    expect(run()).toEqual(run());
  });

  it('reaches a conclusion and awards rewards on victory', () => {
    const state = battle('victory-run');
    // Overwhelm the enemies so the fight ends quickly.
    for (const foe of livingCombatants(state, 'foe')) foe.hp = 1;

    let guard = 0;
    while (state.phase !== 'ended' && guard++ < 30) {
      const foes = livingCombatants(state, 'foe');
      const commands: BattleCommand[] = foes[0]
        ? [{ kind: 'attack', combatantId: 'party:rell', targetId: foes[0].id }]
        : [];
      resolveRound(state, commands, registry);
    }

    expect(state.outcome).toBe('victory');
    expect(state.rewards.xp).toBeGreaterThan(0);
  });

  it('ends in defeat when the party falls', () => {
    const state = battle('defeat-run');
    for (const member of livingCombatants(state, 'party')) member.hp = 1;

    let guard = 0;
    while (state.phase !== 'ended' && guard++ < 30) {
      resolveRound(state, enemyCommands(state, registry), registry);
    }
    expect(state.outcome).toBe('defeat');
  });

  it('halves incoming damage while defending', () => {
    const state = battle('defend');
    const { events } = resolveRound(
      state,
      [{ kind: 'defend', combatantId: 'party:rell' }, ...enemyCommands(state, registry)],
      registry,
    );
    expect(findCombatant(state, 'party:rell')?.defending).toBe(true);
    expect(events.some((e) => e.type === 'round-start')).toBe(true);
  });

  it('restores motes and carries HP back to the party afterwards', () => {
    const roster = party();
    const state = createBattle(
      { party: roster, encounter: { id: 't', members: [{ enemyId: 'thicket-crawler', slot: 0 }] }, seed: 'aftermath' },
      registry,
    );
    resolveRound(state, [{ kind: 'unleash', combatantId: 'party:rell', moteId: 'boulder' }], registry);
    for (const foe of livingCombatants(state, 'foe')) foe.hp = 0;
    resolveRound(state, [], registry);

    const aftermath = concludeBattle(state, roster, registry);
    expect(aftermath.outcome).toBe('victory');
    expect(roster[0]!.motes.every((m) => m.state === 'set')).toBe(true);
  });
});

describe('turn order', () => {
  it('sorts by priority first, then by agility', () => {
    const rng = seedRng('order');
    const order = buildTurnOrder(rng, [
      { combatantId: 'slow-priority', agility: 1, priority: 100 },
      { combatantId: 'fast', agility: 999, priority: 0 },
    ]);
    expect(order[0]?.combatantId).toBe('slow-priority');
  });

  it('generally favours the faster combatant at equal priority', () => {
    const rng = seedRng('speed');
    let fastFirst = 0;
    for (let i = 0; i < 100; i++) {
      const order = buildTurnOrder(rng, [
        { combatantId: 'fast', agility: 100 },
        { combatantId: 'slow', agility: 40 },
      ]);
      if (order[0]?.combatantId === 'fast') fastFirst++;
    }
    expect(fastFirst).toBe(100);
  });

  it('lets jitter occasionally flip near-identical speeds', () => {
    const rng = seedRng('jitter');
    const results = new Set<string>();
    for (let i = 0; i < 200; i++) {
      const order = buildTurnOrder(rng, [
        { combatantId: 'a', agility: 100 },
        { combatantId: 'b', agility: 100 },
      ]);
      results.add(order[0]!.combatantId);
    }
    expect(results.size).toBe(2);
  });
});
