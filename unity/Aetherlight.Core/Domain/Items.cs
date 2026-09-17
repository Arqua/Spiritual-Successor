using System;
using System.Collections.Generic;

namespace Aetherlight.Domain
{
    public enum ItemKind
    {
        /// <summary>Used up when used. Potions, bombs, antidotes.</summary>
        Consumable,
        /// <summary>Story or progression items. Never consumed, never usable in battle.</summary>
        Key,
        /// <summary>Crafting input or sale fodder; has no use of its own.</summary>
        Material,
    }

    /// <summary>
    /// An item is a delivery mechanism for an art rather than a separate effect
    /// system. A potion names the healing art, a bomb names a damage art, and
    /// the resolver applies it through exactly the same machinery that handles
    /// a cast ability. Statuses, revives, buffs and multi-hits all work on items
    /// the day they work on arts.
    ///
    /// The one thing an item does not share with an art is scaling. A potion
    /// heals the same amount whoever drinks it, so an item supplies its own
    /// Power and resolves against a neutral stat block.
    /// </summary>
    public sealed class ItemDef
    {
        public string Id = "";
        public string Name = "";
        public string? Description;
        public ItemKind Kind;

        /// <summary>
        /// The art this item applies. Null for key and material items, which do
        /// nothing when used.
        /// </summary>
        public string? ArtId;

        /// <summary>
        /// Power for the applied art, replacing the art's own. Arts are balanced
        /// around a caster's stats and items are not, so leaving this at the
        /// art's value is usually wrong for a consumable.
        ///
        /// Note that a damage item should name an *aetheric* art: item damage
        /// reads its own power, whereas a physical art scales off attack, and a
        /// neutral stat block has almost none.
        /// </summary>
        public double? Power;

        /// <summary>Overrides the art's targeting.</summary>
        public ArtTargeting? Targeting;

        public bool UsableInBattle;
        public bool UsableOnField;

        /// <summary>Consumed on use. False for reusable tools.</summary>
        public bool ConsumedOnUse;

        /// <summary>Maximum held in one stack. Null means no limit.</summary>
        public int? StackLimit;

        public double Value;
    }

    public sealed class InventoryEntry
    {
        public string ItemId = "";
        public int Count;

        public InventoryEntry() { }

        public InventoryEntry(string itemId, int count)
        {
            ItemId = itemId;
            Count = count;
        }

        public InventoryEntry Clone() => new InventoryEntry(ItemId, Count);
    }

    public struct AddResult
    {
        /// <summary>How many were actually added.</summary>
        public int Added;
        /// <summary>How many did not fit because of the stack limit.</summary>
        public int Overflow;
    }

    /// <summary>The party's bag. A plain list, so it serializes without ceremony.</summary>
    public static class Inventories
    {
        public static int CountOf(IEnumerable<InventoryEntry> inventory, string itemId)
        {
            int total = 0;
            foreach (var entry in inventory)
            {
                if (entry.ItemId == itemId) total += entry.Count;
            }
            return total;
        }

        public static bool Has(IEnumerable<InventoryEntry> inventory, string itemId, int count = 1) =>
            CountOf(inventory, itemId) >= count;

        /// <summary>
        /// Add items, respecting the stack limit. Returns what did not fit
        /// rather than silently discarding it, so a caller can say "your bag is
        /// full" instead of quietly eating a reward.
        /// </summary>
        public static AddResult Add(
            IList<InventoryEntry> inventory,
            string itemId,
            int count,
            Func<string, ItemDef?> defOf)
        {
            if (count <= 0) return new AddResult();

            int? limit = defOf(itemId)?.StackLimit;
            InventoryEntry? existing = null;
            foreach (var entry in inventory)
            {
                if (entry.ItemId == itemId) { existing = entry; break; }
            }

            int current = existing?.Count ?? 0;
            int room = limit.HasValue ? Math.Max(0, limit.Value - current) : count;
            int added = Math.Min(count, room);

            if (added > 0)
            {
                if (existing != null) existing.Count += added;
                else inventory.Add(new InventoryEntry(itemId, added));
            }

            return new AddResult { Added = added, Overflow = count - added };
        }

        /// <summary>
        /// Remove items. Returns false and changes nothing if there are not
        /// enough, so a partially-paid cost cannot happen.
        /// </summary>
        public static bool Remove(IList<InventoryEntry> inventory, string itemId, int count = 1)
        {
            if (count <= 0) return true;
            if (CountOf(inventory, itemId) < count) return false;

            int remaining = count;
            for (int i = inventory.Count - 1; i >= 0 && remaining > 0; i--)
            {
                var entry = inventory[i];
                if (entry.ItemId != itemId) continue;

                int taken = Math.Min(entry.Count, remaining);
                entry.Count -= taken;
                remaining -= taken;
                if (entry.Count <= 0) inventory.RemoveAt(i);
            }
            return true;
        }

        /// <summary>Whether an item can be used in the given context, ignoring the bag.</summary>
        public static bool IsUsable(ItemDef? def, bool inBattle)
        {
            if (def == null) return false;
            if (string.IsNullOrEmpty(def.ArtId)) return false;
            return inBattle ? def.UsableInBattle : def.UsableOnField;
        }

        /// <summary>Drop empty stacks and merge duplicates, e.g. after a hand-edited save.</summary>
        public static List<InventoryEntry> Normalize(IEnumerable<InventoryEntry> inventory)
        {
            var order = new List<string>();
            var merged = new Dictionary<string, int>();
            foreach (var entry in inventory)
            {
                if (entry.Count <= 0) continue;
                if (!merged.ContainsKey(entry.ItemId))
                {
                    merged[entry.ItemId] = 0;
                    order.Add(entry.ItemId);
                }
                merged[entry.ItemId] += entry.Count;
            }

            var outList = new List<InventoryEntry>(order.Count);
            foreach (var itemId in order) outList.Add(new InventoryEntry(itemId, merged[itemId]));
            return outList;
        }
    }
}
