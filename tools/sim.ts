/**
 * Headless battle simulator.
 *
 * Runs full battles with no renderer attached, which is the quickest way to
 * check that a content change did not break the maths. Because the RNG is
 * seeded, a failing balance run is reproducible from its seed alone.
 *
 *   npm run sim -- --battles 200 --level 12
 */

import { ContentRegistry } from '../src/core/registry.js';
import { starterPack } from '../src/data/starterPack.js';
import { createActorState, restore, type ActorState } from '../src/domain/actor.js';
import { makeMote } from '../src/domain/motes.js';
import { createBattle, concludeBattle } from '../src/battle/setup.js';
import { enemyCommands } from '../src/battle/ai.js';
import { resolveRound } from '../src/battle/resolve.js';
import { livingCombatants, type BattleCommand, type BattleState } from '../src/battle/state.js';
import { canAfford } from '../src/domain/summons.js';

const registry = ContentRegistry.from(starterPack);
registry.assertValid();
const ctx = registry.actorContext();

function arg(name: string, fallback: number): number {
  const index = process.argv.indexOf(`--${name}`);
  if (index === -1) return fallback;
  const value = Number(process.argv[index + 1]);
  return Number.isFinite(value) ? value : fallback;
}

const BATTLES = arg('battles', 100);
const LEVEL = arg('level', 12);
const MAX_ROUNDS = arg('max-rounds', 40);

function buildParty(): ActorState[] {
  const rell = createActorState(registry.actorDef('rell')!, LEVEL, ctx);
  rell.motes = [makeMote('boulder'), makeMote('loam')];
  rell.equipment = { weapon: 'iron-blade', armor: 'leather-vest', shield: 'oak-shield' };

  const sunna = createActorState(registry.actorDef('sunna')!, LEVEL, ctx);
  sunna.motes = [makeMote('cinder'), makeMote('flare')];
  sunna.equipment = { weapon: 'ember-brand', armor: 'leather-vest' };

  const kite = createActorState(registry.actorDef('kite')!, LEVEL, ctx);
  kite.motes = [makeMote('zephyr'), makeMote('squall')];
  kite.equipment = { weapon: 'gale-edge', armor: 'leather-vest' };

  const maren = createActorState(registry.actorDef('maren')!, LEVEL, ctx);
  maren.motes = [makeMote('hoarfrost'), makeMote('brine')];
  maren.equipment = { armor: 'leather-vest', trinket: 'clarity-charm' };

  const party = [rell, sunna, kite, maren];
  for (const actor of party) restore(actor, ctx);
  return party;
}

/** A rough scripted party: heal when hurt, summon when affordable, else hit. */
function partyCommands(state: BattleState): BattleCommand[] {
  const commands: BattleCommand[] = [];
  const foes = livingCombatants(state, 'foe');
  if (foes.length === 0) return commands;
  const weakestFoe = foes.slice().sort((a, b) => a.hp - b.hp)[0]!;

  for (const member of livingCombatants(state, 'party')) {
    const actor = member.actor;
    if (!actor) continue;

    const hurtAlly = livingCombatants(state, 'party')
      .slice()
      .sort((a, b) => a.hp / a.maxHp - b.hp / b.maxHp)[0];

    const isHealer = actor.defId === 'maren';
    if (isHealer && hurtAlly && hurtAlly.hp / hurtAlly.maxHp < 0.5 && member.aether >= 6) {
      commands.push({ kind: 'art', combatantId: member.id, artId: 'mend', targetId: hurtAlly.id });
      continue;
    }

    const summon = registry.summonDef('conflagrant');
    if (summon && canAfford(summon, actor.motes, registry.moteDef)) {
      commands.push({ kind: 'summon', combatantId: member.id, summonId: summon.id });
      continue;
    }

    // Unleash a mote roughly a third of the time to build toward a summon.
    const setMote = actor.motes.find((m) => m.state === 'set');
    if (setMote && state.round % 3 === 0) {
      commands.push({ kind: 'unleash', combatantId: member.id, moteId: setMote.defId, targetId: weakestFoe.id });
      continue;
    }

    commands.push({ kind: 'attack', combatantId: member.id, targetId: weakestFoe.id });
  }
  return commands;
}

interface Tally {
  victories: number;
  defeats: number;
  stalemates: number;
  totalRounds: number;
  totalXp: number;
}

function runOne(seed: string, encounterId: string, members: { enemyId: string; slot: number }[]): 'victory' | 'defeat' | 'stalemate' {
  const party = buildParty();
  const state = createBattle({ party, encounter: { id: encounterId, members }, seed }, registry);

  let rounds = 0;
  while (state.phase !== 'ended' && rounds < MAX_ROUNDS) {
    resolveRound(state, [...partyCommands(state), ...enemyCommands(state, registry)], registry);
    rounds++;
  }
  concludeBattle(state, party, registry);
  if (state.outcome === 'victory') return 'victory';
  if (state.outcome === 'defeat') return 'defeat';
  return 'stalemate';
}

function simulate(label: string, members: { enemyId: string; slot: number }[]): void {
  const tally: Tally = { victories: 0, defeats: 0, stalemates: 0, totalRounds: 0, totalXp: 0 };

  for (let i = 0; i < BATTLES; i++) {
    const outcome = runOne(`sim-${label}-${i}`, label, members);
    if (outcome === 'victory') tally.victories++;
    else if (outcome === 'defeat') tally.defeats++;
    else tally.stalemates++;
  }

  const rate = ((tally.victories / BATTLES) * 100).toFixed(1);
  console.log(
    `  ${label.padEnd(22)} win ${rate.padStart(5)}%   ` +
      `(${tally.victories}W / ${tally.defeats}L / ${tally.stalemates} unresolved)`,
  );
}

console.log(`\nAetherlight balance simulation`);
console.log(`  party level ${LEVEL}, ${BATTLES} battles per encounter, cap ${MAX_ROUNDS} rounds\n`);

simulate('trash-pair', [
  { enemyId: 'thicket-crawler', slot: 0 },
  { enemyId: 'thicket-crawler', slot: 1 },
]);
simulate('mixed-trio', [
  { enemyId: 'thicket-crawler', slot: 0 },
  { enemyId: 'ash-wisp', slot: 1 },
  { enemyId: 'ash-wisp', slot: 2 },
]);
simulate('boss', [{ enemyId: 'hollow-sentinel', slot: 0 }]);

console.log('');
