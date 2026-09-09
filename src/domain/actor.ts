/**
 * Actors.
 *
 * `ActorState` is plain data: level, current HP, which motes are bound and in
 * what state, what is equipped. Everything else - stats, class, known arts -
 * is derived on demand from that state plus the content registry. There is no
 * "recalculate" call to forget, and any state object can be JSON round-tripped
 * without custom serialization.
 */

import { type Element } from './elements.js';
import {
  applyModifiers,
  normalizeStats,
  scaleStats,
  statsAtLevel,
  type GrowthProfile,
  type StatBlock,
  type StatModifier,
} from './stats.js';
import { setCounts, type MoteDef, type MoteInstance } from './motes.js';
import { classArtsAtLevel, resolveClass, type ClassDef } from './classes.js';
import { gearModifiers, type GearDef, type Loadout } from './equipment.js';
import type { StatusDef, StatusInstance } from './status.js';

export interface ActorDef {
  id: string;
  name: string;
  innate: Element;
  growth: GrowthProfile;
  /** Arts known regardless of class. */
  baseArts?: string[];
  /** Slots that cannot be filled, for actors who never use a shield, etc. */
  blockedSlots?: string[];
  portraitId?: string;
  spriteId?: string;
}

export interface ActorState {
  /** Instance id, unique within a save. Distinct from `defId`. */
  id: string;
  defId: string;
  level: number;
  xp: number;
  hp: number;
  aether: number;
  motes: MoteInstance[];
  equipment: Loadout;
  statuses: StatusInstance[];
  /** Temporary battle-only modifiers, cleared when the fight ends. */
  tempModifiers?: { modifier: StatModifier; rounds: number }[];
}

/** Everything the derivation functions need to look up. */
export interface ActorContext {
  actorDef: (id: string) => ActorDef | undefined;
  moteDef: (id: string) => MoteDef | undefined;
  classDefs: readonly ClassDef[];
  gearDef: (id: string) => GearDef | undefined;
  statusDef: (id: string) => StatusDef | undefined;
}

export function createActorState(def: ActorDef, level: number, ctx: ActorContext): ActorState {
  const state: ActorState = {
    id: def.id,
    defId: def.id,
    level: Math.max(1, Math.floor(level)),
    xp: 0,
    hp: 1,
    aether: 0,
    motes: [],
    equipment: {},
    statuses: [],
    tempModifiers: [],
  };
  const stats = deriveStats(state, ctx);
  state.hp = stats.maxHp;
  state.aether = stats.maxAether;
  return state;
}

/** Set motes grouped by element - the input to class resolution. */
export function moteCounts(state: ActorState, ctx: ActorContext): Record<Element, number> {
  return setCounts(state.motes, ctx.moteDef);
}

export function deriveClass(state: ActorState, ctx: ActorContext): ClassDef | undefined {
  const def = ctx.actorDef(state.defId);
  if (!def) return undefined;
  return resolveClass(ctx.classDefs, def.innate, moteCounts(state, ctx));
}

/**
 * Full stat block, composed in a fixed order so results are reproducible:
 *
 *   1. level growth curve
 *   2. class multipliers
 *   3. set-mote bonuses
 *   4. equipment
 *   5. status effects
 *   6. temporary battle buffs
 */
export function deriveStats(state: ActorState, ctx: ActorContext): StatBlock {
  const def = ctx.actorDef(state.defId);
  if (!def) return normalizeStats(statsAtLevel(fallbackGrowth(), state.level));

  let stats = statsAtLevel(def.growth, state.level);

  const classDef = deriveClass(state, ctx);
  if (classDef?.statMultipliers) stats = scaleStats(stats, classDef.statMultipliers);

  const mods: (StatModifier | undefined)[] = [];
  for (const mote of state.motes) {
    if (mote.state !== 'set') continue;
    mods.push(ctx.moteDef(mote.defId)?.bonus);
  }
  mods.push(...gearModifiers(state.equipment, ctx.gearDef));
  for (const status of state.statuses) mods.push(ctx.statusDef(status.statusId)?.modifier);
  for (const temp of state.tempModifiers ?? []) mods.push(temp.modifier);

  return normalizeStats(applyModifiers(stats, mods));
}

/** Arts the actor can currently use: innate list plus whatever the class grants. */
export function knownArts(state: ActorState, ctx: ActorContext): string[] {
  const def = ctx.actorDef(state.defId);
  const arts = new Set<string>(def?.baseArts ?? []);
  for (const artId of classArtsAtLevel(deriveClass(state, ctx), state.level)) arts.add(artId);
  return [...arts];
}

export function isDowned(state: ActorState, ctx: ActorContext): boolean {
  if (state.hp <= 0) return true;
  for (const status of state.statuses) {
    if (ctx.statusDef(status.statusId)?.incapacitates) return true;
  }
  return false;
}

export function hasStatus(state: ActorState, statusId: string): boolean {
  return state.statuses.some((status) => status.statusId === statusId);
}

/** Clamp HP/aether into range after a stat change (a class swap, gear change). */
export function clampVitals(state: ActorState, ctx: ActorContext): void {
  const stats = deriveStats(state, ctx);
  state.hp = Math.max(0, Math.min(state.hp, stats.maxHp));
  state.aether = Math.max(0, Math.min(state.aether, stats.maxAether));
}

/** Full restore, as at an inn or after a save point. */
export function restore(state: ActorState, ctx: ActorContext): void {
  const stats = deriveStats(state, ctx);
  state.hp = stats.maxHp;
  state.aether = stats.maxAether;
  state.statuses = [];
  state.tempModifiers = [];
}

function fallbackGrowth(): GrowthProfile {
  const flat = { base: 10, gain: 1 };
  return { maxHp: flat, maxAether: flat, attack: flat, defense: flat, agility: flat, luck: flat };
}
