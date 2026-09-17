using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Aetherlight.Core;
using Aetherlight.Domain;
using Aetherlight.Battle;

namespace Aetherlight.Tests
{
    /// <summary>
    /// Ported from test/items.test.ts. Uses the canonical pack rather than a
    /// hand-built one, so these exercise the same item definitions the
    /// TypeScript suite does.
    /// </summary>
    public class InventoryTests
    {
        private static readonly ContentRegistry Registry =
            ContentRegistry.From(ContentLoader.LoadPack(
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "starter.json"))));

        [Fact]
        public void CountsAcrossDuplicateStacks()
        {
            var bag = new List<InventoryEntry> { new("salve", 3), new("salve", 2) };
            Assert.Equal(5, Inventories.CountOf(bag, "salve"));
            Assert.Equal(0, Inventories.CountOf(bag, "nothing"));
        }

        [Fact]
        public void MergesIntoAnExistingStackWhenAdding()
        {
            var bag = new List<InventoryEntry> { new("salve", 2) };
            var result = Inventories.Add(bag, "salve", 3, Registry.ItemDef);
            Assert.Equal(3, result.Added);
            Assert.Equal(0, result.Overflow);
            Assert.Single(bag);
            Assert.Equal(5, bag[0].Count);
        }

        [Fact]
        public void ReportsWhatDidNotFitRatherThanDiscardingIt()
        {
            // Quietly eating a reward is worse than a full bag: the player
            // cannot tell "picked up" from "lost".
            var bag = new List<InventoryEntry> { new("greater-salve", 19) };
            var result = Inventories.Add(bag, "greater-salve", 5, Registry.ItemDef);
            Assert.Equal(1, result.Added);
            Assert.Equal(4, result.Overflow);
            Assert.Equal(20, Inventories.CountOf(bag, "greater-salve"));
        }

        [Fact]
        public void TreatsAnAbsentStackLimitAsUnlimited()
        {
            var bag = new List<InventoryEntry>();
            Assert.Equal(0, Inventories.Add(bag, "sealed-writ", 5000, Registry.ItemDef).Overflow);
        }

        [Fact]
        public void RefusesARemovalItCannotCoverChangingNothing()
        {
            var bag = new List<InventoryEntry> { new("salve", 2) };
            Assert.False(Inventories.Remove(bag, "salve", 3));
            Assert.Equal(2, Inventories.CountOf(bag, "salve"));
        }

        [Fact]
        public void DropsAStackOnceEmptied()
        {
            var bag = new List<InventoryEntry> { new("salve", 2) };
            Assert.True(Inventories.Remove(bag, "salve", 2));
            Assert.Empty(bag);
        }

        [Fact]
        public void DrawsAcrossSeveralStacksOfTheSameItem()
        {
            var bag = new List<InventoryEntry> { new("salve", 2), new("salve", 2) };
            Assert.True(Inventories.Remove(bag, "salve", 3));
            Assert.Equal(1, Inventories.CountOf(bag, "salve"));
        }

        [Fact]
        public void NormalizesAwayEmptyStacksAndDuplicates()
        {
            var bag = new List<InventoryEntry> { new("salve", 2), new("salve", 3), new("antidote", 0) };
            var normalized = Inventories.Normalize(bag);
            Assert.Single(normalized);
            Assert.Equal("salve", normalized[0].ItemId);
            Assert.Equal(5, normalized[0].Count);
        }

        [Fact]
        public void GatesUsabilityByContext()
        {
            Assert.True(Inventories.IsUsable(Registry.ItemDef("firebomb"), inBattle: true));
            Assert.False(Inventories.IsUsable(Registry.ItemDef("firebomb"), inBattle: false));
            Assert.True(Inventories.IsUsable(Registry.ItemDef("salve"), inBattle: false));
            // A key item names no art, so there is nothing to apply.
            Assert.False(Inventories.IsUsable(Registry.ItemDef("sealed-writ"), inBattle: false));
            Assert.False(Inventories.IsUsable(null, inBattle: true));
        }
    }

    public class ItemsInBattleTests
    {
        private static readonly ContentRegistry Registry =
            ContentRegistry.From(ContentLoader.LoadPack(
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "starter.json"))));

        private static List<ActorState> Party()
        {
            var rell = Actors.Create(Registry.ActorDef("rell")!, 12, Registry);
            var maren = Actors.Create(Registry.ActorDef("maren")!, 12, Registry);
            maren.Id = "maren";
            Actors.Restore(rell, Registry);
            Actors.Restore(maren, Registry);
            return new List<ActorState> { rell, maren };
        }

        private static BattleState Battle(List<InventoryEntry> bag, string seed = "items") =>
            BattleFactory.Create(new BattleSetup
            {
                Party = Party(),
                Encounter = new EncounterDef
                {
                    Id = "test",
                    Members = { new EncounterMember { EnemyId = "thicket-crawler", Slot = 0 } },
                },
                Seed = seed,
                Inventory = bag,
            }, Registry);

        private static BattleCommand UseItem(string user, string itemId, string? target = null) =>
            new BattleCommand { Kind = CommandKind.Item, CombatantId = user, ItemId = itemId, TargetId = target };

        [Fact]
        public void HealsAndConsumesTheItem()
        {
            var bag = new List<InventoryEntry> { new("salve", 2) };
            var state = Battle(bag);
            var rell = Battles.Find(state, "party:rell")!;
            rell.Hp = 20;

            var result = Resolver.ResolveRound(state, new[] { UseItem("party:rell", "salve", "party:rell") }, Registry);

            Assert.Single(result.Events.OfType<ItemUsedEvent>());
            Assert.True(rell.Hp > 20);
            Assert.Equal(1, Inventories.CountOf(bag, "salve"));
        }

        [Fact]
        public void ConsumesTheItemEvenWhenTheEffectAchievesNothing()
        {
            // Drinking at full health still empties the bottle.
            var bag = new List<InventoryEntry> { new("salve", 1) };
            var state = Battle(bag);
            Resolver.ResolveRound(state, new[] { UseItem("party:rell", "salve", "party:rell") }, Registry);
            Assert.Equal(0, Inventories.CountOf(bag, "salve"));
        }

        [Fact]
        public void DoesNotScaleOffWhoeverUsedIt()
        {
            // If it scaled off elemental power, handing the healer the potions
            // would be strictly correct, which is not how a potion works.
            double HealedBy(string userId)
            {
                var bag = new List<InventoryEntry> { new("salve", 1) };
                var state = Battle(bag, "scaling");
                Battles.Find(state, "party:rell")!.Hp = 10;

                var result = Resolver.ResolveRound(state, new[] { UseItem(userId, "salve", "party:rell") }, Registry);
                return result.Events.OfType<HealEvent>().FirstOrDefault()?.Amount ?? 0;
            }

            Assert.Equal(HealedBy("party:maren"), HealedBy("party:rell"));
        }

        [Fact]
        public void SkipsTheTurnWhenTheBagDoesNotHoldTheItem()
        {
            var state = Battle(new List<InventoryEntry>());
            var result = Resolver.ResolveRound(state, new[] { UseItem("party:rell", "salve", "party:rell") }, Registry);

            Assert.Contains(result.Events.OfType<TurnSkippedEvent>(), e => e.Reason == "item-missing");
            Assert.Empty(result.Events.OfType<ItemUsedEvent>());
        }

        [Fact]
        public void RefusesAnItemThatIsNotUsableInBattle()
        {
            var bag = new List<InventoryEntry> { new("sealed-writ", 1) };
            var state = Battle(bag);
            var result = Resolver.ResolveRound(state, new[] { UseItem("party:rell", "sealed-writ") }, Registry);

            Assert.Contains(result.Events.OfType<TurnSkippedEvent>(), e => e.Reason == "item-not-usable");
            // A refused use must not consume the item.
            Assert.Equal(1, Inventories.CountOf(bag, "sealed-writ"));
        }

        [Fact]
        public void ReportsAnUnknownItemRatherThanThrowing()
        {
            var bag = new List<InventoryEntry> { new("ghost", 1) };
            var state = Battle(bag);
            var result = Resolver.ResolveRound(state, new[] { UseItem("party:rell", "ghost") }, Registry);
            Assert.Contains(result.Events.OfType<TurnSkippedEvent>(), e => e.Reason == "unknown-item");
        }

        [Fact]
        public void DamagesFoesWithAnOffensiveItem()
        {
            var bag = new List<InventoryEntry> { new("firebomb", 1) };
            var state = Battle(bag, "bomb");
            var foe = Battles.Living(state, Side.Foe)[0];
            double before = foe.Hp;

            Resolver.ResolveRound(state, new[] { UseItem("party:rell", "firebomb", foe.Id) }, Registry);
            Assert.True(foe.Hp < before);
        }

        [Fact]
        public void RestoresAetherWithATonic()
        {
            var bag = new List<InventoryEntry> { new("aether-tonic", 1) };
            var state = Battle(bag, "tonic");
            var maren = Battles.Find(state, "party:maren")!;
            maren.Aether = 0;

            Resolver.ResolveRound(state, new[] { UseItem("party:rell", "aether-tonic", maren.Id) }, Registry);
            Assert.True(maren.Aether > 0);
        }

        [Fact]
        public void ConsumptionSurvivesTheFight()
        {
            // The bag is held by reference, not copied, so an item spent in a
            // battle the party then runs from is still gone.
            var bag = new List<InventoryEntry> { new("salve", 1) };
            var state = Battle(bag, "flee");
            Battles.Find(state, "party:rell")!.Hp = 5;

            Resolver.ResolveRound(state, new[] { UseItem("party:rell", "salve", "party:rell") }, Registry);
            Assert.False(Inventories.Has(bag, "salve"));
        }
    }

    public class ItemContentValidationTests
    {
        [Fact]
        public void CatchesAnItemNamingAnArtThatDoesNotExist()
        {
            var pack = new ContentPack { Id = "broken" };
            pack.Items.Add(new ItemDef { Id = "bad", Name = "Bad", Kind = ItemKind.Consumable, ArtId = "ghost-art" });
            Assert.Contains(ContentRegistry.From(pack).Validate(), i => i.Message.Contains("ghost-art"));
        }

        [Fact]
        public void WarnsAboutAConsumableThatDoesNothing()
        {
            var pack = new ContentPack { Id = "broken" };
            pack.Items.Add(new ItemDef { Id = "inert", Name = "Inert", Kind = ItemKind.Consumable });
            Assert.Contains(ContentRegistry.From(pack).Validate(), i => !i.IsError && i.Message.Contains("does nothing"));
        }
    }
}
