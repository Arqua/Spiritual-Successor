using System;
using System.Collections.Generic;

namespace Aetherlight.Battle
{
    /// <summary>
    /// Commands for the whole round are chosen first, then everyone acts in one
    /// ordered pass. Order is agility plus a small jitter, with action priority
    /// tiers on top: a fast healer is usually first but never reliably so, and
    /// a priority move beats raw speed outright.
    ///
    /// Recomputed each round rather than kept as a persistent queue, so a speed
    /// buff applied this round is felt next round.
    /// </summary>
    public sealed class OrderInput
    {
        public string CombatantId = "";
        public double Agility;
        public int Priority;
    }

    public sealed class OrderEntry
    {
        public string CombatantId = "";
        public double Agility;
        public int Priority;
        /// <summary>Resolved sort key, exposed for debugging and tests.</summary>
        public double Score;
    }

    public static class TurnOrder
    {
        /// <summary>Fraction of agility the random jitter can swing.</summary>
        public const double AgilityJitter = 0.15;

        public static List<OrderEntry> Build(ref Core.RngState rng, IReadOnlyList<OrderInput> inputs)
        {
            var entries = new List<OrderEntry>(inputs.Count);
            foreach (var input in inputs)
            {
                double jitter = 1 + (Core.Rng.NextDouble(ref rng) * 2 - 1) * AgilityJitter;
                entries.Add(new OrderEntry
                {
                    CombatantId = input.CombatantId,
                    Agility = input.Agility,
                    Priority = input.Priority,
                    Score = Math.Max(0, input.Agility) * jitter,
                });
            }

            entries.Sort((a, b) =>
            {
                if (b.Priority != a.Priority) return b.Priority.CompareTo(a.Priority);
                if (!b.Score.Equals(a.Score)) return b.Score.CompareTo(a.Score);
                return string.CompareOrdinal(a.CombatantId, b.CombatantId);
            });

            return entries;
        }
    }
}
