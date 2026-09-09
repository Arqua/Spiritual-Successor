/**
 * Gear.
 *
 * Equipment contributes flat stats, may carry an elemental alignment that
 * feeds the wearer's power table, and may proc an art on a normal attack.
 * Procs are what keep basic attacks from going dead in the late game.
 */

import type { Element } from './elements.js';
import type { StatModifier } from './stats.js';

export const EQUIP_SLOTS = ['weapon', 'armor', 'helm', 'shield', 'trinket'] as const;
export type EquipSlot = (typeof EQUIP_SLOTS)[number];

export interface GearProc {
  /** Art resolved when the proc fires. */
  artId: string;
  /** Probability per qualifying attack. */
  chance: number;
  /** Power multiplier applied to the art on top of its own. */
  powerScale?: number;
}

export interface GearDef {
  id: string;
  name: string;
  description?: string;
  slot: EquipSlot;
  modifier?: StatModifier;
  element?: Element;
  /** Fires on a normal attack. */
  proc?: GearProc;
  /** Actors allowed to equip it. Empty means anyone. */
  restrictedTo?: string[];
  value?: number;
}

export type Loadout = Partial<Record<EquipSlot, string>>;

export function equippedIds(loadout: Loadout): string[] {
  const ids: string[] = [];
  for (const slot of EQUIP_SLOTS) {
    const id = loadout[slot];
    if (id) ids.push(id);
  }
  return ids;
}

export function canEquip(gear: GearDef, actorId: string): boolean {
  if (!gear.restrictedTo || gear.restrictedTo.length === 0) return true;
  return gear.restrictedTo.includes(actorId);
}

export function gearModifiers(
  loadout: Loadout,
  defOf: (id: string) => GearDef | undefined,
): StatModifier[] {
  const mods: StatModifier[] = [];
  for (const id of equippedIds(loadout)) {
    const def = defOf(id);
    if (def?.modifier) mods.push(def.modifier);
  }
  return mods;
}

/** Procs available from currently worn gear, in slot order. */
export function gearProcs(
  loadout: Loadout,
  defOf: (id: string) => GearDef | undefined,
): GearProc[] {
  const procs: GearProc[] = [];
  for (const id of equippedIds(loadout)) {
    const def = defOf(id);
    if (def?.proc) procs.push(def.proc);
  }
  return procs;
}
