/**
 * Enemy decision-making.
 *
 * Deliberately simple: a weighted roll over the moves whose gating conditions
 * are met, with target selection biased toward low-HP party members so fights
 * have some bite without needing a behaviour tree. A project that wants boss
 * scripting hooks `behaviorTag` and overrides `chooseEnemyCommand`.
 */

import { nextFloat, weightedPick, type RngState } from '../core/rng.js';
import type { ContentRegistry } from '../core/registry.js';
import type { EnemyMove } from '../domain/enemy.js';
import { isOffensive } from '../domain/arts.js';
import { livingCombatants, opposingSide, type BattleCommand, type BattleState, type Combatant } from './state.js';

/** Probability the AI picks the weakest target rather than a random one. */
export const FOCUS_FIRE_CHANCE = 0.45;

export function chooseEnemyCommand(
  state: BattleState,
  enemy: Combatant,
  registry: ContentRegistry,
): BattleCommand | null {
  const targets = livingCombatants(state, opposingSide(enemy.side));
  if (targets.length === 0) return null;

  const def = enemy.enemyId ? registry.enemyDef(enemy.enemyId) : undefined;
  const hpFraction = enemy.maxHp > 0 ? enemy.hp / enemy.maxHp : 0;

  const usable = (def?.moves ?? []).filter((move) => isMoveUsable(move, enemy, state.round, hpFraction, registry));
  const move = weightedPick(state.rng, usable, (m) => m.weight);

  if (!move || move.artId === 'attack') {
    return { kind: 'attack', combatantId: enemy.id, targetId: chooseTarget(state.rng, targets).id };
  }

  const art = registry.artDef(move.artId);
  if (!art) {
    return { kind: 'attack', combatantId: enemy.id, targetId: chooseTarget(state.rng, targets).id };
  }

  if (move.cooldown) enemy.cooldowns[move.artId] = move.cooldown;

  if (isOffensive(art)) {
    return { kind: 'art', combatantId: enemy.id, artId: art.id, targetId: chooseTarget(state.rng, targets).id };
  }
  // Support and healing arts point back at the enemy's own side.
  const allies = livingCombatants(state, enemy.side);
  const ally = allies.slice().sort((a, b) => a.hp / a.maxHp - b.hp / b.maxHp)[0] ?? enemy;
  return { kind: 'art', combatantId: enemy.id, artId: art.id, targetId: ally.id };
}

function isMoveUsable(
  move: EnemyMove,
  enemy: Combatant,
  round: number,
  hpFraction: number,
  registry: ContentRegistry,
): boolean {
  if (move.hpBelow !== undefined && hpFraction > move.hpBelow) return false;
  if (move.fromRound !== undefined && round < move.fromRound) return false;
  if (enemy.cooldowns[move.artId]) return false;
  if (move.artId !== 'attack') {
    const art = registry.artDef(move.artId);
    if (!art) return false;
    if (art.cost > enemy.aether) return false;
  }
  return true;
}

function chooseTarget(rng: RngState, targets: readonly Combatant[]): Combatant {
  if (nextFloat(rng) < FOCUS_FIRE_CHANCE) {
    return targets.slice().sort((a, b) => a.hp / a.maxHp - b.hp / b.maxHp)[0] as Combatant;
  }
  const index = Math.floor(nextFloat(rng) * targets.length);
  return targets[Math.min(index, targets.length - 1)] as Combatant;
}

/** Convenience: commands for every living enemy in one call. */
export function enemyCommands(state: BattleState, registry: ContentRegistry): BattleCommand[] {
  const commands: BattleCommand[] = [];
  for (const enemy of livingCombatants(state, 'foe')) {
    const command = chooseEnemyCommand(state, enemy, registry);
    if (command) commands.push(command);
  }
  return commands;
}
