using System;
using System.Collections.Generic;
using Aetherlight.Core;
using Aetherlight.Domain;

namespace Aetherlight.Battle
{
    public enum Side { Party, Foe }

    public enum BattlePhase { Command, Resolving, Ended }

    public sealed class Combatant
    {
        /// <summary>Unique within the battle.</summary>
        public string Id = "";
        public Side Side;
        /// <summary>Formation slot; adjacency drives spread damage falloff.</summary>
        public int Slot;

        /// <summary>Party members carry a full actor state; enemies carry a stat block.</summary>
        public ActorState? Actor;
        public string? EnemyId;

        public string Name = "";
        public Element Innate;
        public double Hp;
        public double MaxHp;
        public double Aether;
        public double MaxAether;

        /// <summary>Enemy-only cached stats; party stats derive from the actor each read.</summary>
        public StatBlock? EnemyStats;

        public List<StatusInstance> Statuses = new List<StatusInstance>();
        public bool Defending;
        public bool Downed;

        /// <summary>Rounds until each move may be used again, keyed by art id.</summary>
        public Dictionary<string, int> Cooldowns = new Dictionary<string, int>();
        public List<TimedModifier> TempModifiers = new List<TimedModifier>();
    }

    public sealed class BattleRewards
    {
        public long Xp;
        public long Coin;
        public List<string> ItemIds = new List<string>();
    }

    public sealed class BattleState
    {
        public int Round = 1;
        public BattlePhase Phase = BattlePhase.Command;
        public RngState Rng;
        public List<Combatant> Combatants = new List<Combatant>();
        public BattleOutcome? Outcome;
        public BattleRewards Rewards = new BattleRewards();
        /// <summary>Set when the encounter forbids fleeing.</summary>
        public bool NoFlee;
    }

    public enum CommandKind { Attack, Art, Unleash, Summon, Defend, Item, Flee }

    public sealed class BattleCommand
    {
        public CommandKind Kind;
        public string CombatantId = "";
        public string? TargetId;
        public string? ArtId;
        public string? MoteId;
        public string? SummonId;
        public string? ItemId;

        public static BattleCommand Attack(string combatantId, string targetId) =>
            new BattleCommand { Kind = CommandKind.Attack, CombatantId = combatantId, TargetId = targetId };

        public static BattleCommand Art(string combatantId, string artId, string? targetId = null) =>
            new BattleCommand { Kind = CommandKind.Art, CombatantId = combatantId, ArtId = artId, TargetId = targetId };

        public static BattleCommand Unleash(string combatantId, string moteId, string? targetId = null) =>
            new BattleCommand { Kind = CommandKind.Unleash, CombatantId = combatantId, MoteId = moteId, TargetId = targetId };

        public static BattleCommand Summon(string combatantId, string summonId) =>
            new BattleCommand { Kind = CommandKind.Summon, CombatantId = combatantId, SummonId = summonId };

        public static BattleCommand Defend(string combatantId) =>
            new BattleCommand { Kind = CommandKind.Defend, CombatantId = combatantId };

        public static BattleCommand Flee(string combatantId) =>
            new BattleCommand { Kind = CommandKind.Flee, CombatantId = combatantId };
    }

    public static class Battles
    {
        public static List<Combatant> Living(BattleState state, Side side)
        {
            var living = new List<Combatant>();
            foreach (var combatant in state.Combatants)
            {
                if (combatant.Side == side && !combatant.Downed) living.Add(combatant);
            }
            return living;
        }

        public static Combatant? Find(BattleState state, string id) =>
            state.Combatants.Find(c => c.Id == id);

        public static Side Opposing(Side side) => side == Side.Party ? Side.Foe : Side.Party;

        /// <summary>Slot distance, used for spread damage falloff.</summary>
        public static int SlotDistance(Combatant a, Combatant b) => Math.Abs(a.Slot - b.Slot);

        public static bool IsOver(BattleState state) =>
            Living(state, Side.Party).Count == 0 || Living(state, Side.Foe).Count == 0;
    }
}
