/**
 * Items and the party's bag.
 *
 * An item is a delivery mechanism for an art rather than a separate effect
 * system. A potion names the healing art, a bomb names a damage art, and the
 * resolver applies it through exactly the same machinery that handles a cast
 * ability. That reuse is the point: statuses, revives, buffs and multi-hits all
 * work on items the day they work on arts, and a content author learns one
 * shape instead of two.
 *
 * The one thing an item does not share with an art is scaling. A potion heals
 * the same amount whoever drinks it, so an item supplies its own `power` and is
 * resolved against a neutral stat block. An art read off the user's elemental
 * power would make the party's healer the best person to hand a potion to,
 * which is not how a potion works.
 */

import type { ArtTargeting } from './arts.js';

export type ItemKind =
  /** Used up when used. Potions, bombs, antidotes. */
  | 'consumable'
  /** Story or progression items. Never consumed, never usable in battle. */
  | 'key'
  /** Crafting input or sale fodder; has no use of its own. */
  | 'material';

export interface ItemDef {
  id: string;
  name: string;
  description?: string;
  kind: ItemKind;

  /**
   * The art this item applies. Absent for key and material items, which do
   * nothing when used.
   */
  artId?: string;

  /**
   * Power for the applied art, replacing the art's own. An item without this
   * falls back to the art's power, which is usually wrong for a consumable -
   * arts are balanced around a caster's stats and items are not.
   */
  power?: number;

  /** Overrides the art's targeting, e.g. a mass-heal version of a single-target art. */
  targeting?: ArtTargeting;

  usableInBattle?: boolean;
  usableOnField?: boolean;

  /** Consumed on use. False for reusable tools. */
  consumedOnUse?: boolean;

  /** Maximum held in one stack. Absent means no limit. */
  stackLimit?: number;

  /** Shop price. */
  value?: number;
}

export interface InventoryEntry {
  itemId: string;
  count: number;
}

/** The party's bag. A plain array so it serializes without ceremony. */
export type Inventory = InventoryEntry[];

export function countOf(inventory: Inventory, itemId: string): number {
  let total = 0;
  for (const entry of inventory) {
    if (entry.itemId === itemId) total += entry.count;
  }
  return total;
}

export function hasItem(inventory: Inventory, itemId: string, count = 1): boolean {
  return countOf(inventory, itemId) >= count;
}

export interface AddResult {
  /** How many were actually added. */
  added: number;
  /** How many did not fit because of the stack limit. */
  overflow: number;
}

/**
 * Add items, respecting the stack limit.
 *
 * Returns what did not fit rather than silently discarding it, so a caller can
 * tell the player "your bag is full" instead of quietly eating a reward.
 */
export function addItem(
  inventory: Inventory,
  itemId: string,
  count: number,
  defOf: (id: string) => ItemDef | undefined,
): AddResult {
  if (count <= 0) return { added: 0, overflow: 0 };

  const limit = defOf(itemId)?.stackLimit;
  const existing = inventory.find((entry) => entry.itemId === itemId);
  const current = existing?.count ?? 0;

  const room = limit === undefined ? count : Math.max(0, limit - current);
  const added = Math.min(count, room);

  if (added > 0) {
    if (existing) existing.count += added;
    else inventory.push({ itemId, count: added });
  }

  return { added, overflow: count - added };
}

/**
 * Remove items. Returns false and changes nothing if there are not enough,
 * so a partially-paid cost cannot happen.
 */
export function removeItem(inventory: Inventory, itemId: string, count = 1): boolean {
  if (count <= 0) return true;
  if (countOf(inventory, itemId) < count) return false;

  let remaining = count;
  for (let i = inventory.length - 1; i >= 0 && remaining > 0; i--) {
    const entry = inventory[i];
    if (!entry || entry.itemId !== itemId) continue;

    const taken = Math.min(entry.count, remaining);
    entry.count -= taken;
    remaining -= taken;
    if (entry.count <= 0) inventory.splice(i, 1);
  }
  return true;
}

/** Whether an item can be used in the given context, ignoring inventory. */
export function isUsable(def: ItemDef | undefined, context: 'battle' | 'field'): boolean {
  if (!def) return false;
  if (!def.artId) return false;
  return context === 'battle' ? def.usableInBattle === true : def.usableOnField === true;
}

/** Drop empty stacks and merge duplicates. Useful after hand-editing a save. */
export function normalizeInventory(inventory: Inventory): Inventory {
  const merged = new Map<string, number>();
  for (const entry of inventory) {
    if (entry.count <= 0) continue;
    merged.set(entry.itemId, (merged.get(entry.itemId) ?? 0) + entry.count);
  }
  return [...merged.entries()].map(([itemId, count]) => ({ itemId, count }));
}
