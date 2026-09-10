using System;
using Aetherlight.Domain;

namespace Aetherlight.Battle
{
    /// <summary>
    /// Every number a player sees comes out of this file, so the tuning
    /// constants live in one object rather than scattered through the resolver.
    ///
    /// Mitigation is asymptotic rather than subtractive: defense divides damage
    /// through K / (K + defense) instead of coming off the top. A subtractive
    /// formula has a cliff - once defense exceeds attack, damage collapses to
    /// the minimum and armour becomes flat immunity, which made a low-level
    /// party in basic gear unkillable by accident. The asymptotic form gives
    /// defense diminishing returns without ever reaching a wall.
    /// </summary>
    public sealed class DamageConfig
    {
        public AffinityConfig Affinity = AffinityConfig.Default;

        /// <summary>Random spread applied to every damage roll.</summary>
        public double Variance = 0.08;

        /// <summary>Physical: fraction of raw attack a power-1.0 move delivers.</summary>
        public double PhysicalScale = 0.9;

        /// <summary>Aetheric: fraction of target defense that applies to spell damage.</summary>
        public double AethericDefenseWeight = 0.35;

        /// <summary>
        /// A defense equal to this value halves incoming damage. Raise it to
        /// make defense weaker, lower it to make defense stronger.
        /// </summary>
        public double DefenseSoftening = 100;

        public double BaseCritChance = 0.03;
        public double CritLuckDivisor = 900;
        public double CritMultiplier = 1.6;
        public double DefendMultiplier = 0.5;
        public double MinimumDamage = 1;
        public double AgilityEvasionDivisor = 1200;
        public double MaxEvasion = 0.25;

        public static readonly DamageConfig Default = new DamageConfig();
    }

    public enum DamageKind { Physical, Aetheric, Fixed }

    public sealed class DamageInput
    {
        public StatBlock Attacker = new StatBlock();
        public StatBlock Defender = new StatBlock();
        /// <summary>Element of the strike; null for typeless.</summary>
        public Element? Element;
        /// <summary>The defender's own alignment, for opposed-element bonuses.</summary>
        public Element? DefenderInnate;
        public double Power;
        public DamageKind Kind;
        public bool Defending;
        /// <summary>Extra multiplier from spread falloff, procs, buffs.</summary>
        public double Multiplier = 1;
        public bool CanCrit = true;
    }

    public struct DamageResult
    {
        public double Amount;
        public bool Critical;
        public double Effectiveness;
    }

    public static class Damage
    {
        /// <summary>
        /// Asymptotic mitigation. Approaches zero as defense grows but never
        /// reaches it, so armour is always worth something and never a wall.
        /// </summary>
        public static double Mitigation(double defense, DamageConfig? config = null)
        {
            var cfg = config ?? DamageConfig.Default;
            double effective = Math.Max(0, defense);
            return cfg.DefenseSoftening / (cfg.DefenseSoftening + effective);
        }

        public static DamageResult Roll(ref Core.RngState rng, DamageInput input, DamageConfig? config = null)
        {
            var cfg = config ?? DamageConfig.Default;

            double effectiveness = input.Element == null
                ? 1
                : Elements.AffinityFactor(
                    input.Element.Value,
                    input.Attacker.Power[input.Element.Value],
                    input.Defender.Resist[input.Element.Value],
                    input.DefenderInnate,
                    cfg.Affinity);

            double baseDamage;
            if (input.Kind == DamageKind.Fixed)
            {
                baseDamage = input.Power;
            }
            else if (input.Kind == DamageKind.Physical)
            {
                double raw = Math.Max(0, input.Attacker.Attack) * cfg.PhysicalScale * Math.Max(0, input.Power);
                baseDamage = raw * Mitigation(input.Defender.Defense, cfg);
            }
            else
            {
                double raw = Math.Max(0, input.Power);
                baseDamage = raw * Mitigation(input.Defender.Defense * cfg.AethericDefenseWeight, cfg);
            }

            baseDamage *= effectiveness;
            baseDamage *= input.Multiplier;

            bool critical = input.CanCrit && RollCritical(ref rng, input.Attacker.Luck, cfg);
            if (critical) baseDamage *= cfg.CritMultiplier;
            if (input.Defending) baseDamage *= cfg.DefendMultiplier;

            baseDamage *= Core.Rng.Variance(ref rng, cfg.Variance);

            return new DamageResult
            {
                Amount = Math.Max(cfg.MinimumDamage, Math.Round(baseDamage, MidpointRounding.AwayFromZero)),
                Critical = critical,
                Effectiveness = effectiveness,
            };
        }

        public static bool RollCritical(ref Core.RngState rng, double luck, DamageConfig? config = null)
        {
            var cfg = config ?? DamageConfig.Default;
            double critChance = cfg.BaseCritChance + Math.Max(0, luck) / cfg.CritLuckDivisor;
            return Core.Rng.Chance(ref rng, Math.Min(0.5, critChance));
        }

        /// <summary>
        /// A faster defender dodges more often, but the effect is capped so
        /// agility stacking cannot make a character untouchable.
        /// </summary>
        public static bool RollEvaded(ref Core.RngState rng, StatBlock attacker, StatBlock defender, double accuracy = 1, DamageConfig? config = null)
        {
            var cfg = config ?? DamageConfig.Default;
            double agilityGap = Math.Max(0, defender.Agility - attacker.Agility);
            double evasion = Math.Min(cfg.MaxEvasion, agilityGap / cfg.AgilityEvasionDivisor);
            double hitChance = Math.Max(0, Math.Min(1, accuracy - evasion));
            return Core.Rng.NextDouble(ref rng) >= hitChance;
        }

        /// <summary>Healing shares the variance roll so numbers feel consistent.</summary>
        public static double RollHeal(ref Core.RngState rng, double power, double casterPower, double maxHp, double fraction = 0, DamageConfig? config = null)
        {
            var cfg = config ?? DamageConfig.Default;
            double flat = power * (1 + Math.Max(0, casterPower) / cfg.Affinity.Divisor);
            double fromFraction = maxHp * Math.Max(0, fraction);
            return Math.Max(1, Math.Round((flat + fromFraction) * Core.Rng.Variance(ref rng, cfg.Variance), MidpointRounding.AwayFromZero));
        }
    }
}
