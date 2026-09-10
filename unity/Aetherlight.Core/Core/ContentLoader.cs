using System;
using System.Collections.Generic;
using Aetherlight.Domain;

namespace Aetherlight.Core
{
    /// <summary>
    /// Reads a canonical content pack JSON file into a ContentPack.
    ///
    /// The JSON is the interchange format: the TypeScript implementation writes
    /// it (tools/export-content.ts) and both implementations read it, so the
    /// two engines run identical content rather than drifting copies of it.
    /// That is also why every enum here is spelled in the JSON's kebab-case
    /// vocabulary rather than C#'s - the file is the contract, and C# naming
    /// conventions do not get to bend it.
    ///
    /// Unknown fields are ignored and absent optional fields fall back to the
    /// definition's default. Genuine problems - an art referring to a status
    /// that does not exist - are reported by ContentRegistry.Validate() rather
    /// than thrown from here, so one typo does not stop the whole pack loading.
    /// </summary>
    public static class ContentLoader
    {
        public static ContentPack LoadPack(string json) => FromJson(JsonValue.Parse(json));

        public static ContentPack FromJson(JsonValue root)
        {
            var pack = new ContentPack
            {
                Id = root["id"].AsString("unnamed"),
                Version = root["version"].IsNull ? null : root["version"].AsString(),
            };

            foreach (var node in root["statuses"].Items) pack.Statuses.Add(ReadStatus(node));
            foreach (var node in root["arts"].Items) pack.Arts.Add(ReadArt(node));
            foreach (var node in root["motes"].Items) pack.Motes.Add(ReadMote(node));
            foreach (var node in root["classes"].Items) pack.Classes.Add(ReadClass(node));
            foreach (var node in root["actors"].Items) pack.Actors.Add(ReadActor(node));
            foreach (var node in root["gear"].Items) pack.Gear.Add(ReadGear(node));
            foreach (var node in root["summons"].Items) pack.Summons.Add(ReadSummon(node));
            foreach (var node in root["enemies"].Items) pack.Enemies.Add(ReadEnemy(node));

            return pack;
        }

        // --- element helpers -------------------------------------------------

        private static Element ReadElement(JsonValue node, Element fallback = Element.Terra) =>
            Elements.TryParse(node.AsString(), out var element) ? element : fallback;

        private static Element? ReadOptionalElement(JsonValue node) =>
            Elements.TryParse(node.AsString(), out var element) ? element : (Element?)null;

        /// <summary>Reads {"terra": 6, "pyre": 2} into a per-element table.</summary>
        private static ElementTable<double> ReadElementDoubles(JsonValue node)
        {
            var table = ElementTable<double>.Filled(0);
            foreach (var element in Elements.All) table[element] = node[element.ToId()].AsDouble();
            return table;
        }

        private static ElementTable<int> ReadElementInts(JsonValue node)
        {
            var table = ElementTable<int>.Filled(0);
            foreach (var element in Elements.All) table[element] = node[element.ToId()].AsInt();
            return table;
        }

        private static ElementTable<double?> ReadOptionalElementDoubles(JsonValue node)
        {
            var table = ElementTable<double?>.Filled(null);
            foreach (var element in Elements.All) table[element] = node[element.ToId()].AsNullableDouble();
            return table;
        }

        private static ElementTable<int?> ReadOptionalElementInts(JsonValue node)
        {
            var table = ElementTable<int?>.Filled(null);
            foreach (var element in Elements.All) table[element] = node[element.ToId()].AsNullableInt();
            return table;
        }

        // --- shared shapes ---------------------------------------------------

        /// <summary>
        /// A stat modifier distinguishes absent from zero, so every scalar is
        /// read as a nullable. "attack": 0 must mean "add nothing" rather than
        /// "leave attack alone" - they happen to coincide numerically, but the
        /// distinction matters for how modifiers compose.
        /// </summary>
        private static StatModifier? ReadModifier(JsonValue node)
        {
            if (node.IsNull) return null;
            return new StatModifier
            {
                MaxHp = node["maxHp"].AsNullableDouble(),
                MaxAether = node["maxAether"].AsNullableDouble(),
                Attack = node["attack"].AsNullableDouble(),
                Defense = node["defense"].AsNullableDouble(),
                Agility = node["agility"].AsNullableDouble(),
                Luck = node["luck"].AsNullableDouble(),
                Power = ReadOptionalElementDoubles(node["power"]),
                Resist = ReadOptionalElementDoubles(node["resist"]),
            };
        }

        private static GrowthCurve? ReadCurve(JsonValue node)
        {
            if (node.IsNull) return null;
            return new GrowthCurve(node["base"].AsDouble(), node["gain"].AsDouble(), node["curve"].AsDouble(1));
        }

        private static StatBlock ReadStatBlock(JsonValue node) => new StatBlock
        {
            MaxHp = node["maxHp"].AsDouble(),
            MaxAether = node["maxAether"].AsDouble(),
            Attack = node["attack"].AsDouble(),
            Defense = node["defense"].AsDouble(),
            Agility = node["agility"].AsDouble(),
            Luck = node["luck"].AsDouble(),
            Power = ReadElementDoubles(node["power"]),
            Resist = ReadElementDoubles(node["resist"]),
        }.Normalized();

        // --- definitions -----------------------------------------------------

        private static StatusDef ReadStatus(JsonValue node) => new StatusDef
        {
            Id = node["id"].AsString(),
            Name = node["name"].AsString(),
            Description = node["description"].IsNull ? null : node["description"].AsString(),
            Element = ReadOptionalElement(node["element"]),
            Duration = ReadDuration(node["duration"]),
            ExclusiveGroup = node["exclusiveGroup"].IsNull ? null : node["exclusiveGroup"].AsString(),
            Modifier = ReadModifier(node["modifier"]),
            DegenPerRound = node["degenPerRound"].AsDouble(),
            RegenPerRound = node["regenPerRound"].AsDouble(),
            PreventsAction = node["preventsAction"].AsBool(),
            PreventsArts = node["preventsArts"].AsBool(),
            BaseResistChance = node["baseResistChance"].AsDouble(),
            BreaksOnDamage = node["breaksOnDamage"].AsBool(),
            Incapacitates = node["incapacitates"].AsBool(),
        };

        private static StatusDuration ReadDuration(JsonValue node) => node["kind"].AsString() switch
        {
            "rounds" => StatusDuration.ForRounds(node["rounds"].AsInt()),
            "persistent" => StatusDuration.Persistent,
            _ => StatusDuration.WholeBattle,
        };

        private static ArtDef ReadArt(JsonValue node)
        {
            var art = new ArtDef
            {
                Id = node["id"].AsString(),
                Name = node["name"].AsString(),
                Description = node["description"].IsNull ? null : node["description"].AsString(),
                Element = ReadElement(node["element"]),
                Kind = ParseArtKind(node["kind"].AsString()),
                Targeting = ParseArtTargeting(node["targeting"].AsString()),
                Cost = node["cost"].AsDouble(),
                Priority = node["priority"].AsInt(),
                Accuracy = node["accuracy"].AsDouble(1),
                FieldEffect = node["fieldEffect"].IsNull ? null : node["fieldEffect"].AsString(),
                UsableOutOfBattle = node["usableOutOfBattle"].AsBool(),
                FieldOnly = node["fieldOnly"].AsBool(),
            };

            foreach (var tag in node["tags"].Items) art.Tags.Add(tag.AsString());

            var effect = node["effect"];
            art.Effect = new ArtEffect
            {
                Power = effect["power"].AsDouble(),
                Hits = effect["hits"].AsInt(1),
                SpreadFalloff = effect["spreadFalloff"].AsDouble(0.4),
                Drain = effect["drain"].AsDouble(),
                HealFraction = effect["healFraction"].AsDouble(),
                ReviveFraction = effect["reviveFraction"].AsDouble(),
                RestoreAether = effect["restoreAether"].AsDouble(),
            };
            foreach (var status in effect["statuses"].Items)
            {
                art.Effect.Statuses.Add(new ArtStatusChance
                {
                    StatusId = status["statusId"].AsString(),
                    Chance = status["chance"].AsDouble(),
                });
            }
            if (!effect["buff"].IsNull)
            {
                art.Effect.Buff = new ArtBuff
                {
                    Modifier = ReadModifier(effect["buff"]["modifier"]) ?? new StatModifier(),
                    Rounds = effect["buff"]["rounds"].AsInt(),
                };
            }
            return art;
        }

        private static MoteDef ReadMote(JsonValue node) => new MoteDef
        {
            Id = node["id"].AsString(),
            Name = node["name"].AsString(),
            Element = ReadElement(node["element"]),
            Bonus = ReadModifier(node["bonus"]),
            Effect = ReadMoteEffect(node["effect"]),
            RecoveryRounds = node["recoveryRounds"].AsInt(Motes.DefaultRecoveryRounds),
            FieldAbilityId = node["fieldAbilityId"].IsNull ? null : node["fieldAbilityId"].AsString(),
        };

        private static MoteEffect ReadMoteEffect(JsonValue node) => new MoteEffect
        {
            Kind = ParseMoteEffectKind(node["kind"].AsString()),
            Targeting = ParseMoteTargeting(node["targeting"].AsString()),
            Power = node["power"].AsDouble(),
            IgnoresDefense = node["ignoresDefense"].AsBool(),
            HpFraction = node["hpFraction"].AsDouble(),
            StatusId = node["statusId"].IsNull ? null : node["statusId"].AsString(),
            Chance = node["chance"].AsDouble(),
            Modifier = ReadModifier(node["modifier"]),
            Rounds = node["rounds"].AsInt(),
            Amount = node["amount"].AsDouble(),
            Tag = node["tag"].IsNull ? null : node["tag"].AsString(),
        };

        private static ClassDef ReadClass(JsonValue node)
        {
            var def = new ClassDef
            {
                Id = node["id"].AsString(),
                Name = node["name"].AsString(),
                Description = node["description"].IsNull ? null : node["description"].AsString(),
                Priority = node["priority"].AsInt(),
                Requires = new ClassRequirement
                {
                    MinMotes = ReadOptionalElementInts(node["requires"]["minMotes"]),
                    MaxMotes = ReadOptionalElementInts(node["requires"]["maxMotes"]),
                    MinTotal = node["requires"]["minTotal"].AsNullableInt(),
                },
            };

            foreach (var innate in node["innate"].Items)
            {
                if (Elements.TryParse(innate.AsString(), out var element)) def.Innate.Add(element);
            }

            if (!node["statMultipliers"].IsNull)
            {
                var m = node["statMultipliers"];
                def.StatMultipliers = new ClassMultipliers
                {
                    MaxHp = m["maxHp"].AsDouble(1),
                    MaxAether = m["maxAether"].AsDouble(1),
                    Attack = m["attack"].AsDouble(1),
                    Defense = m["defense"].AsDouble(1),
                    Agility = m["agility"].AsDouble(1),
                    Luck = m["luck"].AsDouble(1),
                };
            }

            foreach (var grant in node["arts"].Items)
            {
                def.Arts.Add(new ClassArtGrant { ArtId = grant["artId"].AsString(), Level = grant["level"].AsInt() });
            }

            return def;
        }

        private static ActorDef ReadActor(JsonValue node)
        {
            var def = new ActorDef
            {
                Id = node["id"].AsString(),
                Name = node["name"].AsString(),
                Innate = ReadElement(node["innate"]),
                PortraitId = node["portraitId"].IsNull ? null : node["portraitId"].AsString(),
                SpriteId = node["spriteId"].IsNull ? null : node["spriteId"].AsString(),
            };

            foreach (var art in node["baseArts"].Items) def.BaseArts.Add(art.AsString());

            var growth = node["growth"];
            def.Growth = new GrowthProfile
            {
                MaxHp = ReadCurve(growth["maxHp"]) ?? new GrowthCurve(),
                MaxAether = ReadCurve(growth["maxAether"]) ?? new GrowthCurve(),
                Attack = ReadCurve(growth["attack"]) ?? new GrowthCurve(),
                Defense = ReadCurve(growth["defense"]) ?? new GrowthCurve(),
                Agility = ReadCurve(growth["agility"]) ?? new GrowthCurve(),
                Luck = ReadCurve(growth["luck"]) ?? new GrowthCurve(),
            };
            foreach (var element in Elements.All)
            {
                def.Growth.Power[element] = ReadCurve(growth["power"][element.ToId()]);
                def.Growth.Resist[element] = ReadCurve(growth["resist"][element.ToId()]);
            }

            return def;
        }

        private static GearDef ReadGear(JsonValue node)
        {
            var def = new GearDef
            {
                Id = node["id"].AsString(),
                Name = node["name"].AsString(),
                Description = node["description"].IsNull ? null : node["description"].AsString(),
                Slot = ParseSlot(node["slot"].AsString()),
                Modifier = ReadModifier(node["modifier"]),
                Element = ReadOptionalElement(node["element"]),
                Value = node["value"].AsDouble(),
            };

            foreach (var actorId in node["restrictedTo"].Items) def.RestrictedTo.Add(actorId.AsString());

            if (!node["proc"].IsNull)
            {
                def.Proc = new GearProc
                {
                    ArtId = node["proc"]["artId"].AsString(),
                    Chance = node["proc"]["chance"].AsDouble(),
                    PowerScale = node["proc"]["powerScale"].AsDouble(1),
                };
            }

            return def;
        }

        private static SummonDef ReadSummon(JsonValue node) => new SummonDef
        {
            Id = node["id"].AsString(),
            Name = node["name"].AsString(),
            Description = node["description"].IsNull ? null : node["description"].AsString(),
            Element = ReadElement(node["element"]),
            Cost = ReadElementInts(node["cost"]),
            BasePower = node["basePower"].AsDouble(),
            HpFractionPerMote = node["hpFractionPerMote"].AsDouble(),
            HpFractionCap = node["hpFractionCap"].AsDouble(0.5),
            HitsAll = node["hitsAll"].AsBool(),
            AfterglowPower = node["afterglowPower"].AsDouble(),
            AfterglowRounds = node["afterglowRounds"].AsInt(),
        };

        private static EnemyDef ReadEnemy(JsonValue node)
        {
            var def = new EnemyDef
            {
                Id = node["id"].AsString(),
                Name = node["name"].AsString(),
                Innate = ReadElement(node["innate"]),
                Stats = ReadStatBlock(node["stats"]),
                Xp = node["xp"].AsLong(),
                Coin = node["coin"].AsLong(),
                SpriteId = node["spriteId"].IsNull ? null : node["spriteId"].AsString(),
                BehaviorTag = node["behaviorTag"].IsNull ? null : node["behaviorTag"].AsString(),
                FleeResistance = node["fleeResistance"].AsDouble(),
            };

            foreach (var art in node["arts"].Items) def.Arts.Add(art.AsString());

            foreach (var move in node["moves"].Items)
            {
                def.Moves.Add(new EnemyMove
                {
                    ArtId = move["artId"].AsString(),
                    Weight = move["weight"].AsDouble(),
                    HpBelow = move["hpBelow"].AsNullableDouble(),
                    FromRound = move["fromRound"].AsNullableInt(),
                    Cooldown = move["cooldown"].AsInt(),
                });
            }

            foreach (var drop in node["drops"].Items)
            {
                def.Drops.Add(new EnemyDrop { ItemId = drop["itemId"].AsString(), Chance = drop["chance"].AsDouble() });
            }

            return def;
        }

        // --- enum vocabulary -------------------------------------------------
        //
        // Spelled exactly as the JSON spells them. An unrecognised value falls
        // back to the most conservative option rather than throwing, so a pack
        // written against a newer schema still loads what it can.

        private static ArtKind ParseArtKind(string value) => value switch
        {
            "physical" => ArtKind.Physical,
            "aetheric" => ArtKind.Aetheric,
            "heal" => ArtKind.Heal,
            "support" => ArtKind.Support,
            "field" => ArtKind.Field,
            _ => ArtKind.Aetheric,
        };

        private static ArtTargeting ParseArtTargeting(string value) => value switch
        {
            "one-foe" => ArtTargeting.OneFoe,
            "all-foes" => ArtTargeting.AllFoes,
            "spread-foes" => ArtTargeting.SpreadFoes,
            "self" => ArtTargeting.Self,
            "one-ally" => ArtTargeting.OneAlly,
            "all-allies" => ArtTargeting.AllAllies,
            "downed-ally" => ArtTargeting.DownedAlly,
            _ => ArtTargeting.OneFoe,
        };

        private static MoteEffectKind ParseMoteEffectKind(string value) => value switch
        {
            "damage" => MoteEffectKind.Damage,
            "heal" => MoteEffectKind.Heal,
            "revive" => MoteEffectKind.Revive,
            "status" => MoteEffectKind.Status,
            "buff" => MoteEffectKind.Buff,
            "restore-aether" => MoteEffectKind.RestoreAether,
            "utility" => MoteEffectKind.Utility,
            _ => MoteEffectKind.Utility,
        };

        private static MoteTargeting ParseMoteTargeting(string value) => value switch
        {
            "one-foe" => MoteTargeting.OneFoe,
            "all-foes" => MoteTargeting.AllFoes,
            "self" => MoteTargeting.Self,
            "one-ally" => MoteTargeting.OneAlly,
            "all-allies" => MoteTargeting.AllAllies,
            _ => MoteTargeting.Self,
        };

        private static EquipSlot ParseSlot(string value) => value switch
        {
            "weapon" => EquipSlot.Weapon,
            "armor" => EquipSlot.Armor,
            "helm" => EquipSlot.Helm,
            "shield" => EquipSlot.Shield,
            "trinket" => EquipSlot.Trinket,
            _ => EquipSlot.Trinket,
        };
    }
}
