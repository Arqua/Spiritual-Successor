using System;
using System.Collections.Generic;
using System.Linq;

namespace Aetherlight.Domain
{
    /// <summary>
    /// A mote sits in one of three states.
    ///
    /// Set        - bound to an actor. Contributes its stat bonus and counts
    ///              toward the actor's class.
    /// Standby    - unleashed this battle. Its bonus and class contribution are
    ///              gone until it returns, but it can be spent on a summon.
    /// Recovering - spent on a summon. Returns to Set after a few rounds.
    /// </summary>
    public enum MoteState
    {
        Set,
        Standby,
        Recovering,
    }

    public sealed class MoteDef
    {
        public string Id = "";
        public string Name = "";
        public Element Element;
        public StatModifier? Bonus;
        public int RecoveryRounds = Motes.DefaultRecoveryRounds;
        public string? FieldAbilityId;
    }

    public sealed class MoteInstance
    {
        public string DefId = "";
        public MoteState State = MoteState.Set;
        /// <summary>Rounds left in Recovering. Zero in every other state.</summary>
        public int RecoveryLeft;

        public MoteInstance() { }

        public MoteInstance(string defId, MoteState state = MoteState.Set)
        {
            DefId = defId;
            State = state;
        }

        public MoteInstance Clone() => new MoteInstance { DefId = DefId, State = State, RecoveryLeft = RecoveryLeft };
    }

    public static class Motes
    {
        public const int DefaultRecoveryRounds = 3;

        /// <summary>Motes currently contributing to class and stats, by element.</summary>
        public static ElementTable<int> SetCounts(IEnumerable<MoteInstance> motes, Func<string, MoteDef?> defOf)
        {
            var counts = ElementTable<int>.Filled(0);
            foreach (var mote in motes)
            {
                if (mote.State != MoteState.Set) continue;
                var def = defOf(mote.DefId);
                if (def != null) counts[def.Element] += 1;
            }
            return counts;
        }

        /// <summary>Motes on standby, which are the currency for summons.</summary>
        public static ElementTable<int> StandbyCounts(IEnumerable<MoteInstance> motes, Func<string, MoteDef?> defOf)
        {
            var counts = ElementTable<int>.Filled(0);
            foreach (var mote in motes)
            {
                if (mote.State != MoteState.Standby) continue;
                var def = defOf(mote.DefId);
                if (def != null) counts[def.Element] += 1;
            }
            return counts;
        }

        /// <summary>Move a set mote to standby. False if it was not available.</summary>
        public static bool Unleash(IList<MoteInstance> motes, string defId)
        {
            var mote = motes.FirstOrDefault(m => m.DefId == defId && m.State == MoteState.Set);
            if (mote == null) return false;
            mote.State = MoteState.Standby;
            mote.RecoveryLeft = 0;
            return true;
        }

        /// <summary>
        /// Spend standby motes of an element on a summon, moving them to
        /// Recovering. Returns the ids spent, empty if the cost could not be
        /// met - in which case nothing is mutated.
        /// </summary>
        public static List<string> SpendStandby(IList<MoteInstance> motes, Element element, int count, Func<string, MoteDef?> defOf)
        {
            var available = motes
                .Where(m => m.State == MoteState.Standby && defOf(m.DefId)?.Element == element)
                .ToList();
            if (available.Count < count) return new List<string>();

            var spent = new List<string>();
            for (int i = 0; i < count; i++)
            {
                var mote = available[i];
                mote.State = MoteState.Recovering;
                mote.RecoveryLeft = defOf(mote.DefId)?.RecoveryRounds ?? DefaultRecoveryRounds;
                spent.Add(mote.DefId);
            }
            return spent;
        }

        /// <summary>End-of-round recovery tick. Returns ids that returned to Set.</summary>
        public static List<string> TickRecovery(IEnumerable<MoteInstance> motes)
        {
            var recovered = new List<string>();
            foreach (var mote in motes)
            {
                if (mote.State != MoteState.Recovering) continue;
                mote.RecoveryLeft -= 1;
                if (mote.RecoveryLeft <= 0)
                {
                    mote.State = MoteState.Set;
                    mote.RecoveryLeft = 0;
                    recovered.Add(mote.DefId);
                }
            }
            return recovered;
        }

        /// <summary>Between battles every mote returns to its bearer.</summary>
        public static void RestoreAll(IEnumerable<MoteInstance> motes)
        {
            foreach (var mote in motes)
            {
                mote.State = MoteState.Set;
                mote.RecoveryLeft = 0;
            }
        }
    }
}
