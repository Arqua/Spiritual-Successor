using System.Collections.Generic;

namespace Aetherlight.Domain
{
    /// <summary>
    /// Enemies carry a flat stat block rather than a growth curve - encounter
    /// design is easier when a slime's numbers are written down instead of
    /// derived.
    /// </summary>
    public sealed class EnemyMove
    {
        /// <summary>Art id, or the sentinel "attack" for a basic strike.</summary>
        public string ArtId = "";
        public double Weight;

        /// <summary>Only considered at or below this HP fraction.</summary>
        public double? HpBelow;

        /// <summary>Only considered from this round onward.</summary>
        public int? FromRound;

        /// <summary>Not usable again for this many rounds.</summary>
        public int Cooldown;
    }

    public sealed class EnemyDrop
    {
        public string ItemId = "";
        public double Chance;
    }

    public sealed class EnemyDef
    {
        public string Id = "";
        public string Name = "";
        public Element Innate;
        public StatBlock Stats = new StatBlock();
        public List<string> Arts = new List<string>();
        public List<EnemyMove> Moves = new List<EnemyMove>();
        public long Xp;
        public long Coin;
        public List<EnemyDrop> Drops = new List<EnemyDrop>();
        public string? SpriteId;

        /// <summary>Hook for project-specific boss scripting.</summary>
        public string? BehaviorTag;

        /// <summary>Resistance to being fled from, 0..1.</summary>
        public double FleeResistance;
    }

    public sealed class EncounterMember
    {
        public string EnemyId = "";
        /// <summary>Formation slot; adjacency drives spread damage.</summary>
        public int Slot;
    }

    public sealed class EncounterDef
    {
        public string Id = "";
        public List<EncounterMember> Members = new List<EncounterMember>();
        public bool NoFlee;
        public string? MusicId;
        public string? BackgroundId;
    }
}
