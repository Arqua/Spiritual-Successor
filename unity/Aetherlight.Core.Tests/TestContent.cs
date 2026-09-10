using System.Collections.Generic;
using Aetherlight.Core;
using Aetherlight.Domain;

namespace Aetherlight.Tests
{
    /// <summary>
    /// A minimal content pack for the battle tests. Deliberately separate from
    /// any shipping content so a balance change cannot break these assertions.
    /// </summary>
    public static class TestContent
    {
        public static ContentRegistry Registry()
        {
            var pack = new ContentPack { Id = "test" };

            pack.Statuses.Add(new StatusDef
            {
                Id = "scorch", Name = "Scorch", Element = Element.Pyre,
                Duration = StatusDuration.ForRounds(3),
                DegenPerRound = 0.04,
                Modifier = new StatModifier { Defense = -8 },
            });
            pack.Statuses.Add(new StatusDef
            {
                Id = "bind", Name = "Bind", Duration = StatusDuration.ForRounds(2),
                PreventsAction = true, BreaksOnDamage = true, ExclusiveGroup = "control",
            });

            pack.Arts.Add(new ArtDef
            {
                Id = "strike", Name = "Strike", Element = Element.Terra,
                Kind = ArtKind.Physical, Targeting = ArtTargeting.OneFoe, Cost = 4,
                Effect = new ArtEffect { Power = 1.4 },
            });
            pack.Arts.Add(new ArtDef
            {
                Id = "ember", Name = "Ember", Element = Element.Pyre,
                Kind = ArtKind.Aetheric, Targeting = ArtTargeting.OneFoe, Cost = 5,
                Effect = new ArtEffect { Power = 34, Statuses = { new ArtStatusChance { StatusId = "scorch", Chance = 1.0 } } },
            });
            pack.Arts.Add(new ArtDef
            {
                Id = "mend", Name = "Mend", Element = Element.Rime,
                Kind = ArtKind.Heal, Targeting = ArtTargeting.OneAlly, Cost = 6,
                Effect = new ArtEffect { Power = 40 },
            });

            pack.Motes.Add(new MoteDef
            {
                Id = "boulder", Name = "Boulder", Element = Element.Terra,
                Bonus = new StatModifier { MaxHp = 12, Defense = 4 },
                Effect = new MoteEffect { Kind = MoteEffectKind.Damage, Power = 30, Targeting = MoteTargeting.OneFoe },
            });
            pack.Motes.Add(new MoteDef
            {
                Id = "cinder", Name = "Cinder", Element = Element.Pyre,
                Bonus = new StatModifier { Attack = 6 },
                Effect = new MoteEffect { Kind = MoteEffectKind.Damage, Power = 38, Targeting = MoteTargeting.OneFoe },
            });

            pack.Classes.Add(new ClassDef { Id = "wanderer", Name = "Wanderer", Priority = 0, Requires = new ClassRequirement { MinTotal = 0 } });

            var pureTerra = new ClassRequirement();
            pureTerra.MinMotes[Element.Terra] = 1;
            pureTerra.MaxMotes[Element.Pyre] = 0;
            pack.Classes.Add(new ClassDef
            {
                Id = "geomancer", Name = "Geomancer", Priority = 10,
                Innate = { Element.Terra }, Requires = pureTerra,
                StatMultipliers = new ClassMultipliers { MaxHp = 1.15, Defense = 1.2 },
                Arts = { new ClassArtGrant { ArtId = "strike", Level = 1 } },
            });

            var dual = new ClassRequirement();
            dual.MinMotes[Element.Terra] = 1;
            dual.MinMotes[Element.Pyre] = 1;
            pack.Classes.Add(new ClassDef
            {
                Id = "ashwarden", Name = "Ashwarden", Priority = 20,
                Innate = { Element.Terra }, Requires = dual,
                StatMultipliers = new ClassMultipliers { MaxHp = 1.2, Attack = 1.25 },
                Arts = { new ClassArtGrant { ArtId = "ember", Level = 1 } },
            });

            pack.Actors.Add(new ActorDef
            {
                Id = "rell", Name = "Rell", Innate = Element.Terra,
                Growth = Growth(58, 22, 16, 14, 11, 6),
                BaseArts = { "strike" },
            });
            pack.Actors.Add(new ActorDef
            {
                Id = "maren", Name = "Maren", Innate = Element.Rime,
                Growth = Growth(46, 34, 12, 11, 12, 7),
                BaseArts = { "mend" },
            });

            pack.Gear.Add(new GearDef { Id = "blade", Name = "Blade", Slot = EquipSlot.Weapon, Modifier = new StatModifier { Attack = 14 } });

            var summonCost = ElementTable<int>.Filled(0);
            summonCost[Element.Terra] = 2;
            pack.Summons.Add(new SummonDef
            {
                Id = "warden", Name = "Warden", Element = Element.Terra,
                Cost = summonCost, BasePower = 90, HpFractionPerMote = 0.04, HpFractionCap = 0.2,
            });

            pack.Enemies.Add(new EnemyDef
            {
                Id = "crawler", Name = "Crawler", Innate = Element.Terra,
                Stats = new StatBlock
                {
                    MaxHp = 70, MaxAether = 0, Attack = 22, Defense = 12, Agility = 10, Luck = 3,
                    Power = ElementTable<double>.Filled(0),
                    Resist = ElementTable<double>.Filled(0),
                }.Normalized(),
                Xp = 24, Coin = 18,
                Moves = { new EnemyMove { ArtId = "attack", Weight = 8 } },
            });

            return ContentRegistry.From(pack);
        }

        private static GrowthProfile Growth(double hp, double aether, double atk, double def, double agi, double luck) => new GrowthProfile
        {
            MaxHp = new GrowthCurve(hp, hp * 0.11, 1.05),
            MaxAether = new GrowthCurve(aether, aether * 0.12, 1.02),
            Attack = new GrowthCurve(atk, atk * 0.09),
            Defense = new GrowthCurve(def, def * 0.08),
            Agility = new GrowthCurve(agi, agi * 0.07),
            Luck = new GrowthCurve(luck, 0.4),
        };
    }
}
