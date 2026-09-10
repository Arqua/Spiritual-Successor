using System.Collections.Generic;

namespace Aetherlight.Domain
{
    public enum ArtTargeting
    {
        OneFoe,
        AllFoes,
        /// <summary>Hits the primary target fully and its neighbours for less.</summary>
        SpreadFoes,
        Self,
        OneAlly,
        AllAllies,
        DownedAlly,
    }

    public enum ArtKind
    {
        /// <summary>Scales off attack and is mitigated by defense.</summary>
        Physical,
        /// <summary>Scales off elemental power; only lightly reduced by defense.</summary>
        Aetheric,
        Heal,
        Support,
        Field,
    }

    public sealed class ArtStatusChance
    {
        public string StatusId = "";
        public double Chance;
    }

    public sealed class ArtBuff
    {
        public StatModifier Modifier = new StatModifier();
        public int Rounds;
    }

    public sealed class ArtEffect
    {
        /// <summary>Base power. Interpreted per ArtKind; see Damage.</summary>
        public double Power;

        /// <summary>Multiple strikes, each rolled separately.</summary>
        public int Hits = 1;

        /// <summary>Falloff per step away from the primary target for SpreadFoes.</summary>
        public double SpreadFalloff = 0.4;

        public List<ArtStatusChance> Statuses = new List<ArtStatusChance>();
        public ArtBuff? Buff;

        /// <summary>Fraction of damage dealt returned to the user as HP.</summary>
        public double Drain;

        /// <summary>Fraction of max HP restored, added on top of Power.</summary>
        public double HealFraction;

        /// <summary>Revives a downed ally at this fraction of max HP.</summary>
        public double ReviveFraction;

        /// <summary>Aether restored to the target.</summary>
        public double RestoreAether;
    }

    /// <summary>
    /// One definition serves both battle and the overworld. An art may carry a
    /// battle effect, a field effect, or both - which is what makes the
    /// exploration kit and the combat kit feel like one toolkit.
    /// </summary>
    public sealed class ArtDef
    {
        public string Id = "";
        public string Name = "";
        public string? Description;
        public Element Element;
        public ArtKind Kind;
        public ArtTargeting Targeting;
        public double Cost;
        public ArtEffect Effect = new ArtEffect();

        /// <summary>Higher acts earlier regardless of agility.</summary>
        public int Priority;

        /// <summary>Base hit chance before evasion.</summary>
        public double Accuracy = 1;

        public string? FieldEffect;
        public bool UsableOutOfBattle;
        public bool FieldOnly;
        public List<string> Tags = new List<string>();
    }

    public static class Arts
    {
        public static bool IsOffensive(ArtDef art) =>
            art.Targeting == ArtTargeting.OneFoe
            || art.Targeting == ArtTargeting.AllFoes
            || art.Targeting == ArtTargeting.SpreadFoes;

        public static bool TargetsAllies(ArtDef art) =>
            art.Targeting == ArtTargeting.OneAlly
            || art.Targeting == ArtTargeting.AllAllies
            || art.Targeting == ArtTargeting.DownedAlly
            || art.Targeting == ArtTargeting.Self;

        /// <summary>Spread damage falls off with distance from the primary target.</summary>
        public static double SpreadMultiplier(ArtDef art, int distance)
        {
            if (distance <= 0) return 1;
            return System.Math.Max(0, System.Math.Pow(1 - art.Effect.SpreadFalloff, distance));
        }
    }
}
