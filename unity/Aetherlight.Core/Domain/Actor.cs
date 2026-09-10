using System;
using System.Collections.Generic;

namespace Aetherlight.Domain
{
    public sealed class ActorDef
    {
        public string Id = "";
        public string Name = "";
        public Element Innate;
        public GrowthProfile Growth = new GrowthProfile();
        /// <summary>Arts known regardless of class.</summary>
        public List<string> BaseArts = new List<string>();
        public string? PortraitId;
        public string? SpriteId;
    }

    public sealed class TimedModifier
    {
        public StatModifier Modifier = new StatModifier();
        public int Rounds;
    }

    /// <summary>
    /// Plain data: level, current HP, which motes are bound and in what state,
    /// what is equipped. Everything else - stats, class, known arts - is derived
    /// on demand. There is no "recalculate" call to forget.
    /// </summary>
    public sealed class ActorState
    {
        /// <summary>Instance id, unique within a save. Distinct from DefId.</summary>
        public string Id = "";
        public string DefId = "";
        public int Level = 1;
        public long Xp;
        public double Hp;
        public double Aether;
        public List<MoteInstance> Motes = new List<MoteInstance>();
        public Loadout Equipment = new Loadout();
        public List<StatusInstance> Statuses = new List<StatusInstance>();
        /// <summary>Battle-only modifiers, cleared when the fight ends.</summary>
        public List<TimedModifier> TempModifiers = new List<TimedModifier>();

        public ActorState Clone()
        {
            var copy = new ActorState
            {
                Id = Id,
                DefId = DefId,
                Level = Level,
                Xp = Xp,
                Hp = Hp,
                Aether = Aether,
                Equipment = Equipment.Clone(),
            };
            foreach (var mote in Motes) copy.Motes.Add(mote.Clone());
            foreach (var status in Statuses) copy.Statuses.Add(status.Clone());
            foreach (var mod in TempModifiers) copy.TempModifiers.Add(new TimedModifier { Modifier = mod.Modifier, Rounds = mod.Rounds });
            return copy;
        }
    }

    /// <summary>Everything the derivation functions need to look up.</summary>
    public interface IActorContext
    {
        ActorDef? ActorDef(string id);
        MoteDef? MoteDef(string id);
        IReadOnlyList<ClassDef> ClassDefs { get; }
        GearDef? GearDef(string id);
        StatusDef? StatusDef(string id);
    }

    public static class Actors
    {
        public static ActorState Create(ActorDef def, int level, IActorContext ctx)
        {
            var state = new ActorState
            {
                Id = def.Id,
                DefId = def.Id,
                Level = Math.Max(1, level),
            };
            var stats = DeriveStats(state, ctx);
            state.Hp = stats.MaxHp;
            state.Aether = stats.MaxAether;
            return state;
        }

        public static ElementTable<int> MoteCounts(ActorState state, IActorContext ctx) =>
            Motes.SetCounts(state.Motes, ctx.MoteDef);

        public static ClassDef? DeriveClass(ActorState state, IActorContext ctx)
        {
            var def = ctx.ActorDef(state.DefId);
            if (def == null) return null;
            return Classes.Resolve(ctx.ClassDefs, def.Innate, MoteCounts(state, ctx));
        }

        /// <summary>
        /// Composed in a fixed order so results are reproducible:
        /// level growth, class multipliers, set-mote bonuses, equipment,
        /// statuses, temporary buffs.
        ///
        /// Multiplication before addition matters: a class multiplier scales
        /// base growth but not the flat bonus from a ring, which keeps
        /// late-game gear from being multiplied into absurdity.
        /// </summary>
        public static StatBlock DeriveStats(ActorState state, IActorContext ctx)
        {
            var def = ctx.ActorDef(state.DefId);
            if (def == null) return new StatBlock { MaxHp = 1, Attack = 1, Agility = 1 }.Normalized();

            var stats = Stats.AtLevel(def.Growth, state.Level);

            var classDef = DeriveClass(state, ctx);
            if (classDef?.StatMultipliers != null) stats = Stats.Scale(stats, classDef.StatMultipliers);

            var mods = new List<StatModifier?>();
            foreach (var mote in state.Motes)
            {
                if (mote.State != MoteState.Set) continue;
                mods.Add(ctx.MoteDef(mote.DefId)?.Bonus);
            }
            mods.AddRange(Equipment.Modifiers(state.Equipment, ctx.GearDef));
            foreach (var status in state.Statuses) mods.Add(ctx.StatusDef(status.StatusId)?.Modifier);
            foreach (var temp in state.TempModifiers) mods.Add(temp.Modifier);

            return Stats.ApplyAll(stats, mods.ToArray()).Normalized();
        }

        /// <summary>Innate arts plus whatever the current class grants.</summary>
        public static List<string> KnownArts(ActorState state, IActorContext ctx)
        {
            var def = ctx.ActorDef(state.DefId);
            var seen = new HashSet<string>();
            var arts = new List<string>();

            foreach (var artId in def?.BaseArts ?? new List<string>())
            {
                if (seen.Add(artId)) arts.Add(artId);
            }
            foreach (var artId in Classes.ArtsAtLevel(DeriveClass(state, ctx), state.Level))
            {
                if (seen.Add(artId)) arts.Add(artId);
            }
            return arts;
        }

        public static bool IsDowned(ActorState state, IActorContext ctx)
        {
            if (state.Hp <= 0) return true;
            foreach (var status in state.Statuses)
            {
                if (ctx.StatusDef(status.StatusId)?.Incapacitates == true) return true;
            }
            return false;
        }

        public static bool HasStatus(ActorState state, string statusId) =>
            state.Statuses.Exists(s => s.StatusId == statusId);

        /// <summary>Clamp vitals after a stat change (class swap, gear change).</summary>
        public static void ClampVitals(ActorState state, IActorContext ctx)
        {
            var stats = DeriveStats(state, ctx);
            state.Hp = Math.Max(0, Math.Min(state.Hp, stats.MaxHp));
            state.Aether = Math.Max(0, Math.Min(state.Aether, stats.MaxAether));
        }

        /// <summary>Full restore, as at an inn.</summary>
        public static void Restore(ActorState state, IActorContext ctx)
        {
            var stats = DeriveStats(state, ctx);
            state.Hp = stats.MaxHp;
            state.Aether = stats.MaxAether;
            state.Statuses.Clear();
            state.TempModifiers.Clear();
        }
    }
}
