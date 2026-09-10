/**
 * Save files.
 *
 * Every piece of runtime state in this library is already plain JSON-safe
 * data, so saving is mostly a matter of stamping a version and writing it out.
 * The version matters: content packs change, and a save written against an
 * older schema should migrate rather than crash. `migrate` is the single place
 * that happens.
 */

import type { ActorState } from '../domain/actor.js';
import type { Loadout } from '../domain/equipment.js';
import type { Vec2 } from '../field/grid.js';

export const SAVE_VERSION = 1;

export interface InventoryEntry {
  itemId: string;
  count: number;
}

export interface SaveFile {
  version: number;
  /** Content packs this save expects, so a mismatch can be reported. */
  packs: string[];
  savedAt: string;
  playTimeSeconds: number;
  party: ActorState[];
  /** Actors recruited but not in the active party. */
  reserve: ActorState[];
  inventory: InventoryEntry[];
  coin: number;
  /** Motes collected but not yet bound to anyone. */
  looseMotes: string[];
  location: { mapId: string; position: Vec2; facing: string };
  /** Arbitrary project-specific progress flags. */
  flags: Record<string, string | number | boolean>;
}

export interface SaveInput {
  packs: string[];
  party: ActorState[];
  reserve?: ActorState[];
  inventory?: InventoryEntry[];
  coin?: number;
  looseMotes?: string[];
  location: { mapId: string; position: Vec2; facing: string };
  flags?: Record<string, string | number | boolean>;
  playTimeSeconds?: number;
}

export function createSave(input: SaveInput): SaveFile {
  return {
    version: SAVE_VERSION,
    packs: input.packs,
    savedAt: new Date().toISOString(),
    playTimeSeconds: input.playTimeSeconds ?? 0,
    party: input.party.map(cloneActor),
    reserve: (input.reserve ?? []).map(cloneActor),
    inventory: (input.inventory ?? []).map((entry) => ({ ...entry })),
    coin: input.coin ?? 0,
    looseMotes: [...(input.looseMotes ?? [])],
    location: { ...input.location, position: { ...input.location.position } },
    flags: { ...(input.flags ?? {}) },
  };
}

export function serialize(save: SaveFile): string {
  return JSON.stringify(save);
}

export function deserialize(text: string): SaveFile {
  const parsed = JSON.parse(text) as unknown;
  if (!isSaveShaped(parsed)) throw new Error('Not a valid save file');
  return migrate(parsed);
}

/**
 * Bring an older save up to the current version. Each step is written as a
 * discrete `if`, so adding version 2 means adding one block rather than
 * rewriting the function.
 */
export function migrate(save: SaveFile): SaveFile {
  const out: SaveFile = { ...save };

  if (out.version < 1) {
    out.looseMotes = out.looseMotes ?? [];
    out.flags = out.flags ?? {};
    out.version = 1;
  }

  // Defensive fill-ins for fields a hand-edited save might be missing.
  out.reserve = out.reserve ?? [];
  out.inventory = out.inventory ?? [];
  out.looseMotes = out.looseMotes ?? [];
  out.flags = out.flags ?? {};
  for (const actor of [...out.party, ...out.reserve]) {
    actor.statuses = actor.statuses ?? [];
    actor.motes = actor.motes ?? [];
    actor.tempModifiers = actor.tempModifiers ?? [];
    actor.equipment = (actor.equipment ?? {}) as Loadout;

    // A status that lasts until cured carries Infinity as its remaining
    // duration, and JSON has no infinity - `JSON.stringify` writes null. Restore
    // it on the way back in, or `remaining` is a null masquerading as a number:
    // `Number.isFinite(null)` is false so the tick loop happens to keep the
    // status forever, but `null - 1` is -1, so the first piece of code that does
    // arithmetic on it expires a permanent status on the spot.
    for (const status of actor.statuses) {
      if (typeof status.remaining !== 'number' || Number.isNaN(status.remaining)) {
        status.remaining = Number.POSITIVE_INFINITY;
      }
    }
  }

  out.version = SAVE_VERSION;
  return out;
}

function isSaveShaped(value: unknown): value is SaveFile {
  if (typeof value !== 'object' || value === null) return false;
  const candidate = value as Partial<SaveFile>;
  return typeof candidate.version === 'number' && Array.isArray(candidate.party);
}

function cloneActor(actor: ActorState): ActorState {
  return {
    ...actor,
    motes: actor.motes.map((mote) => ({ ...mote })),
    statuses: actor.statuses.map((status) => ({ ...status })),
    equipment: { ...actor.equipment },
    tempModifiers: (actor.tempModifiers ?? []).map((mod) => ({ ...mod })),
  };
}
