using System.Collections.Generic;
using Aetherlight.Domain;

namespace Aetherlight.Core
{
    /// <summary>
    /// All game content is data, loaded from one or more packs. Packs merge in
    /// load order: a later pack overrides an earlier entry with the same id,
    /// which is what makes a balance patch or a mod pack possible without
    /// editing the base content.
    /// </summary>
    public sealed class ContentPack
    {
        public string Id = "";
        public string? Version;
        public List<ActorDef> Actors = new List<ActorDef>();
        public List<MoteDef> Motes = new List<MoteDef>();
        public List<ClassDef> Classes = new List<ClassDef>();
        public List<ArtDef> Arts = new List<ArtDef>();
        public List<GearDef> Gear = new List<GearDef>();
        public List<SummonDef> Summons = new List<SummonDef>();
        public List<StatusDef> Statuses = new List<StatusDef>();
        public List<EnemyDef> Enemies = new List<EnemyDef>();
    }

    public sealed class ValidationIssue
    {
        public bool IsError;
        public string Entity = "";
        public string Id = "";
        public string Message = "";

        public override string ToString() => $"{(IsError ? "error" : "warning")} {Entity} {Id}: {Message}";
    }

    public sealed class ContentRegistry : IActorContext
    {
        public readonly Dictionary<string, ActorDef> Actors = new Dictionary<string, ActorDef>();
        public readonly Dictionary<string, MoteDef> Motes = new Dictionary<string, MoteDef>();
        public readonly Dictionary<string, ClassDef> Classes = new Dictionary<string, ClassDef>();
        public readonly Dictionary<string, ArtDef> Arts = new Dictionary<string, ArtDef>();
        public readonly Dictionary<string, GearDef> Gear = new Dictionary<string, GearDef>();
        public readonly Dictionary<string, SummonDef> Summons = new Dictionary<string, SummonDef>();
        public readonly Dictionary<string, StatusDef> Statuses = new Dictionary<string, StatusDef>();
        public readonly Dictionary<string, EnemyDef> Enemies = new Dictionary<string, EnemyDef>();

        private readonly List<string> _packs = new List<string>();
        private List<ClassDef> _classList = new List<ClassDef>();

        public IReadOnlyList<string> Packs => _packs;

        public static ContentRegistry From(params ContentPack[] packs)
        {
            var registry = new ContentRegistry();
            foreach (var pack in packs) registry.Load(pack);
            return registry;
        }

        public ContentRegistry Load(ContentPack pack)
        {
            foreach (var entry in pack.Actors) Actors[entry.Id] = entry;
            foreach (var entry in pack.Motes) Motes[entry.Id] = entry;
            foreach (var entry in pack.Classes) Classes[entry.Id] = entry;
            foreach (var entry in pack.Arts) Arts[entry.Id] = entry;
            foreach (var entry in pack.Gear) Gear[entry.Id] = entry;
            foreach (var entry in pack.Summons) Summons[entry.Id] = entry;
            foreach (var entry in pack.Statuses) Statuses[entry.Id] = entry;
            foreach (var entry in pack.Enemies) Enemies[entry.Id] = entry;
            _packs.Add(pack.Id);
            _classList = new List<ClassDef>(Classes.Values);
            return this;
        }

        public ActorDef? ActorDef(string id) => Actors.TryGetValue(id, out var v) ? v : null;
        public MoteDef? MoteDef(string id) => Motes.TryGetValue(id, out var v) ? v : null;
        public ArtDef? ArtDef(string id) => Arts.TryGetValue(id, out var v) ? v : null;
        public GearDef? GearDef(string id) => Gear.TryGetValue(id, out var v) ? v : null;
        public SummonDef? SummonDef(string id) => Summons.TryGetValue(id, out var v) ? v : null;
        public StatusDef? StatusDef(string id) => Statuses.TryGetValue(id, out var v) ? v : null;
        public EnemyDef? EnemyDef(string id) => Enemies.TryGetValue(id, out var v) ? v : null;

        public IReadOnlyList<ClassDef> ClassDefs => _classList;

        /// <summary>
        /// Check every cross-reference. Run this in a content test so a typo in
        /// an art id fails the build instead of a boss fight.
        /// </summary>
        public List<ValidationIssue> Validate()
        {
            var issues = new List<ValidationIssue>();

            void RequireArt(string? id, string entity, string ownerId)
            {
                if (!string.IsNullOrEmpty(id) && !Arts.ContainsKey(id!))
                    issues.Add(new ValidationIssue { IsError = true, Entity = entity, Id = ownerId, Message = $"unknown art \"{id}\"" });
            }
            void RequireStatus(string? id, string entity, string ownerId)
            {
                if (!string.IsNullOrEmpty(id) && !Statuses.ContainsKey(id!))
                    issues.Add(new ValidationIssue { IsError = true, Entity = entity, Id = ownerId, Message = $"unknown status \"{id}\"" });
            }

            foreach (var actor in Actors.Values)
                foreach (var artId in actor.BaseArts) RequireArt(artId, "actor", actor.Id);

            foreach (var cls in Classes.Values)
                foreach (var grant in cls.Arts) RequireArt(grant.ArtId, "class", cls.Id);

            foreach (var art in Arts.Values)
                foreach (var status in art.Effect.Statuses) RequireStatus(status.StatusId, "art", art.Id);

            foreach (var gear in Gear.Values) RequireArt(gear.Proc?.ArtId, "gear", gear.Id);

            foreach (var enemy in Enemies.Values)
            {
                foreach (var artId in enemy.Arts) RequireArt(artId, "enemy", enemy.Id);
                foreach (var drop in enemy.Drops)
                {
                    if (!Gear.ContainsKey(drop.ItemId))
                        issues.Add(new ValidationIssue { IsError = false, Entity = "enemy", Id = enemy.Id, Message = $"drop \"{drop.ItemId}\" is not gear" });
                }
            }

            foreach (var summon in Summons.Values)
            {
                if (Domain.Summons.CostTotal(summon) == 0)
                    issues.Add(new ValidationIssue { IsError = false, Entity = "summon", Id = summon.Id, Message = "summon costs nothing" });
            }

            return issues;
        }

        public void AssertValid()
        {
            var errors = Validate().FindAll(i => i.IsError);
            if (errors.Count == 0) return;
            var detail = string.Join("\n", errors.ConvertAll(e => "  " + e));
            throw new System.InvalidOperationException($"Content validation failed with {errors.Count} error(s):\n{detail}");
        }
    }
}
