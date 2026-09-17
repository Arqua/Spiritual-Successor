import { describe, expect, it } from 'vitest';
import { ContentRegistry, type ContentPack } from '../src/core/registry.js';
import { starterPack } from '../src/data/starterPack.js';
import { createActorState, restore, type ActorState } from '../src/domain/actor.js';
import {
  addItem,
  countOf,
  hasItem,
  isUsable,
  normalizeInventory,
  removeItem,
  type Inventory,
} from '../src/domain/items.js';
import { createBattle } from '../src/battle/setup.js';
import { resolveRound } from '../src/battle/resolve.js';
import { findCombatant, livingCombatants, type BattleState } from '../src/battle/state.js';
import { eventsOfType } from '../src/core/events.js';

const registry = ContentRegistry.from(starterPack);
const ctx = registry.actorContext();

describe('inventory', () => {
  it('counts across duplicate stacks', () => {
    const bag: Inventory = [
      { itemId: 'salve', count: 3 },
      { itemId: 'salve', count: 2 },
    ];
    expect(countOf(bag, 'salve')).toBe(5);
    expect(countOf(bag, 'nothing')).toBe(0);
  });

  it('merges into an existing stack when adding', () => {
    const bag: Inventory = [{ itemId: 'salve', count: 2 }];
    expect(addItem(bag, 'salve', 3, registry.itemDef)).toEqual({ added: 3, overflow: 0 });
    expect(bag).toEqual([{ itemId: 'salve', count: 5 }]);
  });

  it('reports what did not fit rather than discarding it', () => {
    // Quietly eating a reward is worse than a full bag: the player cannot tell
    // the difference between "picked up" and "lost".
    const bag: Inventory = [{ itemId: 'greater-salve', count: 19 }];
    expect(addItem(bag, 'greater-salve', 5, registry.itemDef)).toEqual({ added: 1, overflow: 4 });
    expect(countOf(bag, 'greater-salve')).toBe(20);
  });

  it('treats an absent stack limit as unlimited', () => {
    const bag: Inventory = [];
    expect(addItem(bag, 'sealed-writ', 5000, registry.itemDef).overflow).toBe(0);
  });

  it('refuses a removal it cannot cover, changing nothing', () => {
    const bag: Inventory = [{ itemId: 'salve', count: 2 }];
    expect(removeItem(bag, 'salve', 3)).toBe(false);
    expect(countOf(bag, 'salve')).toBe(2);
  });

  it('drops a stack once it is emptied', () => {
    const bag: Inventory = [{ itemId: 'salve', count: 2 }];
    expect(removeItem(bag, 'salve', 2)).toBe(true);
    expect(bag).toEqual([]);
  });

  it('draws across several stacks of the same item', () => {
    const bag: Inventory = [
      { itemId: 'salve', count: 2 },
      { itemId: 'salve', count: 2 },
    ];
    expect(removeItem(bag, 'salve', 3)).toBe(true);
    expect(countOf(bag, 'salve')).toBe(1);
  });

  it('normalizes away empty stacks and duplicates', () => {
    const bag: Inventory = [
      { itemId: 'salve', count: 2 },
      { itemId: 'salve', count: 3 },
      { itemId: 'antidote', count: 0 },
    ];
    expect(normalizeInventory(bag)).toEqual([{ itemId: 'salve', count: 5 }]);
  });

  it('gates usability by context', () => {
    expect(isUsable(registry.itemDef('firebomb'), 'battle')).toBe(true);
    expect(isUsable(registry.itemDef('firebomb'), 'field')).toBe(false);
    expect(isUsable(registry.itemDef('salve'), 'field')).toBe(true);
    // A key item names no art, so there is nothing to apply.
    expect(isUsable(registry.itemDef('sealed-writ'), 'field')).toBe(false);
    expect(isUsable(undefined, 'battle')).toBe(false);
  });
});

describe('using items in battle', () => {
  function party(): ActorState[] {
    const rell = createActorState(registry.actorDef('rell')!, 12, ctx);
    const maren = createActorState(registry.actorDef('maren')!, 12, ctx);
    maren.id = 'maren';
    restore(rell, ctx);
    restore(maren, ctx);
    return [rell, maren];
  }

  function battle(inventory: Inventory, seed = 'items'): { state: BattleState; roster: ActorState[] } {
    const roster = party();
    const state = createBattle(
      {
        party: roster,
        encounter: { id: 'test', members: [{ enemyId: 'thicket-crawler', slot: 0 }] },
        seed,
        inventory,
      },
      registry,
    );
    return { state, roster };
  }

  it('heals and consumes the item', () => {
    const bag: Inventory = [{ itemId: 'salve', count: 2 }];
    const { state } = battle(bag);
    const rell = findCombatant(state, 'party:rell')!;
    rell.hp = 20;

    const { events } = resolveRound(
      state,
      [{ kind: 'item', combatantId: 'party:rell', itemId: 'salve', targetId: 'party:rell' }],
      registry,
    );

    expect(eventsOfType(events, 'item-used')).toHaveLength(1);
    expect(rell.hp).toBeGreaterThan(20);
    expect(countOf(bag, 'salve')).toBe(1);
  });

  it('consumes the item even when the effect achieves nothing', () => {
    // Drinking at full health still empties the bottle.
    const bag: Inventory = [{ itemId: 'salve', count: 1 }];
    const { state } = battle(bag);

    resolveRound(
      state,
      [{ kind: 'item', combatantId: 'party:rell', itemId: 'salve', targetId: 'party:rell' }],
      registry,
    );
    expect(countOf(bag, 'salve')).toBe(0);
  });

  it('does not scale off whoever used it', () => {
    // A potion heals the same amount for the party's healer and its bruiser.
    // If it scaled off elemental power, handing the healer the potions would
    // be strictly correct, which is not how a potion works.
    function healedBy(userId: string): number {
      const bag: Inventory = [{ itemId: 'salve', count: 1 }];
      const { state } = battle(bag, 'scaling');
      const target = findCombatant(state, 'party:rell')!;
      target.hp = 10;

      const { events } = resolveRound(
        state,
        [{ kind: 'item', combatantId: userId, itemId: 'salve', targetId: 'party:rell' }],
        registry,
      );
      return eventsOfType(events, 'heal')[0]?.amount ?? 0;
    }

    // maren is the rime healer; rell is terra and heals for nothing normally.
    expect(healedBy('party:maren')).toBe(healedBy('party:rell'));
  });

  it('skips the turn when the bag does not hold the item', () => {
    const { state } = battle([]);
    const { events } = resolveRound(
      state,
      [{ kind: 'item', combatantId: 'party:rell', itemId: 'salve', targetId: 'party:rell' }],
      registry,
    );
    expect(eventsOfType(events, 'turn-skipped').some((e) => e.reason === 'item-missing')).toBe(true);
    expect(eventsOfType(events, 'item-used')).toHaveLength(0);
  });

  it('refuses an item that is not usable in battle', () => {
    const bag: Inventory = [{ itemId: 'sealed-writ', count: 1 }];
    const { state } = battle(bag);
    const { events } = resolveRound(
      state,
      [{ kind: 'item', combatantId: 'party:rell', itemId: 'sealed-writ' }],
      registry,
    );
    expect(eventsOfType(events, 'turn-skipped').some((e) => e.reason === 'item-not-usable')).toBe(true);
    // A refused use must not consume the item.
    expect(countOf(bag, 'sealed-writ')).toBe(1);
  });

  it('reports an unknown item rather than throwing', () => {
    const bag: Inventory = [{ itemId: 'ghost', count: 1 }];
    const { state } = battle(bag);
    const { events } = resolveRound(
      state,
      [{ kind: 'item', combatantId: 'party:rell', itemId: 'ghost' }],
      registry,
    );
    expect(eventsOfType(events, 'turn-skipped').some((e) => e.reason === 'unknown-item')).toBe(true);
  });

  it('damages foes with an offensive item', () => {
    const bag: Inventory = [{ itemId: 'firebomb', count: 1 }];
    const { state } = battle(bag, 'bomb');
    const foe = livingCombatants(state, 'foe')[0]!;
    const before = foe.hp;

    resolveRound(
      state,
      [{ kind: 'item', combatantId: 'party:rell', itemId: 'firebomb', targetId: foe.id }],
      registry,
    );
    expect(foe.hp).toBeLessThan(before);
  });

  it('restores aether with a tonic', () => {
    const bag: Inventory = [{ itemId: 'aether-tonic', count: 1 }];
    const { state } = battle(bag, 'tonic');
    const maren = findCombatant(state, 'party:maren')!;
    maren.aether = 0;

    resolveRound(
      state,
      [{ kind: 'item', combatantId: 'party:rell', itemId: 'aether-tonic', targetId: maren.id }],
      registry,
    );
    expect(maren.aether).toBeGreaterThan(0);
  });

  it('consumption survives fleeing the fight', () => {
    // The bag is held by reference, not copied, so an item spent in a battle
    // the party then runs from is still gone.
    const bag: Inventory = [{ itemId: 'salve', count: 1 }];
    const { state } = battle(bag, 'flee');
    findCombatant(state, 'party:rell')!.hp = 5;

    resolveRound(
      state,
      [{ kind: 'item', combatantId: 'party:rell', itemId: 'salve', targetId: 'party:rell' }],
      registry,
    );
    expect(countOf(bag, 'salve')).toBe(0);
    expect(hasItem(bag, 'salve')).toBe(false);
  });
});

describe('item content validation', () => {
  it('accepts the starter pack', () => {
    expect(ContentRegistry.from(starterPack).validate().filter((i) => i.severity === 'error')).toEqual([]);
  });

  it('catches an item naming an art that does not exist', () => {
    const broken: ContentPack = {
      id: 'broken',
      items: [{ id: 'bad', name: 'Bad', kind: 'consumable', artId: 'ghost-art' }],
    };
    expect(ContentRegistry.from(broken).validate().some((i) => i.message.includes('ghost-art'))).toBe(true);
  });

  it('warns about a consumable that does nothing', () => {
    const broken: ContentPack = {
      id: 'broken',
      items: [{ id: 'inert', name: 'Inert', kind: 'consumable' }],
    };
    const issues = ContentRegistry.from(broken).validate();
    expect(issues.some((i) => i.severity === 'warning' && i.message.includes('does nothing'))).toBe(true);
  });
});
