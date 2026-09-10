using System;
using System.Collections.Generic;
using Aetherlight.Core;
using Aetherlight.Domain;

namespace Aetherlight.Battle
{
    public sealed class BattleSetup
    {
        public List<ActorState> Party = new List<ActorState>();
        public EncounterDef Encounter = new EncounterDef();
        public string Seed = "";
    }

    public sealed class BattleAftermath
    {
        public BattleOutcome? Outcome;
        public long Xp;
        public long Coin;
        public List<string> ItemIds = new List<string>();
        public List<(string ActorId, int From, int To)> LevelUps = new List<(string, int, int)>();
    }

    public static class BattleFactory
    {
        public static BattleState Create(BattleSetup setup, ContentRegistry registry)
        {
            var state = new BattleState
            {
                Rng = Rng.Seed(setup.Seed),
                NoFlee = setup.Encounter.NoFlee,
            };

            for (int index = 0; index < setup.Party.Count; index++)
            {
                var actor = setup.Party[index];
                var stats = Actors.DeriveStats(actor, registry);
                var def = registry.ActorDef(actor.DefId);

                var combatant = new Combatant
                {
                    Id = $"party:{actor.Id}",
                    Side = Side.Party,
                    Slot = index,
                    Actor = actor,
                    Name = def?.Name ?? actor.DefId,
                    Innate = def?.Innate ?? Element.Terra,
                    Hp = Math.Min(actor.Hp, stats.MaxHp),
                    MaxHp = stats.MaxHp,
                    Aether = Math.Min(actor.Aether, stats.MaxAether),
                    MaxAether = stats.MaxAether,
                    Downed = Actors.IsDowned(actor, registry),
                };
                foreach (var status in actor.Statuses) combatant.Statuses.Add(status.Clone());
                state.Combatants.Add(combatant);
            }

            for (int index = 0; index < setup.Encounter.Members.Count; index++)
            {
                var member = setup.Encounter.Members[index];
                var def = registry.EnemyDef(member.EnemyId);
                if (def == null) continue;

                state.Combatants.Add(new Combatant
                {
                    Id = $"foe:{member.EnemyId}:{index}",
                    Side = Side.Foe,
                    Slot = member.Slot,
                    EnemyId = member.EnemyId,
                    Name = def.Name,
                    Innate = def.Innate,
                    Hp = def.Stats.MaxHp,
                    MaxHp = def.Stats.MaxHp,
                    Aether = def.Stats.MaxAether,
                    MaxAether = def.Stats.MaxAether,
                    EnemyStats = def.Stats,
                });
            }

            return state;
        }

        /// <summary>
        /// Write results back onto the party: HP/aether carry over, battle-scoped
        /// statuses clear, motes return, and XP is split among survivors.
        /// </summary>
        public static BattleAftermath Conclude(BattleState state, List<ActorState> party, ContentRegistry registry)
        {
            var aftermath = new BattleAftermath
            {
                Outcome = state.Outcome,
                Xp = state.Rewards.Xp,
                Coin = state.Rewards.Coin,
                ItemIds = state.Rewards.ItemIds,
            };

            int survivors = party.FindAll(a => a.Hp > 0).Count;
            long share = survivors > 0 ? state.Rewards.Xp / survivors : 0;

            foreach (var actor in party)
            {
                var combatant = state.Combatants.Find(c => c.Actor != null && c.Actor.Id == actor.Id);
                if (combatant != null)
                {
                    actor.Hp = combatant.Hp;
                    actor.Aether = combatant.Aether;
                }

                // Battle-scoped statuses and buffs do not persist onto the field.
                actor.Statuses.RemoveAll(s => registry.StatusDef(s.StatusId)?.Duration.Kind != StatusDurationKind.Persistent);
                actor.TempModifiers.Clear();
                Motes.RestoreAll(actor.Motes);

                if (state.Outcome == BattleOutcome.Victory && actor.Hp > 0 && share > 0)
                {
                    int before = actor.Level;
                    actor.Xp += share;
                    int after = Stats.LevelForXp(actor.Xp);
                    if (after != before)
                    {
                        actor.Level = after;
                        aftermath.LevelUps.Add((actor.Id, before, after));
                    }
                }
            }

            return aftermath;
        }
    }
}
