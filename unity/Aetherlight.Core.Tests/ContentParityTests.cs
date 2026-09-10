using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using Aetherlight.Core;
using Aetherlight.Domain;

namespace Aetherlight.Tests
{
    /// <summary>
    /// Cross-language content parity.
    ///
    /// Both implementations read content/starter.json. This test recomputes the
    /// fingerprint that tools/content-fingerprint.ts produces from the
    /// TypeScript side and asserts it matches byte for byte.
    ///
    /// The fingerprint deliberately covers more than the parse: stat curves
    /// evaluated at three levels, class resolution across six mote spreads,
    /// and the numbers on every art, enemy, summon and mote. Identical parsing
    /// is necessary but not sufficient - what matters is that both engines
    /// *behave* the same on the same content, and derivation is where a port
    /// drifts silently.
    /// </summary>
    public class ContentParityTests
    {
        private static string ContentPath(string file) =>
            Path.Combine(AppContext.BaseDirectory, "content", file);

        private static string Num(double value) =>
            value.ToString("F6", CultureInfo.InvariantCulture);

        // The JSON's vocabulary, not C#'s. The file is the contract.
        private static string Spell(ArtKind kind) => kind switch
        {
            ArtKind.Physical => "physical",
            ArtKind.Aetheric => "aetheric",
            ArtKind.Heal => "heal",
            ArtKind.Support => "support",
            ArtKind.Field => "field",
            _ => "?",
        };

        private static string Spell(ArtTargeting targeting) => targeting switch
        {
            ArtTargeting.OneFoe => "one-foe",
            ArtTargeting.AllFoes => "all-foes",
            ArtTargeting.SpreadFoes => "spread-foes",
            ArtTargeting.Self => "self",
            ArtTargeting.OneAlly => "one-ally",
            ArtTargeting.AllAllies => "all-allies",
            ArtTargeting.DownedAlly => "downed-ally",
            _ => "?",
        };

        private static string Spell(MoteEffectKind kind) => kind switch
        {
            MoteEffectKind.Damage => "damage",
            MoteEffectKind.Heal => "heal",
            MoteEffectKind.Revive => "revive",
            MoteEffectKind.Status => "status",
            MoteEffectKind.Buff => "buff",
            MoteEffectKind.RestoreAether => "restore-aether",
            MoteEffectKind.Utility => "utility",
            _ => "?",
        };

        private static string Spell(MoteTargeting targeting) => targeting switch
        {
            MoteTargeting.OneFoe => "one-foe",
            MoteTargeting.AllFoes => "all-foes",
            MoteTargeting.Self => "self",
            MoteTargeting.OneAlly => "one-ally",
            MoteTargeting.AllAllies => "all-allies",
            _ => "?",
        };

        /// <summary>JavaScript prints booleans lower-case; C# does not.</summary>
        private static string Spell(bool value) => value ? "true" : "false";

        private static List<string> Fingerprint(ContentRegistry registry)
        {
            var lines = new List<string>();

            lines.Add(
                $"counts actors={registry.Actors.Count} arts={registry.Arts.Count} classes={registry.Classes.Count} " +
                $"enemies={registry.Enemies.Count} gear={registry.Gear.Count} motes={registry.Motes.Count} " +
                $"statuses={registry.Statuses.Count} summons={registry.Summons.Count}");

            static string SortedIds<T>(Dictionary<string, T> map) =>
                string.Join(",", map.Keys.OrderBy(k => k, StringComparer.Ordinal));

            lines.Add($"ids.actors {SortedIds(registry.Actors)}");
            lines.Add($"ids.arts {SortedIds(registry.Arts)}");
            lines.Add($"ids.classes {SortedIds(registry.Classes)}");
            lines.Add($"ids.motes {SortedIds(registry.Motes)}");
            lines.Add($"ids.statuses {SortedIds(registry.Statuses)}");
            lines.Add($"ids.summons {SortedIds(registry.Summons)}");
            lines.Add($"ids.gear {SortedIds(registry.Gear)}");
            lines.Add($"ids.enemies {SortedIds(registry.Enemies)}");

            foreach (var actorId in registry.Actors.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                var def = registry.ActorDef(actorId)!;
                foreach (int level in new[] { 1, 12, 40 })
                {
                    var state = Actors.Create(def, level, registry);
                    var stats = Actors.DeriveStats(state, registry);
                    string power = string.Join(" ", Elements.All.Select(e => $"{e.ToId()}={Num(stats.Power[e])}"));
                    string resist = string.Join(" ", Elements.All.Select(e => $"{e.ToId()}={Num(stats.Resist[e])}"));
                    lines.Add(
                        $"actor.{actorId}.L{level} hp={Num(stats.MaxHp)} aether={Num(stats.MaxAether)} " +
                        $"atk={Num(stats.Attack)} def={Num(stats.Defense)} agi={Num(stats.Agility)} luck={Num(stats.Luck)} " +
                        $"power[{power}] resist[{resist}]");
                }
            }

            var spreads = new (string Label, string[] Motes)[]
            {
                ("none", System.Array.Empty<string>()),
                ("terra1", new[] { "boulder" }),
                ("terra2", new[] { "boulder", "loam" }),
                ("terra1pyre1", new[] { "boulder", "cinder" }),
                ("aeris1rime1", new[] { "zephyr", "hoarfrost" }),
                ("allfour", new[] { "boulder", "cinder", "zephyr", "hoarfrost" }),
            };
            foreach (var actorId in registry.Actors.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                var def = registry.ActorDef(actorId)!;
                foreach (var spread in spreads)
                {
                    var state = Actors.Create(def, 20, registry);
                    state.Motes = spread.Motes.Select(id => new MoteInstance(id)).ToList();
                    lines.Add($"class.{actorId}.{spread.Label} {Actors.DeriveClass(state, registry)?.Id ?? "-"}");
                }
            }

            foreach (var artId in registry.Arts.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                var art = registry.ArtDef(artId)!;
                string statuses = art.Effect.Statuses.Count > 0
                    ? string.Join("|", art.Effect.Statuses.Select(s => $"{s.StatusId}:{Num(s.Chance)}"))
                    : "-";
                lines.Add(
                    $"art.{artId} cost={Num(art.Cost)} power={Num(art.Effect.Power)} " +
                    $"hits={art.Effect.Hits} kind={Spell(art.Kind)} targeting={Spell(art.Targeting)} element={art.Element.ToId()} " +
                    $"priority={art.Priority} statuses={statuses}");
            }

            foreach (var enemyId in registry.Enemies.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                var enemy = registry.EnemyDef(enemyId)!;
                var s = enemy.Stats;
                lines.Add(
                    $"enemy.{enemyId} hp={Num(s.MaxHp)} aether={Num(s.MaxAether)} atk={Num(s.Attack)} def={Num(s.Defense)} " +
                    $"agi={Num(s.Agility)} luck={Num(s.Luck)} xp={enemy.Xp} coin={enemy.Coin} moves={enemy.Moves.Count}");
            }

            foreach (var summonId in registry.Summons.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                var summon = registry.SummonDef(summonId)!;
                string cost = string.Join(" ", Elements.All.Select(e => $"{e.ToId()}={summon.Cost[e]}"));
                lines.Add(
                    $"summon.{summonId} base={Num(summon.BasePower)} perMote={Num(summon.HpFractionPerMote)} " +
                    $"cap={Num(summon.HpFractionCap)} hitsAll={Spell(summon.HitsAll)} cost[{cost}]");
            }

            foreach (var moteId in registry.Motes.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                var mote = registry.MoteDef(moteId)!;
                lines.Add(
                    $"mote.{moteId} element={mote.Element.ToId()} effect={Spell(mote.Effect.Kind)} " +
                    $"power={Num(mote.Effect.Power)} " +
                    $"targeting={Spell(mote.Effect.Targeting)} recovery={mote.RecoveryRounds}");
            }

            return lines;
        }

        [Fact]
        public void LoadsTheCanonicalPack()
        {
            var registry = ContentRegistry.From(ContentLoader.LoadPack(File.ReadAllText(ContentPath("starter.json"))));
            Assert.NotEmpty(registry.Actors);
            Assert.NotEmpty(registry.Arts);
            Assert.NotEmpty(registry.Enemies);
        }

        [Fact]
        public void TheCanonicalPackValidatesCleanly()
        {
            var registry = ContentRegistry.From(ContentLoader.LoadPack(File.ReadAllText(ContentPath("starter.json"))));
            var errors = registry.Validate().FindAll(i => i.IsError);
            Assert.Empty(errors);
        }

        [Fact]
        public void FingerprintMatchesTheTypeScriptImplementation()
        {
            var registry = ContentRegistry.From(ContentLoader.LoadPack(File.ReadAllText(ContentPath("starter.json"))));
            var actual = Fingerprint(registry);
            var expected = File.ReadAllText(ContentPath("starter.fingerprint.txt"))
                .Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

            // Compare line by line: a whole-blob assert reports "these two
            // 6KB strings differ", which is useless when one number drifted.
            Assert.Equal(expected.Length, actual.Count);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.True(expected[i] == actual[i],
                    $"line {i + 1} differs:\n  TypeScript: {expected[i]}\n  C#        : {actual[i]}");
            }
        }
    }
}
