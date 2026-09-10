using System.Collections.Generic;

namespace Aetherlight.Domain
{
    /// <summary>How a status ends.</summary>
    public enum StatusDurationKind
    {
        /// <summary>Ticks down at the end of each round.</summary>
        Rounds,
        /// <summary>Lasts until the battle ends.</summary>
        Battle,
        /// <summary>Persists on the field until cured.</summary>
        Persistent,
    }

    public sealed class StatusDuration
    {
        public StatusDurationKind Kind = StatusDurationKind.Battle;
        public int Rounds;

        public static StatusDuration ForRounds(int rounds) =>
            new StatusDuration { Kind = StatusDurationKind.Rounds, Rounds = rounds };

        public static readonly StatusDuration WholeBattle = new StatusDuration { Kind = StatusDurationKind.Battle };
        public static readonly StatusDuration Persistent = new StatusDuration { Kind = StatusDurationKind.Persistent };
    }

    public sealed class StatusDef
    {
        public string Id = "";
        public string Name = "";
        public string? Description;
        public Element? Element;
        public StatusDuration Duration = StatusDuration.WholeBattle;

        /// <summary>Only one status per group can be active; a new one replaces the old.</summary>
        public string? ExclusiveGroup;

        public StatModifier? Modifier;

        /// <summary>Damage at end of round, as a fraction of max HP.</summary>
        public double DegenPerRound;

        /// <summary>Healing at end of round, as a fraction of max HP.</summary>
        public double RegenPerRound;

        /// <summary>Blocks the actor's turn entirely.</summary>
        public bool PreventsAction;

        /// <summary>Blocks aether-costing arts but allows physical attacks.</summary>
        public bool PreventsArts;

        /// <summary>Base probability the status is resisted outright, before luck.</summary>
        public double BaseResistChance;

        /// <summary>Cleared when the bearer takes damage.</summary>
        public bool BreaksOnDamage;

        /// <summary>Marks the bearer as out of the fight.</summary>
        public bool Incapacitates;
    }

    public sealed class StatusInstance
    {
        public string StatusId = "";

        /// <summary>
        /// Remaining rounds. Infinity for battle-long and persistent statuses,
        /// which is what keeps the tick loop from having to special-case them.
        /// </summary>
        public double Remaining = double.PositiveInfinity;

        public string? SourceId;

        public StatusInstance Clone() => new StatusInstance
        {
            StatusId = StatusId,
            Remaining = Remaining,
            SourceId = SourceId,
        };
    }

    public static class Statuses
    {
        public static StatusInstance Make(StatusDef def, string? sourceId = null) => new StatusInstance
        {
            StatusId = def.Id,
            Remaining = def.Duration.Kind == StatusDurationKind.Rounds
                ? def.Duration.Rounds
                : double.PositiveInfinity,
            SourceId = sourceId,
        };

        /// <summary>Tick finite durations down. Returns the ids that expired.</summary>
        public static List<string> Tick(IList<StatusInstance> instances)
        {
            var expired = new List<string>();
            var remaining = new List<StatusInstance>();
            foreach (var instance in instances)
            {
                if (double.IsInfinity(instance.Remaining))
                {
                    remaining.Add(instance);
                    continue;
                }
                double next = instance.Remaining - 1;
                if (next <= 0) expired.Add(instance.StatusId);
                else
                {
                    instance.Remaining = next;
                    remaining.Add(instance);
                }
            }
            instances.Clear();
            foreach (var instance in remaining) instances.Add(instance);
            return expired;
        }

        /// <summary>
        /// Landing chance, tempered by the target's luck. Luck is a soft counter
        /// rather than an immunity: each point shaves a slice off, to a floor.
        /// </summary>
        public static double LandChance(double baseChance, StatusDef? def, double targetLuck, double luckDivisor = 400)
        {
            double resisted = def?.BaseResistChance ?? 0;
            double luckFactor = 1 - System.Math.Min(0.75, System.Math.Max(0, targetLuck) / luckDivisor);
            double chance = baseChance * (1 - resisted) * luckFactor;
            return System.Math.Min(1, System.Math.Max(0, chance));
        }
    }
}
