/**
 * Round resolution.
 *
 * A round runs in three phases:
 *
 *   1. build the turn order from agility and action priority
 *   2. execute each combatant's command in that order, skipping anyone who
 *      went down before their turn came up
 *   3. tick statuses, cooldowns, mote recovery, and check for an ending
 *
 * The function is deliberately long-form rather than clever: an RPG resolver
 * is read far more often than it is written, and every branch here maps to a
 * rule a designer will want to find.
 */

import { EventLog, type GameEvent } from '../core/events.js';
import { chance, nextFloat, pick, type RngState } from '../core/rng.js';
import type { ContentRegistry } from '../core/registry.js';
import { deriveStats, type ActorState } from '../domain/actor.js';
import { spreadMultiplier, type ArtDef } from '../domain/arts.js';
import { deriveClass } from '../domain/actor.js';
import type { Element } from '../domain/elements.js';
import { gearProcs } from '../domain/equipment.js';
import { tickRecovery, unleashMote, type MoteDef } from '../domain/motes.js';
import { statusLandChance, type StatusDef } from '../domain/status.js';
import { paySummonCost, summonBaseDamage, summonCostTotal } from '../domain/summons.js';
import type { StatBlock } from '../domain/stats.js';
import { applyModifiers, normalizeStats } from '../domain/stats.js';
import { DEFAULT_DAMAGE_CONFIG, rollDamage, rollEvaded, rollHeal, type DamageConfig } from './damage.js';
import { buildTurnOrder } from './order.js';
import {
  findCombatant,
  isBattleOver,
  livingCombatants,
  opposingSide,
  slotDistance,
  type BattleCommand,
  type BattleState,
  type Combatant,
} from './state.js';

export interface ResolveOptions {
  damage?: DamageConfig;
}

export interface RoundResult {
  state: BattleState;
  events: GameEvent[];
}

/** Resolve one full round. Mutates and returns `state` for convenience. */
export function resolveRound(
  state: BattleState,
  commands: readonly BattleCommand[],
  registry: ContentRegistry,
  options: ResolveOptions = {},
): RoundResult {
  const config = options.damage ?? DEFAULT_DAMAGE_CONFIG;
  const log = new EventLog();

  if (state.phase === 'ended') return { state, events: [] };

  state.phase = 'resolving';
  log.push({ type: 'round-start', round: state.round });

  for (const combatant of state.combatants) combatant.defending = false;

  const commandByCombatant = new Map<string, BattleCommand>();
  for (const command of commands) commandByCombatant.set(command.combatantId, command);

  const order = buildTurnOrder(
    state.rng,
    state.combatants
      .filter((c) => !c.downed)
      .map((c) => ({
        combatantId: c.id,
        agility: statsOf(c, registry).agility,
        priority: commandPriority(commandByCombatant.get(c.id), registry),
      })),
  );

  // Defend resolves before anything else so the damage reduction is live.
  for (const entry of order) {
    const command = commandByCombatant.get(entry.combatantId);
    if (command?.kind === 'defend') {
      const combatant = findCombatant(state, entry.combatantId);
      if (combatant && !combatant.downed) combatant.defending = true;
    }
  }

  for (const entry of order) {
    const combatant = findCombatant(state, entry.combatantId);
    if (!combatant || combatant.downed) continue;
    if (isBattleOver(state)) break;

    const command = commandByCombatant.get(entry.combatantId);
    if (!command) continue;

    const blocker = actionBlocker(combatant, registry);
    if (blocker) {
      if (blocker.shakeOff) {
        combatant.statuses = combatant.statuses.filter((s) => s.statusId !== blocker.statusId);
        log.push({ type: 'status-expired', targetId: combatant.id, statusId: blocker.statusId });
      } else {
        log.push({ type: 'turn-skipped', combatantId: combatant.id, reason: blocker.statusId });
        continue;
      }
    }

    log.push({ type: 'turn-start', combatantId: combatant.id });
    executeCommand(state, combatant, command, registry, config, log);
    reapDowned(state, log, registry);
  }

  endOfRound(state, registry, log);

  if (!state.outcome && isBattleOver(state)) {
    finishBattle(state, registry, log);
  }

  log.push({ type: 'round-end', round: state.round });
  state.round += 1;
  // `outcome` is set exactly when the fight is over, by either finishBattle or
  // a successful flee, so it is the reliable signal here.
  if (state.outcome === null) state.phase = 'command';

  return { state, events: log.drain() };
}

function commandPriority(command: BattleCommand | undefined, registry: ContentRegistry): number {
  if (!command) return 0;
  switch (command.kind) {
    case 'defend':
      return 100;
    case 'flee':
      return 90;
    case 'item':
      return 50;
    case 'unleash':
      return 20;
    case 'summon':
      return 15;
    case 'art':
      return registry.artDef(command.artId)?.priority ?? 0;
    default:
      return 0;
  }
}

function executeCommand(
  state: BattleState,
  actor: Combatant,
  command: BattleCommand,
  registry: ContentRegistry,
  config: DamageConfig,
  log: EventLog,
): void {
  switch (command.kind) {
    case 'attack':
      doAttack(state, actor, command.targetId, registry, config, log);
      break;
    case 'art':
      doArt(state, actor, command.artId, command.targetId, registry, config, log);
      break;
    case 'unleash':
      doUnleash(state, actor, command.moteId, command.targetId, registry, config, log);
      break;
    case 'summon':
      doSummon(state, actor, command.summonId, registry, config, log);
      break;
    case 'flee':
      doFlee(state, actor, registry, log);
      break;
    case 'defend':
      log.push({ type: 'action-declared', combatantId: actor.id, action: 'defend', targetIds: [] });
      break;
    case 'item':
      // Items are project-specific; the core emits the intent and lets the
      // host game apply its own inventory rules.
      log.push({ type: 'action-declared', combatantId: actor.id, action: `item:${command.itemId}`, targetIds: command.targetId ? [command.targetId] : [] });
      break;
  }
}

function doAttack(
  state: BattleState,
  actor: Combatant,
  targetId: string,
  registry: ContentRegistry,
  config: DamageConfig,
  log: EventLog,
): void {
  const target = resolveTarget(state, actor, targetId);
  if (!target) return;

  log.push({ type: 'action-declared', combatantId: actor.id, action: 'attack', targetIds: [target.id] });

  // A worn weapon may convert the swing into an art instead.
  const proc = rollGearProc(state.rng, actor, registry);
  if (proc) {
    const art = registry.artDef(proc.artId);
    if (art) {
      applyArtToTargets(state, actor, art, [target], registry, config, log, proc.powerScale ?? 1);
      return;
    }
  }

  const attackerStats = statsOf(actor, registry);
  const defenderStats = statsOf(target, registry);

  if (rollEvaded(state.rng, attackerStats, defenderStats, 1, config)) {
    log.push({ type: 'miss', sourceId: actor.id, targetId: target.id });
    return;
  }

  const element = weaponElement(actor, registry);
  const result = rollDamage(
    state.rng,
    {
      attacker: attackerStats,
      defender: defenderStats,
      element,
      defenderInnate: target.innate,
      power: 1,
      kind: 'physical',
      defending: target.defending,
    },
    config,
  );
  dealDamage(state, actor, target, result.amount, element, result.critical, result.effectiveness, log, registry);
}

function doArt(
  state: BattleState,
  actor: Combatant,
  artId: string,
  targetId: string | undefined,
  registry: ContentRegistry,
  config: DamageConfig,
  log: EventLog,
): void {
  const art = registry.artDef(artId);
  if (!art || art.fieldOnly) return;

  if (artsBlocked(actor, registry)) {
    log.push({ type: 'turn-skipped', combatantId: actor.id, reason: 'arts-sealed' });
    return;
  }
  if (actor.aether < art.cost) {
    log.push({ type: 'turn-skipped', combatantId: actor.id, reason: 'insufficient-aether' });
    return;
  }

  actor.aether -= art.cost;
  if (art.cost > 0) log.push({ type: 'aether-spent', combatantId: actor.id, amount: art.cost });

  const targets = selectTargets(state, actor, art, targetId);
  log.push({ type: 'action-declared', combatantId: actor.id, action: `art:${art.id}`, targetIds: targets.map((t) => t.id) });
  applyArtToTargets(state, actor, art, targets, registry, config, log, 1);
}

function applyArtToTargets(
  state: BattleState,
  actor: Combatant,
  art: ArtDef,
  targets: readonly Combatant[],
  registry: ContentRegistry,
  config: DamageConfig,
  log: EventLog,
  powerScale: number,
): void {
  const primary = targets[0];
  const attackerStats = statsOf(actor, registry);

  for (const target of targets) {
    const distance = primary ? slotDistance(primary, target) : 0;
    const falloff = art.targeting === 'spread-foes' ? spreadMultiplier(art, distance) : 1;
    if (falloff <= 0) continue;

    if (art.kind === 'heal') {
      applyHeal(state, actor, target, art, attackerStats, registry, log, powerScale * falloff);
      continue;
    }
    if (art.kind === 'support') {
      applySupport(state, actor, target, art, registry, log);
      continue;
    }

    const defenderStats = statsOf(target, registry);
    if (rollEvaded(state.rng, attackerStats, defenderStats, art.accuracy ?? 1, config)) {
      log.push({ type: 'miss', sourceId: actor.id, targetId: target.id });
      continue;
    }

    const hits = Math.max(1, art.effect.hits ?? 1);
    let dealtTotal = 0;
    for (let hit = 0; hit < hits; hit++) {
      if (target.downed) break;
      const result = rollDamage(
        state.rng,
        {
          attacker: attackerStats,
          defender: defenderStats,
          element: art.element,
          defenderInnate: target.innate,
          power: (art.effect.power ?? 0) * powerScale,
          kind: art.kind === 'physical' ? 'physical' : 'aetheric',
          defending: target.defending,
          multiplier: falloff,
        },
        config,
      );
      dealtTotal += result.amount;
      dealDamage(state, actor, target, result.amount, art.element, result.critical, result.effectiveness, log, registry);
    }

    if (art.effect.drain && dealtTotal > 0) {
      const healed = Math.max(1, Math.round(dealtTotal * art.effect.drain));
      applyRawHeal(state, actor, actor, healed, log);
    }

    for (const status of art.effect.statuses ?? []) {
      tryApplyStatus(state, actor, target, status.statusId, status.chance, registry, log);
    }
  }
}

function applyHeal(
  state: BattleState,
  actor: Combatant,
  target: Combatant,
  art: ArtDef,
  attackerStats: StatBlock,
  registry: ContentRegistry,
  log: EventLog,
  scale: number,
): void {
  if (art.effect.reviveFraction && target.downed) {
    const hp = Math.max(1, Math.round(target.maxHp * art.effect.reviveFraction));
    target.downed = false;
    target.hp = hp;
    if (target.actor) target.actor.hp = hp;
    log.push({ type: 'revived', combatantId: target.id, hp });
    return;
  }
  if (target.downed) return;

  if (art.effect.restoreAether) {
    const amount = Math.min(art.effect.restoreAether, target.maxAether - target.aether);
    if (amount > 0) {
      target.aether += amount;
      if (target.actor) target.actor.aether = target.aether;
      log.push({ type: 'aether-restored', combatantId: target.id, amount });
    }
  }

  const power = (art.effect.power ?? 0) * scale;
  if (power > 0 || art.effect.healFraction) {
    const healed = rollHeal(
      state.rng,
      power,
      attackerStats.power[art.element],
      target.maxHp,
      art.effect.healFraction ?? 0,
    );
    applyRawHeal(state, actor, target, healed, log);
  }

  for (const status of art.effect.statuses ?? []) {
    // On a healing art, listed statuses are cures rather than afflictions.
    const before = target.statuses.length;
    target.statuses = target.statuses.filter((s) => s.statusId !== status.statusId);
    if (target.statuses.length !== before) {
      log.push({ type: 'status-expired', targetId: target.id, statusId: status.statusId });
    }
  }

  if (art.effect.buff) applyBuff(target, art.effect.buff, log);
}

function applySupport(
  state: BattleState,
  actor: Combatant,
  target: Combatant,
  art: ArtDef,
  registry: ContentRegistry,
  log: EventLog,
): void {
  if (art.effect.buff) applyBuff(target, art.effect.buff, log);
  if (art.effect.restoreAether) {
    const amount = Math.min(art.effect.restoreAether, target.maxAether - target.aether);
    if (amount > 0) {
      target.aether += amount;
      if (target.actor) target.actor.aether = target.aether;
      log.push({ type: 'aether-restored', combatantId: target.id, amount });
    }
  }
  for (const status of art.effect.statuses ?? []) {
    tryApplyStatus(state, actor, target, status.statusId, status.chance, registry, log);
  }
}

function applyBuff(
  target: Combatant,
  buff: { modifier: import('../domain/stats.js').StatModifier; rounds: number },
  log: EventLog,
): void {
  target.tempModifiers.push({ modifier: buff.modifier, rounds: buff.rounds });
  for (const [stat, delta] of Object.entries(buff.modifier)) {
    if (typeof delta === 'number') {
      log.push({ type: 'stat-stage-changed', targetId: target.id, stat, delta, stage: delta });
    }
  }
}

function doUnleash(
  state: BattleState,
  actor: Combatant,
  moteId: string,
  targetId: string | undefined,
  registry: ContentRegistry,
  config: DamageConfig,
  log: EventLog,
): void {
  if (!actor.actor) return;
  const moteDef = registry.moteDef(moteId);
  if (!moteDef) return;

  const classBefore = deriveClass(actor.actor, registry.actorContext())?.id ?? null;
  if (!unleashMote(actor.actor.motes, moteId)) return;

  log.push({ type: 'mote-unleashed', combatantId: actor.id, moteId, element: moteDef.element });

  // Losing a set mote can drop the actor out of their class, which changes
  // their stats mid-round. Re-sync the cached vitals and announce it.
  syncActorVitals(actor, registry);
  const classAfter = deriveClass(actor.actor, registry.actorContext())?.id ?? null;
  if (classAfter !== classBefore && classAfter !== null) {
    log.push({ type: 'class-changed', combatantId: actor.id, fromClassId: classBefore, toClassId: classAfter });
  }

  applyMoteEffect(state, actor, moteDef, targetId, registry, config, log);
}

function applyMoteEffect(
  state: BattleState,
  actor: Combatant,
  moteDef: MoteDef,
  targetId: string | undefined,
  registry: ContentRegistry,
  config: DamageConfig,
  log: EventLog,
): void {
  const effect = moteDef.effect;
  const targets = selectMoteTargets(state, actor, effect.kind === 'utility' ? 'self' : targetingOf(effect), targetId);
  const attackerStats = statsOf(actor, registry);

  for (const target of targets) {
    switch (effect.kind) {
      case 'damage': {
        const defenderStats = statsOf(target, registry);
        const result = rollDamage(
          state.rng,
          {
            attacker: attackerStats,
            defender: defenderStats,
            element: moteDef.element,
            defenderInnate: target.innate,
            power: effect.power,
            kind: effect.ignoresDefense ? 'fixed' : 'aetheric',
            defending: target.defending,
          },
          config,
        );
        dealDamage(state, actor, target, result.amount, moteDef.element, result.critical, result.effectiveness, log, registry);
        break;
      }
      case 'heal': {
        const healed = rollHeal(state.rng, effect.power, attackerStats.power[moteDef.element], target.maxHp);
        applyRawHeal(state, actor, target, healed, log);
        break;
      }
      case 'revive': {
        if (!target.downed) break;
        const hp = Math.max(1, Math.round(target.maxHp * effect.hpFraction));
        target.downed = false;
        target.hp = hp;
        if (target.actor) target.actor.hp = hp;
        log.push({ type: 'revived', combatantId: target.id, hp });
        break;
      }
      case 'status':
        tryApplyStatus(state, actor, target, effect.statusId, effect.chance, registry, log);
        break;
      case 'buff':
        applyBuff(target, { modifier: effect.modifier, rounds: effect.rounds }, log);
        break;
      case 'restore-aether': {
        const amount = Math.min(effect.amount, target.maxAether - target.aether);
        if (amount > 0) {
          target.aether += amount;
          if (target.actor) target.actor.aether = target.aether;
          log.push({ type: 'aether-restored', combatantId: target.id, amount });
        }
        break;
      }
      case 'utility':
        log.push({ type: 'message', text: `${moteDef.name}: ${effect.tag}` });
        break;
    }
  }
}

function targetingOf(effect: MoteDef['effect']): string {
  return 'targeting' in effect ? effect.targeting : 'self';
}

function doSummon(
  state: BattleState,
  actor: Combatant,
  summonId: string,
  registry: ContentRegistry,
  config: DamageConfig,
  log: EventLog,
): void {
  if (!actor.actor) return;
  const summon = registry.summonDef(summonId);
  if (!summon) return;

  const spent = paySummonCost(summon, actor.actor.motes, registry.moteDef);
  if (spent.length === 0) {
    log.push({ type: 'turn-skipped', combatantId: actor.id, reason: 'insufficient-motes' });
    return;
  }

  syncActorVitals(actor, registry);
  log.push({ type: 'summon-called', combatantId: actor.id, summonId, motesSpent: spent.length });

  const foes = livingCombatants(state, opposingSide(actor.side));
  const targets = summon.hitsAll ? foes : foes.slice(0, 1);
  const attackerStats = statsOf(actor, registry);
  const totalCost = summonCostTotal(summon);

  for (const target of targets) {
    const base = summonBaseDamage(summon, totalCost, target.maxHp);
    const result = rollDamage(
      state.rng,
      {
        attacker: attackerStats,
        defender: statsOf(target, registry),
        element: summon.element,
        defenderInnate: target.innate,
        power: base,
        kind: 'aetheric',
        defending: target.defending,
        canCrit: false,
      },
      config,
    );
    dealDamage(state, actor, target, result.amount, summon.element, false, result.effectiveness, log, registry);
  }

  if (summon.afterglowPower && summon.afterglowRounds) {
    applyBuff(
      actor,
      { modifier: { power: { [summon.element]: summon.afterglowPower } }, rounds: summon.afterglowRounds },
      log,
    );
  }
}

function doFlee(state: BattleState, actor: Combatant, registry: ContentRegistry, log: EventLog): void {
  if (state.noFlee) {
    log.push({ type: 'flee-failed', side: actor.side });
    return;
  }
  const allies = livingCombatants(state, actor.side);
  const foes = livingCombatants(state, opposingSide(actor.side));
  const ourAgility = average(allies.map((c) => statsOf(c, registry).agility));
  const theirAgility = average(foes.map((c) => statsOf(c, registry).agility));
  const resistance = Math.max(
    0,
    ...foes.map((c) => (c.enemyId ? registry.enemyDef(c.enemyId)?.fleeResistance ?? 0 : 0)),
  );
  const odds = Math.max(0.05, Math.min(0.95, (ourAgility / Math.max(1, ourAgility + theirAgility)) * 1.5 - resistance));

  if (chance(state.rng, odds)) {
    log.push({ type: 'flee-succeeded', side: actor.side });
    state.outcome = 'fled';
    state.phase = 'ended';
    log.push({ type: 'battle-ended', outcome: 'fled' });
  } else {
    log.push({ type: 'flee-failed', side: actor.side });
  }
}

function dealDamage(
  state: BattleState,
  source: Combatant | null,
  target: Combatant,
  amount: number,
  element: Element | null,
  critical: boolean,
  effectiveness: number,
  log: EventLog,
  registry: ContentRegistry,
): void {
  if (target.downed) return;
  const applied = Math.min(amount, target.hp);
  target.hp -= applied;
  if (target.actor) target.actor.hp = target.hp;

  log.push({
    type: 'damage',
    sourceId: source?.id ?? null,
    targetId: target.id,
    amount: applied,
    element,
    critical,
    effective: effectiveness,
  });

  // Some statuses break the moment their bearer is struck.
  const survivors = target.statuses.filter((status) => !registry.statusDef(status.statusId)?.breaksOnDamage);
  if (survivors.length !== target.statuses.length) {
    for (const status of target.statuses) {
      if (registry.statusDef(status.statusId)?.breaksOnDamage) {
        log.push({ type: 'status-expired', targetId: target.id, statusId: status.statusId });
      }
    }
    target.statuses = survivors;
  }
}

function applyRawHeal(state: BattleState, source: Combatant | null, target: Combatant, amount: number, log: EventLog): void {
  if (target.downed) return;
  const applied = Math.min(amount, target.maxHp - target.hp);
  if (applied <= 0) return;
  target.hp += applied;
  if (target.actor) target.actor.hp = target.hp;
  log.push({ type: 'heal', sourceId: source?.id ?? null, targetId: target.id, amount: applied });
}

function tryApplyStatus(
  state: BattleState,
  source: Combatant | null,
  target: Combatant,
  statusId: string,
  baseChance: number,
  registry: ContentRegistry,
  log: EventLog,
): void {
  if (target.downed) return;
  const def = registry.statusDef(statusId);
  const luck = statsOf(target, registry).luck;
  const landChance = statusLandChance(baseChance, def, luck);

  if (!chance(state.rng, landChance)) {
    log.push({ type: 'status-resisted', targetId: target.id, statusId });
    return;
  }

  if (def?.exclusiveGroup) {
    target.statuses = target.statuses.filter(
      (s) => registry.statusDef(s.statusId)?.exclusiveGroup !== def.exclusiveGroup,
    );
  }
  if (target.statuses.some((s) => s.statusId === statusId)) return;

  const duration = def?.duration.kind === 'rounds' ? def.duration.rounds : Number.POSITIVE_INFINITY;
  target.statuses.push({ statusId, remaining: duration, sourceId: source?.id ?? null });
  log.push({ type: 'status-applied', targetId: target.id, statusId, duration: Number.isFinite(duration) ? duration : -1 });

  if (def?.incapacitates) {
    target.downed = true;
    log.push({ type: 'downed', combatantId: target.id });
  }
}

function endOfRound(state: BattleState, registry: ContentRegistry, log: EventLog): void {
  for (const combatant of state.combatants) {
    if (combatant.downed) continue;

    for (const status of combatant.statuses) {
      const def = registry.statusDef(status.statusId);
      if (!def) continue;
      if (def.degenPerRound) {
        const amount = Math.max(1, Math.round(combatant.maxHp * def.degenPerRound));
        dealDamage(state, null, combatant, amount, def.element ?? null, false, 1, log, registry);
      }
      if (def.regenPerRound) {
        applyRawHeal(state, null, combatant, Math.max(1, Math.round(combatant.maxHp * def.regenPerRound)), log);
      }
    }

    const kept: typeof combatant.statuses = [];
    for (const status of combatant.statuses) {
      if (!Number.isFinite(status.remaining)) {
        kept.push(status);
        continue;
      }
      const remaining = status.remaining - 1;
      if (remaining <= 0) log.push({ type: 'status-expired', targetId: combatant.id, statusId: status.statusId });
      else kept.push({ ...status, remaining });
    }
    combatant.statuses = kept;

    combatant.tempModifiers = combatant.tempModifiers
      .map((mod) => ({ ...mod, rounds: mod.rounds - 1 }))
      .filter((mod) => mod.rounds > 0);

    for (const [artId, rounds] of Object.entries(combatant.cooldowns)) {
      if (rounds <= 1) delete combatant.cooldowns[artId];
      else combatant.cooldowns[artId] = rounds - 1;
    }

    if (combatant.actor) {
      for (const moteId of tickRecovery(combatant.actor.motes)) {
        log.push({ type: 'mote-recovered', combatantId: combatant.id, moteId });
      }
      syncActorVitals(combatant, registry);
    }
  }
  reapDowned(state, log, registry);
}

function reapDowned(state: BattleState, log: EventLog, registry: ContentRegistry): void {
  for (const combatant of state.combatants) {
    if (!combatant.downed && combatant.hp <= 0) {
      combatant.downed = true;
      combatant.hp = 0;
      if (combatant.actor) combatant.actor.hp = 0;
      log.push({ type: 'downed', combatantId: combatant.id });
    }
  }
}

function finishBattle(state: BattleState, registry: ContentRegistry, log: EventLog): void {
  const partyAlive = livingCombatants(state, 'party').length > 0;
  state.outcome = partyAlive ? 'victory' : 'defeat';
  state.phase = 'ended';

  if (state.outcome === 'victory') {
    let xp = 0;
    let coin = 0;
    const itemIds: string[] = [];
    for (const combatant of state.combatants) {
      if (combatant.side !== 'foe' || !combatant.enemyId) continue;
      const def = registry.enemyDef(combatant.enemyId);
      if (!def) continue;
      xp += def.xp;
      coin += def.coin;
      for (const drop of def.drops ?? []) {
        if (chance(state.rng, drop.chance)) itemIds.push(drop.itemId);
      }
    }
    state.rewards = { xp, coin, itemIds };
    log.push({ type: 'reward', xp, coin, itemIds });
  }

  log.push({ type: 'battle-ended', outcome: state.outcome });
}

// --- helpers -------------------------------------------------------------

/** Current stats for a combatant, derived fresh for party members. */
export function statsOf(combatant: Combatant, registry: ContentRegistry): StatBlock {
  const base = combatant.actor
    ? deriveStats(combatant.actor, registry.actorContext())
    : combatant.enemyStats ?? fallbackStats();

  if (combatant.tempModifiers.length === 0) return base;
  return normalizeStats(applyModifiers(base, combatant.tempModifiers.map((m) => m.modifier)));
}

function fallbackStats(): StatBlock {
  return normalizeStats({
    maxHp: 1,
    maxAether: 0,
    attack: 1,
    defense: 0,
    agility: 1,
    luck: 0,
    power: { terra: 0, pyre: 0, aeris: 0, rime: 0 },
    resist: { terra: 0, pyre: 0, aeris: 0, rime: 0 },
  });
}

/** Re-read max HP/aether after a class or mote change and clamp current values. */
function syncActorVitals(combatant: Combatant, registry: ContentRegistry): void {
  if (!combatant.actor) return;
  const stats = statsOf(combatant, registry);
  combatant.maxHp = stats.maxHp;
  combatant.maxAether = stats.maxAether;
  combatant.hp = Math.min(combatant.hp, stats.maxHp);
  combatant.aether = Math.min(combatant.aether, stats.maxAether);
  combatant.actor.hp = combatant.hp;
  combatant.actor.aether = combatant.aether;
}

function actionBlocker(
  combatant: Combatant,
  registry: ContentRegistry,
): { statusId: string; shakeOff: boolean } | null {
  for (const status of combatant.statuses) {
    const def = registry.statusDef(status.statusId);
    if (def?.preventsAction) {
      return { statusId: status.statusId, shakeOff: false };
    }
  }
  return null;
}

function artsBlocked(combatant: Combatant, registry: ContentRegistry): boolean {
  return combatant.statuses.some((status) => registry.statusDef(status.statusId)?.preventsArts);
}

function weaponElement(combatant: Combatant, registry: ContentRegistry): Element | null {
  const weaponId = combatant.actor?.equipment.weapon;
  if (!weaponId) return null;
  return registry.gearDef(weaponId)?.element ?? null;
}

function rollGearProc(rng: RngState, combatant: Combatant, registry: ContentRegistry) {
  if (!combatant.actor) return undefined;
  for (const proc of gearProcs(combatant.actor.equipment, registry.gearDef)) {
    if (chance(rng, proc.chance)) return proc;
  }
  return undefined;
}

function resolveTarget(state: BattleState, actor: Combatant, targetId: string): Combatant | undefined {
  const target = findCombatant(state, targetId);
  if (target && !target.downed) return target;
  // Retarget rather than wasting the turn when the chosen target already fell.
  return pick(state.rng, livingCombatants(state, opposingSide(actor.side)));
}

function selectTargets(
  state: BattleState,
  actor: Combatant,
  art: ArtDef,
  targetId: string | undefined,
): Combatant[] {
  const foes = livingCombatants(state, opposingSide(actor.side));
  const allies = livingCombatants(state, actor.side);

  switch (art.targeting) {
    case 'self':
      return [actor];
    case 'all-foes':
      return foes;
    case 'all-allies':
      return allies;
    case 'downed-ally': {
      const downed = state.combatants.filter((c) => c.side === actor.side && c.downed);
      const chosen = targetId ? downed.find((c) => c.id === targetId) : downed[0];
      return chosen ? [chosen] : [];
    }
    case 'one-ally': {
      const chosen = (targetId ? allies.find((c) => c.id === targetId) : undefined) ?? pick(state.rng, allies);
      return chosen ? [chosen] : [];
    }
    case 'spread-foes': {
      const primary = (targetId ? foes.find((c) => c.id === targetId) : undefined) ?? pick(state.rng, foes);
      if (!primary) return [];
      // Primary first so falloff distances measure from it.
      return [primary, ...foes.filter((c) => c.id !== primary.id)];
    }
    case 'one-foe':
    default: {
      const chosen = (targetId ? foes.find((c) => c.id === targetId) : undefined) ?? pick(state.rng, foes);
      return chosen ? [chosen] : [];
    }
  }
}

function selectMoteTargets(
  state: BattleState,
  actor: Combatant,
  targeting: string,
  targetId: string | undefined,
): Combatant[] {
  const foes = livingCombatants(state, opposingSide(actor.side));
  const allies = livingCombatants(state, actor.side);
  switch (targeting) {
    case 'all-foes':
      return foes;
    case 'all-allies':
      return allies;
    case 'one-ally': {
      const chosen = (targetId ? allies.find((c) => c.id === targetId) : undefined) ?? actor;
      return [chosen];
    }
    case 'self':
      return [actor];
    case 'one-foe':
    default: {
      const chosen = (targetId ? foes.find((c) => c.id === targetId) : undefined) ?? pick(state.rng, foes);
      return chosen ? [chosen] : [];
    }
  }
}

function average(values: number[]): number {
  if (values.length === 0) return 0;
  return values.reduce((a, b) => a + b, 0) / values.length;
}
