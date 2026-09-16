/**
 * Building a battle from a party and an encounter, and applying the results
 * back to the party when it ends.
 */

import { seedRng, type RngState } from '../core/rng.js';
import type { ContentRegistry } from '../core/registry.js';
import { deriveStats, isDowned, type ActorState } from '../domain/actor.js';
import { restoreAllMotes } from '../domain/motes.js';
import { levelForXp } from '../domain/stats.js';
import type { EncounterDef } from '../domain/enemy.js';
import type { Inventory } from '../domain/items.js';
import type { BattleState, Combatant } from './state.js';

export interface BattleSetup {
  party: ActorState[];
  encounter: EncounterDef;
  seed: string | number;
  /**
   * The party's bag, held by reference so consumption during the fight is
   * permanent. Omitted means the party fights without items.
   */
  inventory?: Inventory;
}

export function createBattle(setup: BattleSetup, registry: ContentRegistry): BattleState {
  const rng: RngState = seedRng(setup.seed);
  const combatants: Combatant[] = [];
  const ctx = registry.actorContext();

  setup.party.forEach((actor, index) => {
    const stats = deriveStats(actor, ctx);
    combatants.push({
      id: `party:${actor.id}`,
      side: 'party',
      slot: index,
      actor,
      name: registry.actorDef(actor.defId)?.name ?? actor.defId,
      innate: registry.actorDef(actor.defId)?.innate ?? 'terra',
      hp: Math.min(actor.hp, stats.maxHp),
      maxHp: stats.maxHp,
      aether: Math.min(actor.aether, stats.maxAether),
      maxAether: stats.maxAether,
      statuses: actor.statuses.map((s) => ({ ...s })),
      defending: false,
      downed: isDowned(actor, ctx),
      cooldowns: {},
      tempModifiers: [],
    });
  });

  setup.encounter.members.forEach((member, index) => {
    const def = registry.enemyDef(member.enemyId);
    if (!def) return;
    combatants.push({
      id: `foe:${member.enemyId}:${index}`,
      side: 'foe',
      slot: member.slot ?? index,
      enemyId: member.enemyId,
      name: def.name,
      innate: def.innate,
      hp: def.stats.maxHp,
      maxHp: def.stats.maxHp,
      aether: def.stats.maxAether,
      maxAether: def.stats.maxAether,
      enemyStats: def.stats,
      statuses: [],
      defending: false,
      downed: false,
      cooldowns: {},
      tempModifiers: [],
    });
  });

  return {
    round: 1,
    phase: 'command',
    rng,
    combatants,
    outcome: null,
    rewards: { xp: 0, coin: 0, itemIds: [] },
    noFlee: setup.encounter.noFlee ?? false,
    inventory: setup.inventory ?? [],
  };
}

export interface BattleAftermath {
  outcome: 'victory' | 'defeat' | 'fled' | null;
  xp: number;
  coin: number;
  itemIds: string[];
  levelUps: { actorId: string; from: number; to: number }[];
}

/**
 * Write battle results back onto the party: HP/aether carry over, temporary
 * statuses clear, motes return, and XP is split among survivors.
 */
export function concludeBattle(
  state: BattleState,
  party: ActorState[],
  registry: ContentRegistry,
): BattleAftermath {
  const levelUps: BattleAftermath['levelUps'] = [];
  const survivors = party.filter((actor) => actor.hp > 0);
  const share = survivors.length > 0 ? Math.floor(state.rewards.xp / survivors.length) : 0;

  for (const actor of party) {
    const combatant = state.combatants.find((c) => c.actor?.id === actor.id);
    if (combatant) {
      actor.hp = combatant.hp;
      actor.aether = combatant.aether;
    }
    // Battle-scoped statuses and buffs do not persist onto the field.
    actor.statuses = actor.statuses.filter(
      (status) => registry.statusDef(status.statusId)?.duration.kind === 'persistent',
    );
    actor.tempModifiers = [];
    restoreAllMotes(actor.motes);

    if (state.outcome === 'victory' && actor.hp > 0 && share > 0) {
      const before = actor.level;
      actor.xp += share;
      const after = levelForXp(actor.xp);
      if (after !== before) {
        actor.level = after;
        levelUps.push({ actorId: actor.id, from: before, to: after });
      }
    }
  }

  return {
    outcome: state.outcome,
    xp: state.rewards.xp,
    coin: state.rewards.coin,
    itemIds: state.rewards.itemIds,
    levelUps,
  };
}
