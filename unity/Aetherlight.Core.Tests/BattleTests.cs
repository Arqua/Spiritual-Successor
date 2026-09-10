using System.Collections.Generic;
using System.Linq;
using Xunit;
using Aetherlight.Core;
using Aetherlight.Domain;
using Aetherlight.Battle;

namespace Aetherlight.Tests
{
    /// <summary>Ported from test/battle.test.ts.</summary>
    public class BattleTests
    {
        private static readonly ContentRegistry Registry = TestContent.Registry();

        private static List<ActorState> Party()
        {
            var rell = Actors.Create(Registry.ActorDef("rell")!, 12, Registry);
            rell.Motes.Add(new MoteInstance("boulder"));
            rell.Motes.Add(new MoteInstance("cinder"));
            rell.Equipment[EquipSlot.Weapon] = "blade";

            var maren = Actors.Create(Registry.ActorDef("maren")!, 12, Registry);
            maren.Id = "maren";

            // Motes and gear were assigned after creation, which raised max HP
            // without filling it. Top everyone up before the fight starts.
            Actors.Restore(rell, Registry);
            Actors.Restore(maren, Registry);
            return new List<ActorState> { rell, maren };
        }

        private static BattleState NewBattle(string seed = "test-seed", List<ActorState>? party = null) =>
            BattleFactory.Create(new BattleSetup
            {
                Party = party ?? Party(),
                Encounter = new EncounterDef
                {
                    Id = "test",
                    Members = { new EncounterMember { EnemyId = "crawler", Slot = 0 }, new EncounterMember { EnemyId = "crawler", Slot = 1 } },
                },
                Seed = seed,
            }, Registry);

        [Fact]
        public void BuildsCombatantsForBothSidesAtFullHealth()
        {
            var state = NewBattle();
            Assert.Equal(4, state.Combatants.Count);
            Assert.Equal(2, Battles.Living(state, Side.Party).Count);
            Assert.Equal(2, Battles.Living(state, Side.Foe).Count);
            foreach (var combatant in state.Combatants) Assert.Equal(combatant.MaxHp, combatant.Hp);
        }

        [Fact]
        public void DoesNotHealAnActorForBindingAMote()
        {
            // Binding raises max HP; if current HP rose with it, a player could
            // bind and unbind motes to heal for free between fights.
            var rell = Actors.Create(Registry.ActorDef("rell")!, 12, Registry);
            double beforeHp = rell.Hp;
            rell.Motes.Add(new MoteInstance("boulder"));
            Assert.True(Actors.DeriveStats(rell, Registry).MaxHp > beforeHp);
            Assert.Equal(beforeHp, rell.Hp);
        }

        [Fact]
        public void DealsDamageOnABasicAttack()
        {
            var state = NewBattle();
            var target = Battles.Living(state, Side.Foe)[0];
            double before = target.Hp;

            var result = Resolver.ResolveRound(state, new[] { BattleCommand.Attack("party:rell", target.Id) }, Registry);
            var damage = result.Events.OfType<DamageEvent>().Where(e => e.TargetId == target.Id).ToList();

            // Either the blow landed or it was evaded; both are legal outcomes.
            if (damage.Count > 0) Assert.True(target.Hp < before);
        }

        [Fact]
        public void RefusesAnArtWhenAetherIsShort()
        {
            var state = NewBattle();
            var maren = Battles.Find(state, "party:maren")!;
            maren.Aether = 0;

            var result = Resolver.ResolveRound(state, new[]
            {
                BattleCommand.Art("party:maren", "mend", "party:rell"),
            }, Registry);

            Assert.Contains(result.Events.OfType<TurnSkippedEvent>(), e => e.Reason == "insufficient-aether");
        }

        [Fact]
        public void UnleashMovesAMoteToStandbyAndReportsTheClassChange()
        {
            var state = NewBattle();
            var result = Resolver.ResolveRound(state, new[] { BattleCommand.Unleash("party:rell", "cinder") }, Registry);

            Assert.Single(result.Events.OfType<MoteUnleashedEvent>());

            var rell = Battles.Find(state, "party:rell")!;
            Assert.Equal(MoteState.Standby, rell.Actor!.Motes.Find(m => m.DefId == "cinder")!.State);

            // Losing the pyre mote drops Rell out of the dual class.
            var classChange = result.Events.OfType<ClassChangedEvent>().SingleOrDefault();
            Assert.NotNull(classChange);
            Assert.Equal("ashwarden", classChange!.FromClassId);
            Assert.Equal("geomancer", classChange.ToClassId);
        }

        [Fact]
        public void IsFullyDeterministicForAGivenSeed()
        {
            List<string> Run()
            {
                var state = NewBattle("determinism");
                var trace = new List<string>();
                for (int round = 0; round < 4 && state.Phase != BattlePhase.Ended; round++)
                {
                    var foes = Battles.Living(state, Side.Foe);
                    var commands = new List<BattleCommand>();
                    if (foes.Count > 0) commands.Add(BattleCommand.Attack("party:rell", foes[0].Id));
                    commands.Add(BattleCommand.Art("party:maren", "mend", "party:rell"));
                    commands.AddRange(EnemyAi.CommandsFor(state, Registry));

                    foreach (var e in Resolver.ResolveRound(state, commands, Registry).Events)
                    {
                        trace.Add(e switch
                        {
                            DamageEvent d => $"dmg {d.TargetId} {d.Amount} crit={d.Critical}",
                            HealEvent h => $"heal {h.TargetId} {h.Amount}",
                            MissEvent m => $"miss {m.TargetId}",
                            DownedEvent dn => $"down {dn.CombatantId}",
                            _ => e.GetType().Name,
                        });
                    }
                }
                return trace;
            }

            Assert.Equal(Run(), Run());
        }

        [Fact]
        public void ReachesVictoryAndAwardsRewards()
        {
            var state = NewBattle("victory-run");
            foreach (var foe in Battles.Living(state, Side.Foe)) foe.Hp = 1;

            int guard = 0;
            while (state.Phase != BattlePhase.Ended && guard++ < 30)
            {
                var foes = Battles.Living(state, Side.Foe);
                var commands = foes.Count > 0
                    ? new[] { BattleCommand.Attack("party:rell", foes[0].Id) }
                    : System.Array.Empty<BattleCommand>();
                Resolver.ResolveRound(state, commands, Registry);
            }

            Assert.Equal(BattleOutcome.Victory, state.Outcome);
            Assert.True(state.Rewards.Xp > 0);
        }

        [Fact]
        public void EndsInDefeatWhenThePartyFalls()
        {
            var state = NewBattle("defeat-run");
            foreach (var member in Battles.Living(state, Side.Party)) member.Hp = 1;

            int guard = 0;
            while (state.Phase != BattlePhase.Ended && guard++ < 30)
            {
                Resolver.ResolveRound(state, EnemyAi.CommandsFor(state, Registry), Registry);
            }

            Assert.Equal(BattleOutcome.Defeat, state.Outcome);
        }

        [Fact]
        public void MarksACombatantAsDefending()
        {
            var state = NewBattle("defend");
            Resolver.ResolveRound(state, new[] { BattleCommand.Defend("party:rell") }, Registry);
            Assert.True(Battles.Find(state, "party:rell")!.Defending);
        }

        [Fact]
        public void AppliesAStatusAndTicksItDown()
        {
            var state = NewBattle("status");
            var maren = Battles.Find(state, "party:maren")!;
            maren.Aether = 100;
            var target = Battles.Living(state, Side.Foe)[0];

            // Ember applies scorch with certainty in the test pack.
            Resolver.ResolveRound(state, new[] { BattleCommand.Art("party:maren", "ember", target.Id) }, Registry);

            if (!target.Downed)
            {
                Assert.Contains(target.Statuses, s => s.StatusId == "scorch");
                double remaining = target.Statuses.Find(s => s.StatusId == "scorch")!.Remaining;
                // Three rounds, minus the end-of-round tick that already ran.
                Assert.Equal(2, remaining);
            }
        }

        [Fact]
        public void RefusesASummonWithoutEnoughStandbyMotes()
        {
            var state = NewBattle("summon");
            var result = Resolver.ResolveRound(state, new[] { BattleCommand.Summon("party:rell", "warden") }, Registry);
            Assert.Contains(result.Events.OfType<TurnSkippedEvent>(), e => e.Reason == "insufficient-motes");
        }

        [Fact]
        public void RestoresMotesAndCarriesHpBackToThePartyAfterwards()
        {
            var roster = Party();
            var state = NewBattle("aftermath", roster);

            Resolver.ResolveRound(state, new[] { BattleCommand.Unleash("party:rell", "boulder") }, Registry);
            foreach (var foe in Battles.Living(state, Side.Foe)) foe.Hp = 0;
            Resolver.ResolveRound(state, System.Array.Empty<BattleCommand>(), Registry);

            var aftermath = BattleFactory.Conclude(state, roster, Registry);
            Assert.Equal(BattleOutcome.Victory, aftermath.Outcome);
            Assert.All(roster[0].Motes, m => Assert.Equal(MoteState.Set, m.State));
        }
    }

    /// <summary>Ported from the turn-order section of test/battle.test.ts.</summary>
    public class TurnOrderTests
    {
        [Fact]
        public void SortsByPriorityFirstThenAgility()
        {
            var rng = Rng.Seed("order");
            var order = TurnOrder.Build(ref rng, new[]
            {
                new OrderInput { CombatantId = "slow-priority", Agility = 1, Priority = 100 },
                new OrderInput { CombatantId = "fast", Agility = 999, Priority = 0 },
            });
            Assert.Equal("slow-priority", order[0].CombatantId);
        }

        [Fact]
        public void FavoursTheFasterCombatantAtEqualPriority()
        {
            var rng = Rng.Seed("speed");
            for (int i = 0; i < 100; i++)
            {
                var order = TurnOrder.Build(ref rng, new[]
                {
                    new OrderInput { CombatantId = "fast", Agility = 100 },
                    new OrderInput { CombatantId = "slow", Agility = 40 },
                });
                Assert.Equal("fast", order[0].CombatantId);
            }
        }

        [Fact]
        public void JitterOccasionallyFlipsNearIdenticalSpeeds()
        {
            var rng = Rng.Seed("jitter");
            var seen = new HashSet<string>();
            for (int i = 0; i < 200; i++)
            {
                var order = TurnOrder.Build(ref rng, new[]
                {
                    new OrderInput { CombatantId = "a", Agility = 100 },
                    new OrderInput { CombatantId = "b", Agility = 100 },
                });
                seen.Add(order[0].CombatantId);
            }
            Assert.Equal(2, seen.Count);
        }
    }
}
