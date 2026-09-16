/**
 * Cross-language content fingerprint.
 *
 * Loads content/starter.json through the TypeScript loader, then prints a
 * deterministic summary that exercises not just the parse but the derivation
 * built on top of it - stat curves at a level, class resolution for a given
 * mote spread, the numbers on every art and enemy.
 *
 * The C# side computes the same fingerprint from the same file and asserts it
 * matches. Byte-identical parsing is necessary but not sufficient: the point
 * is that both engines *behave* the same on the same content.
 *
 * All numbers are formatted to six decimal places so the two languages agree
 * on representation rather than on floating-point printing conventions.
 *
 *   npx tsx tools/content-fingerprint.ts
 */

import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { loadPackStrict } from '../src/data/loadPack.js';
import { ContentRegistry } from '../src/core/registry.js';
import { createActorState, deriveStats, deriveClass } from '../src/domain/actor.js';
import { makeMote } from '../src/domain/motes.js';
import { ELEMENTS } from '../src/domain/elements.js';

const num = (value: number): string => value.toFixed(6);

const packJson = readFileSync(resolve(process.cwd(), 'content/starter.json'), 'utf8');
const registry = ContentRegistry.from(loadPackStrict(packJson));
const ctx = registry.actorContext();

const lines: string[] = [];

lines.push(
  `counts actors=${registry.actors.size} arts=${registry.arts.size} classes=${registry.classes.size} ` +
    `enemies=${registry.enemies.size} gear=${registry.gear.size} motes=${registry.motes.size} ` +
    `statuses=${registry.statuses.size} summons=${registry.summons.size}`,
);

const sortedIds = (map: Map<string, unknown>): string => [...map.keys()].sort().join(',');
lines.push(`ids.actors ${sortedIds(registry.actors)}`);
lines.push(`ids.arts ${sortedIds(registry.arts)}`);
lines.push(`ids.classes ${sortedIds(registry.classes)}`);
lines.push(`ids.motes ${sortedIds(registry.motes)}`);
lines.push(`ids.statuses ${sortedIds(registry.statuses)}`);
lines.push(`ids.summons ${sortedIds(registry.summons)}`);
lines.push(`ids.gear ${sortedIds(registry.gear)}`);
lines.push(`ids.enemies ${sortedIds(registry.enemies)}`);
lines.push(`ids.items ${sortedIds(registry.items)}`);

// Derived stats at a level exercise the growth curves through the loader.
for (const actorId of [...registry.actors.keys()].sort()) {
  const def = registry.actorDef(actorId)!;
  for (const level of [1, 12, 40]) {
    const state = createActorState(def, level, ctx);
    const stats = deriveStats(state, ctx);
    const power = ELEMENTS.map((e) => `${e}=${num(stats.power[e])}`).join(' ');
    const resist = ELEMENTS.map((e) => `${e}=${num(stats.resist[e])}`).join(' ');
    lines.push(
      `actor.${actorId}.L${level} hp=${num(stats.maxHp)} aether=${num(stats.maxAether)} ` +
        `atk=${num(stats.attack)} def=${num(stats.defense)} agi=${num(stats.agility)} luck=${num(stats.luck)} ` +
        `power[${power}] resist[${resist}]`,
    );
  }
}

// Class resolution for a range of mote spreads, which is the rule most likely
// to drift silently between implementations.
const spreads: [string, string[]][] = [
  ['none', []],
  ['terra1', ['boulder']],
  ['terra2', ['boulder', 'loam']],
  ['terra1pyre1', ['boulder', 'cinder']],
  ['aeris1rime1', ['zephyr', 'hoarfrost']],
  ['allfour', ['boulder', 'cinder', 'zephyr', 'hoarfrost']],
];
for (const actorId of [...registry.actors.keys()].sort()) {
  const def = registry.actorDef(actorId)!;
  for (const [label, moteIds] of spreads) {
    const state = createActorState(def, 20, ctx);
    state.motes = moteIds.map((id) => makeMote(id));
    lines.push(`class.${actorId}.${label} ${deriveClass(state, ctx)?.id ?? '-'}`);
  }
}

for (const artId of [...registry.arts.keys()].sort()) {
  const art = registry.artDef(artId)!;
  lines.push(
    `art.${artId} cost=${num(art.cost)} power=${num(art.effect.power ?? 0)} ` +
      `hits=${art.effect.hits ?? 1} kind=${art.kind} targeting=${art.targeting} element=${art.element} ` +
      `priority=${art.priority ?? 0} statuses=${(art.effect.statuses ?? []).map((s) => `${s.statusId}:${num(s.chance)}`).join('|') || '-'}`,
  );
}

for (const enemyId of [...registry.enemies.keys()].sort()) {
  const enemy = registry.enemyDef(enemyId)!;
  const s = enemy.stats;
  lines.push(
    `enemy.${enemyId} hp=${num(s.maxHp)} aether=${num(s.maxAether)} atk=${num(s.attack)} def=${num(s.defense)} ` +
      `agi=${num(s.agility)} luck=${num(s.luck)} xp=${enemy.xp} coin=${enemy.coin} moves=${(enemy.moves ?? []).length}`,
  );
}

for (const summonId of [...registry.summons.keys()].sort()) {
  const summon = registry.summonDef(summonId)!;
  const cost = ELEMENTS.map((e) => `${e}=${summon.cost[e] ?? 0}`).join(' ');
  lines.push(
    `summon.${summonId} base=${num(summon.basePower)} perMote=${num(summon.hpFractionPerMote ?? 0)} ` +
      `cap=${num(summon.hpFractionCap ?? 0.5)} hitsAll=${summon.hitsAll ?? false} cost[${cost}]`,
  );
}

for (const moteId of [...registry.motes.keys()].sort()) {
  const mote = registry.moteDef(moteId)!;
  const effect = mote.effect as Record<string, unknown>;
  lines.push(
    `mote.${moteId} element=${mote.element} effect=${String(effect.kind)} ` +
      `power=${num(typeof effect.power === 'number' ? effect.power : 0)} ` +
      `targeting=${String(effect.targeting ?? '-')} recovery=${mote.recoveryRounds ?? 3}`,
  );
}

for (const itemId of [...registry.items.keys()].sort()) {
  const item = registry.itemDef(itemId)!;
  lines.push(
    `item.${itemId} kind=${item.kind} art=${item.artId ?? '-'} power=${num(item.power ?? 0)} ` +
      `targeting=${item.targeting ?? '-'} battle=${item.usableInBattle ?? false} field=${item.usableOnField ?? false} ` +
      `consumed=${item.consumedOnUse ?? false} stack=${item.stackLimit ?? -1} value=${num(item.value ?? 0)}`,
  );
}

console.log(lines.join('\n'));
