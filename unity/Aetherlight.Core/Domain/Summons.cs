using System;
using System.Collections.Generic;

namespace Aetherlight.Domain
{
    /// <summary>
    /// A summon is bought with motes already on standby, so the cost was paid a
    /// turn or more earlier by unleashing them. Damage scales off the target's
    /// max HP plus a flat term, which keeps summons relevant against high-HP
    /// bosses without making them the answer to trash.
    /// </summary>
    public sealed class SummonDef
    {
        public string Id = "";
        public string Name = "";
        public string? Description;
        public Element Element;

        /// <summary>Standby motes required, per element.</summary>
        public ElementTable<int> Cost = ElementTable<int>.Filled(0);

        public double BasePower;

        /// <summary>Extra damage as a fraction of the target's max HP, per mote spent.</summary>
        public double HpFractionPerMote;

        /// <summary>Cap on the max-HP-scaled portion, so bosses are not deleted.</summary>
        public double HpFractionCap = 0.5;

        public bool HitsAll;

        /// <summary>Temporary elemental power granted to the summoner afterwards.</summary>
        public double AfterglowPower;
        public int AfterglowRounds;
    }

    public static class Summons
    {
        public static int CostTotal(SummonDef def)
        {
            int total = 0;
            foreach (var element in Elements.All) total += def.Cost[element];
            return total;
        }

        public static bool CanAfford(SummonDef def, IEnumerable<MoteInstance> motes, Func<string, MoteDef?> moteDefOf)
        {
            var available = Motes.StandbyCounts(motes, moteDefOf);
            foreach (var element in Elements.All)
            {
                int needed = def.Cost[element];
                if (needed > 0 && available[element] < needed) return false;
            }
            return true;
        }

        /// <summary>
        /// Pay the cost, moving motes to Recovering. Returns the spent ids, or
        /// an empty list if the cost could not be met - in which case nothing
        /// is mutated.
        /// </summary>
        public static List<string> PayCost(SummonDef def, IList<MoteInstance> motes, Func<string, MoteDef?> moteDefOf)
        {
            if (!CanAfford(def, motes, moteDefOf)) return new List<string>();

            var spent = new List<string>();
            foreach (var element in Elements.All)
            {
                int needed = def.Cost[element];
                if (needed <= 0) continue;
                spent.AddRange(Motes.SpendStandby(motes, element, needed, moteDefOf));
            }
            return spent;
        }

        /// <summary>Raw damage before affinity and variance, which Damage applies.</summary>
        public static double BaseDamage(SummonDef def, int motesSpent, double targetMaxHp)
        {
            double scaled = Math.Min(def.HpFractionCap, def.HpFractionPerMote * motesSpent) * targetMaxHp;
            return def.BasePower + scaled;
        }
    }
}
