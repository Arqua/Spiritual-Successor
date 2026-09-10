using System;
using System.Collections.Generic;
using System.Globalization;
using Aetherlight.Core;
using Aetherlight.Domain;

namespace Aetherlight.Save
{
    public sealed class InventoryEntry
    {
        public string ItemId = "";
        public int Count;
    }

    public sealed class SaveLocation
    {
        public string MapId = "";
        public int X;
        public int Y;
        public string Facing = "south";
    }

    public sealed class SaveFile
    {
        public int Version = SaveIo.CurrentVersion;
        /// <summary>Content packs this save expects, so a mismatch can be reported.</summary>
        public List<string> Packs = new List<string>();
        public string SavedAt = "";
        public double PlayTimeSeconds;
        public List<ActorState> Party = new List<ActorState>();
        /// <summary>Actors recruited but not in the active party.</summary>
        public List<ActorState> Reserve = new List<ActorState>();
        public List<InventoryEntry> Inventory = new List<InventoryEntry>();
        public long Coin;
        /// <summary>Motes collected but not yet bound to anyone.</summary>
        public List<string> LooseMotes = new List<string>();
        public SaveLocation Location = new SaveLocation();
        /// <summary>Arbitrary project-specific progress flags.</summary>
        public Dictionary<string, string> Flags = new Dictionary<string, string>();
    }

    /// <summary>
    /// Save files.
    ///
    /// Every piece of runtime state is already plain data, so saving is mostly
    /// stamping a version and writing it out. The version matters: content
    /// packs change, and a save written against an older schema should migrate
    /// rather than crash. Migrate is the single place that happens.
    ///
    /// The wire format is shared with the TypeScript implementation, so a save
    /// written by one engine loads in the other.
    /// </summary>
    public static class SaveIo
    {
        public const int CurrentVersion = 1;

        /// <summary>
        /// JSON has no infinity, and a status that lasts until cured carries
        /// exactly that as its remaining duration. It travels as null, which is
        /// what the TypeScript side already writes (JSON.stringify turns
        /// Infinity into null), so the two implementations agree on the wire.
        /// Reading null - or a missing field - restores the infinity.
        /// </summary>
        private static double ReadRemaining(JsonValue node) =>
            node.Kind == JsonKind.Number ? node.AsDouble() : double.PositiveInfinity;

        public static string Serialize(SaveFile save)
        {
            var w = new JsonWriter();
            w.BeginObject();
            w.Member("version", (long)save.Version);

            w.Name("packs").BeginArray();
            foreach (var pack in save.Packs) w.Value(pack);
            w.EndArray();

            w.Member("savedAt", save.SavedAt);
            w.Member("playTimeSeconds", save.PlayTimeSeconds);

            w.Name("party").BeginArray();
            foreach (var actor in save.Party) WriteActor(w, actor);
            w.EndArray();

            w.Name("reserve").BeginArray();
            foreach (var actor in save.Reserve) WriteActor(w, actor);
            w.EndArray();

            w.Name("inventory").BeginArray();
            foreach (var entry in save.Inventory)
            {
                w.BeginObject();
                w.Member("itemId", entry.ItemId);
                w.Member("count", (long)entry.Count);
                w.EndObject();
            }
            w.EndArray();

            w.Member("coin", save.Coin);

            w.Name("looseMotes").BeginArray();
            foreach (var moteId in save.LooseMotes) w.Value(moteId);
            w.EndArray();

            w.Name("location").BeginObject();
            w.Member("mapId", save.Location.MapId);
            w.Name("position").BeginObject();
            w.Member("x", (long)save.Location.X);
            w.Member("y", (long)save.Location.Y);
            w.EndObject();
            w.Member("facing", save.Location.Facing);
            w.EndObject();

            w.Name("flags").BeginObject();
            foreach (var flag in save.Flags) w.Member(flag.Key, flag.Value);
            w.EndObject();

            w.EndObject();
            return w.ToString();
        }

        private static void WriteActor(JsonWriter w, ActorState actor)
        {
            w.BeginObject();
            w.Member("id", actor.Id);
            w.Member("defId", actor.DefId);
            w.Member("level", (long)actor.Level);
            w.Member("xp", actor.Xp);
            w.Member("hp", actor.Hp);
            w.Member("aether", actor.Aether);

            w.Name("motes").BeginArray();
            foreach (var mote in actor.Motes)
            {
                w.BeginObject();
                w.Member("defId", mote.DefId);
                w.Member("state", SpellState(mote.State));
                w.Member("recoveryLeft", (long)mote.RecoveryLeft);
                w.EndObject();
            }
            w.EndArray();

            w.Name("equipment").BeginObject();
            foreach (EquipSlot slot in Enum.GetValues(typeof(EquipSlot)))
            {
                var id = actor.Equipment[slot];
                if (id != null) w.Member(SpellSlot(slot), id);
            }
            w.EndObject();

            w.Name("statuses").BeginArray();
            foreach (var status in actor.Statuses)
            {
                w.BeginObject();
                w.Member("statusId", status.StatusId);
                if (double.IsInfinity(status.Remaining)) w.Name("remaining").Null();
                else w.Member("remaining", status.Remaining);
                w.Member("sourceId", status.SourceId);
                w.EndObject();
            }
            w.EndArray();

            w.EndObject();
        }

        public static SaveFile Deserialize(string text)
        {
            var root = JsonValue.Parse(text);
            if (root.Kind != JsonKind.Object || root["party"].Kind != JsonKind.Array)
                throw new InvalidOperationException("Not a valid save file");
            return Migrate(FromJson(root));
        }

        private static SaveFile FromJson(JsonValue root)
        {
            var save = new SaveFile
            {
                Version = root["version"].AsInt(),
                SavedAt = root["savedAt"].AsString(),
                PlayTimeSeconds = root["playTimeSeconds"].AsDouble(),
                Coin = root["coin"].AsLong(),
            };

            foreach (var pack in root["packs"].Items) save.Packs.Add(pack.AsString());
            foreach (var actor in root["party"].Items) save.Party.Add(ReadActor(actor));
            foreach (var actor in root["reserve"].Items) save.Reserve.Add(ReadActor(actor));
            foreach (var moteId in root["looseMotes"].Items) save.LooseMotes.Add(moteId.AsString());

            foreach (var entry in root["inventory"].Items)
            {
                save.Inventory.Add(new InventoryEntry { ItemId = entry["itemId"].AsString(), Count = entry["count"].AsInt() });
            }

            save.Location = new SaveLocation
            {
                MapId = root["location"]["mapId"].AsString(),
                X = root["location"]["position"]["x"].AsInt(),
                Y = root["location"]["position"]["y"].AsInt(),
                Facing = root["location"]["facing"].AsString("south"),
            };

            foreach (var flag in root["flags"].Members)
            {
                save.Flags[flag.Key] = flag.Value.Kind switch
                {
                    JsonKind.String => flag.Value.AsString(),
                    JsonKind.Number => flag.Value.AsDouble().ToString("R", CultureInfo.InvariantCulture),
                    JsonKind.Bool => flag.Value.AsBool() ? "true" : "false",
                    _ => "",
                };
            }

            return save;
        }

        private static ActorState ReadActor(JsonValue node)
        {
            var actor = new ActorState
            {
                Id = node["id"].AsString(),
                DefId = node["defId"].AsString(),
                Level = node["level"].AsInt(1),
                Xp = node["xp"].AsLong(),
                Hp = node["hp"].AsDouble(),
                Aether = node["aether"].AsDouble(),
            };

            foreach (var mote in node["motes"].Items)
            {
                actor.Motes.Add(new MoteInstance
                {
                    DefId = mote["defId"].AsString(),
                    State = ParseState(mote["state"].AsString()),
                    RecoveryLeft = mote["recoveryLeft"].AsInt(),
                });
            }

            foreach (var slot in node["equipment"].Members)
            {
                if (TryParseSlot(slot.Key, out var parsed)) actor.Equipment[parsed] = slot.Value.AsString();
            }

            foreach (var status in node["statuses"].Items)
            {
                actor.Statuses.Add(new StatusInstance
                {
                    StatusId = status["statusId"].AsString(),
                    Remaining = ReadRemaining(status["remaining"]),
                    SourceId = status["sourceId"].IsNull ? null : status["sourceId"].AsString(),
                });
            }

            return actor;
        }

        /// <summary>
        /// Bring an older save up to the current version. Each step is a
        /// discrete block, so adding version 2 means adding one block rather
        /// than rewriting the method.
        /// </summary>
        public static SaveFile Migrate(SaveFile save)
        {
            if (save.Version < 1)
            {
                save.Version = 1;
            }

            // Defensive fill-ins for a hand-edited save.
            foreach (var actor in save.Party) FillDefaults(actor);
            foreach (var actor in save.Reserve) FillDefaults(actor);

            save.Version = CurrentVersion;
            return save;
        }

        private static void FillDefaults(ActorState actor)
        {
            actor.Motes ??= new List<MoteInstance>();
            actor.Statuses ??= new List<StatusInstance>();
            actor.TempModifiers ??= new List<TimedModifier>();
            actor.Equipment ??= new Loadout();
            if (actor.Level < 1) actor.Level = 1;
        }

        // The wire vocabulary, matching the TypeScript side.
        private static string SpellState(MoteState state) => state switch
        {
            MoteState.Set => "set",
            MoteState.Standby => "standby",
            MoteState.Recovering => "recovering",
            _ => "set",
        };

        private static MoteState ParseState(string value) => value switch
        {
            "standby" => MoteState.Standby,
            "recovering" => MoteState.Recovering,
            _ => MoteState.Set,
        };

        private static string SpellSlot(EquipSlot slot) => slot switch
        {
            EquipSlot.Weapon => "weapon",
            EquipSlot.Armor => "armor",
            EquipSlot.Helm => "helm",
            EquipSlot.Shield => "shield",
            EquipSlot.Trinket => "trinket",
            _ => "trinket",
        };

        private static bool TryParseSlot(string value, out EquipSlot slot)
        {
            switch (value)
            {
                case "weapon": slot = EquipSlot.Weapon; return true;
                case "armor": slot = EquipSlot.Armor; return true;
                case "helm": slot = EquipSlot.Helm; return true;
                case "shield": slot = EquipSlot.Shield; return true;
                case "trinket": slot = EquipSlot.Trinket; return true;
                default: slot = EquipSlot.Trinket; return false;
            }
        }
    }
}
