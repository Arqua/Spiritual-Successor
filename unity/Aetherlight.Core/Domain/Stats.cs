using System;

namespace Aetherlight.Domain
{
    /// <summary>
    /// Stat blocks and level growth.
    ///
    /// Derived stats are always recomputed from base + modifiers rather than
    /// stored, which keeps equipment and class swaps from drifting.
    /// </summary>
    public sealed class StatBlock
    {
        public double MaxHp;
        public double MaxAether;
        public double Attack;
        public double Defense;
        public double Agility;
        public double Luck;
        public ElementTable<double> Power = ElementTable<double>.Filled(0);
        public ElementTable<double> Resist = ElementTable<double>.Filled(0);

        public StatBlock Clone() => new StatBlock
        {
            MaxHp = MaxHp,
            MaxAether = MaxAether,
            Attack = Attack,
            Defense = Defense,
            Agility = Agility,
            Luck = Luck,
            Power = Power.Copy(),
            Resist = Resist.Copy(),
        };

        /// <summary>Clamp every stat to a sane floor, so modifiers cannot go negative.</summary>
        public StatBlock Normalized()
        {
            var outBlock = Clone();
            outBlock.MaxHp = Math.Max(1, Math.Round(outBlock.MaxHp, MidpointRounding.AwayFromZero));
            outBlock.MaxAether = Math.Max(0, Math.Round(outBlock.MaxAether, MidpointRounding.AwayFromZero));
            outBlock.Attack = Math.Max(1, Math.Round(outBlock.Attack, MidpointRounding.AwayFromZero));
            outBlock.Defense = Math.Max(0, Math.Round(outBlock.Defense, MidpointRounding.AwayFromZero));
            outBlock.Agility = Math.Max(1, Math.Round(outBlock.Agility, MidpointRounding.AwayFromZero));
            outBlock.Luck = Math.Max(0, Math.Round(outBlock.Luck, MidpointRounding.AwayFromZero));
            foreach (var element in Elements.All)
            {
                outBlock.Power[element] = Math.Max(0, Math.Round(outBlock.Power[element], MidpointRounding.AwayFromZero));
                outBlock.Resist[element] = Math.Max(0, Math.Round(outBlock.Resist[element], MidpointRounding.AwayFromZero));
            }
            return outBlock;
        }
    }

    /// <summary>A sparse change to a stat block. Null fields are left alone.</summary>
    public sealed class StatModifier
    {
        public double? MaxHp;
        public double? MaxAether;
        public double? Attack;
        public double? Defense;
        public double? Agility;
        public double? Luck;
        public ElementTable<double?> Power = ElementTable<double?>.Filled(null);
        public ElementTable<double?> Resist = ElementTable<double?>.Filled(null);
    }

    public static class Stats
    {
        public static StatBlock Apply(StatBlock baseStats, StatModifier? mod)
        {
            var outBlock = baseStats.Clone();
            if (mod == null) return outBlock;

            if (mod.MaxHp.HasValue) outBlock.MaxHp += mod.MaxHp.Value;
            if (mod.MaxAether.HasValue) outBlock.MaxAether += mod.MaxAether.Value;
            if (mod.Attack.HasValue) outBlock.Attack += mod.Attack.Value;
            if (mod.Defense.HasValue) outBlock.Defense += mod.Defense.Value;
            if (mod.Agility.HasValue) outBlock.Agility += mod.Agility.Value;
            if (mod.Luck.HasValue) outBlock.Luck += mod.Luck.Value;

            foreach (var element in Elements.All)
            {
                var power = mod.Power[element];
                if (power.HasValue) outBlock.Power[element] += power.Value;
                var resist = mod.Resist[element];
                if (resist.HasValue) outBlock.Resist[element] += resist.Value;
            }
            return outBlock;
        }

        public static StatBlock ApplyAll(StatBlock baseStats, params StatModifier?[] mods)
        {
            var acc = baseStats.Clone();
            foreach (var mod in mods) acc = Apply(acc, mod);
            return acc;
        }

        /// <summary>Multiply the scalar stats. Elemental power/resist are untouched.</summary>
        public static StatBlock Scale(StatBlock baseStats, ClassMultipliers multipliers)
        {
            var outBlock = baseStats.Clone();
            outBlock.MaxHp = Math.Round(outBlock.MaxHp * multipliers.MaxHp, MidpointRounding.AwayFromZero);
            outBlock.MaxAether = Math.Round(outBlock.MaxAether * multipliers.MaxAether, MidpointRounding.AwayFromZero);
            outBlock.Attack = Math.Round(outBlock.Attack * multipliers.Attack, MidpointRounding.AwayFromZero);
            outBlock.Defense = Math.Round(outBlock.Defense * multipliers.Defense, MidpointRounding.AwayFromZero);
            outBlock.Agility = Math.Round(outBlock.Agility * multipliers.Agility, MidpointRounding.AwayFromZero);
            outBlock.Luck = Math.Round(outBlock.Luck * multipliers.Luck, MidpointRounding.AwayFromZero);
            return outBlock;
        }

        /// <summary>
        /// value(level) = base + gain * (level - 1) ^ curve
        ///
        /// A curve of 1 is linear; below 1 front-loads growth, above 1 back-loads it.
        /// Four numbers per stat instead of ninety-nine table rows.
        /// </summary>
        public static double CurveAt(GrowthCurve? curve, int level)
        {
            if (curve == null) return 0;
            double steps = Math.Max(0, level - 1);
            return curve.Base + curve.Gain * Math.Pow(steps, curve.Curve);
        }

        public static StatBlock AtLevel(GrowthProfile profile, int level)
        {
            int lvl = Math.Max(1, level);
            var block = new StatBlock
            {
                MaxHp = CurveAt(profile.MaxHp, lvl),
                MaxAether = CurveAt(profile.MaxAether, lvl),
                Attack = CurveAt(profile.Attack, lvl),
                Defense = CurveAt(profile.Defense, lvl),
                Agility = CurveAt(profile.Agility, lvl),
                Luck = CurveAt(profile.Luck, lvl),
            };
            foreach (var element in Elements.All)
            {
                block.Power[element] = CurveAt(profile.Power[element], lvl);
                block.Resist[element] = CurveAt(profile.Resist[element], lvl);
            }
            return block.Normalized();
        }

        public static long XpForLevel(int level, double scale = 30, double exponent = 2.4)
        {
            int lvl = Math.Max(1, level);
            if (lvl == 1) return 0;
            return (long)Math.Round(scale * Math.Pow(lvl - 1, exponent), MidpointRounding.AwayFromZero);
        }

        public static int LevelForXp(long xp, int maxLevel = 99, double scale = 30, double exponent = 2.4)
        {
            int level = 1;
            while (level < maxLevel && xp >= XpForLevel(level + 1, scale, exponent)) level++;
            return level;
        }
    }

    public sealed class GrowthCurve
    {
        public double Base;
        public double Gain;
        public double Curve = 1;

        public GrowthCurve() { }

        public GrowthCurve(double baseValue, double gain, double curve = 1)
        {
            Base = baseValue;
            Gain = gain;
            Curve = curve;
        }
    }

    public sealed class GrowthProfile
    {
        public GrowthCurve MaxHp = new GrowthCurve();
        public GrowthCurve MaxAether = new GrowthCurve();
        public GrowthCurve Attack = new GrowthCurve();
        public GrowthCurve Defense = new GrowthCurve();
        public GrowthCurve Agility = new GrowthCurve();
        public GrowthCurve Luck = new GrowthCurve();
        public ElementTable<GrowthCurve?> Power = ElementTable<GrowthCurve?>.Filled(null);
        public ElementTable<GrowthCurve?> Resist = ElementTable<GrowthCurve?>.Filled(null);
    }

    /// <summary>Class stat multipliers. 1.0 means unchanged.</summary>
    public sealed class ClassMultipliers
    {
        public double MaxHp = 1;
        public double MaxAether = 1;
        public double Attack = 1;
        public double Defense = 1;
        public double Agility = 1;
        public double Luck = 1;
    }
}
