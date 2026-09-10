using System;
using System.Collections.Generic;

namespace Aetherlight.Domain
{
    public enum EquipSlot
    {
        Weapon,
        Armor,
        Helm,
        Shield,
        Trinket,
    }

    /// <summary>Fires on a normal attack, which keeps basic attacks alive late.</summary>
    public sealed class GearProc
    {
        public string ArtId = "";
        public double Chance;
        public double PowerScale = 1;
    }

    public sealed class GearDef
    {
        public string Id = "";
        public string Name = "";
        public string? Description;
        public EquipSlot Slot;
        public StatModifier? Modifier;
        public Element? Element;
        public GearProc? Proc;
        /// <summary>Actors allowed to equip it. Empty means anyone.</summary>
        public List<string> RestrictedTo = new List<string>();
        public double Value;
    }

    /// <summary>What an actor is wearing, by slot.</summary>
    public sealed class Loadout
    {
        private readonly Dictionary<EquipSlot, string> _slots = new Dictionary<EquipSlot, string>();

        public string? this[EquipSlot slot]
        {
            get => _slots.TryGetValue(slot, out var id) ? id : null;
            set
            {
                if (value == null) _slots.Remove(slot);
                else _slots[slot] = value;
            }
        }

        public IEnumerable<string> EquippedIds()
        {
            foreach (EquipSlot slot in Enum.GetValues(typeof(EquipSlot)))
            {
                if (_slots.TryGetValue(slot, out var id)) yield return id;
            }
        }

        public Loadout Clone()
        {
            var copy = new Loadout();
            foreach (var pair in _slots) copy[pair.Key] = pair.Value;
            return copy;
        }
    }

    public static class Equipment
    {
        public static bool CanEquip(GearDef gear, string actorId) =>
            gear.RestrictedTo.Count == 0 || gear.RestrictedTo.Contains(actorId);

        public static List<StatModifier> Modifiers(Loadout loadout, Func<string, GearDef?> defOf)
        {
            var mods = new List<StatModifier>();
            foreach (var id in loadout.EquippedIds())
            {
                var mod = defOf(id)?.Modifier;
                if (mod != null) mods.Add(mod);
            }
            return mods;
        }

        public static List<GearProc> Procs(Loadout loadout, Func<string, GearDef?> defOf)
        {
            var procs = new List<GearProc>();
            foreach (var id in loadout.EquippedIds())
            {
                var proc = defOf(id)?.Proc;
                if (proc != null) procs.Add(proc);
            }
            return procs;
        }
    }
}
