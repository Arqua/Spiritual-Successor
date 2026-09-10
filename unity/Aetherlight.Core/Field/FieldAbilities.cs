using System;
using System.Collections.Generic;
using Aetherlight.Core;
using Aetherlight.Domain;

namespace Aetherlight.Field
{
    public enum FieldShapeKind
    {
        /// <summary>The single tile the user is facing.</summary>
        Facing,
        /// <summary>Every tile within a radius of the user.</summary>
        Radius,
        /// <summary>A straight line ahead, stopping at the first solid tile.</summary>
        Line,
        /// <summary>The user's own tile.</summary>
        Self,
    }

    public sealed class FieldShape
    {
        public FieldShapeKind Kind = FieldShapeKind.Facing;
        public int Distance = 1;
        public int Radius;
        public int Length;

        public static FieldShape FacingTile(int distance = 1) =>
            new FieldShape { Kind = FieldShapeKind.Facing, Distance = distance };

        public static FieldShape WithinRadius(int radius) =>
            new FieldShape { Kind = FieldShapeKind.Radius, Radius = radius };

        public static FieldShape LineAhead(int length) =>
            new FieldShape { Kind = FieldShapeKind.Line, Length = length };

        public static readonly FieldShape OwnTile = new FieldShape { Kind = FieldShapeKind.Self };
    }

    public enum FieldEffectKind
    {
        Lift,
        Push,
        Toggle,
        Raise,
        Lower,
        Reveal,
        Burn,
        Freeze,
        Grow,
    }

    public sealed class FieldAbilityDef
    {
        public string Id = "";
        public string Name = "";
        public Element Element;
        public string? Description;
        /// <summary>Aether cost when used on the field. Zero for free tools.</summary>
        public double Cost;
        public FieldShape Shape = FieldShape.FacingTile();
        public FieldEffectKind Effect;
        /// <summary>Elevation delta for raise and lower.</summary>
        public double? ElevationDelta;
        /// <summary>Terrain the affected tile becomes.</summary>
        public string? SetsTerrain;
    }

    public sealed class FieldWorld
    {
        public Grid Grid;
        public List<Interactable> Interactables = new List<Interactable>();

        public FieldWorld(Grid grid)
        {
            Grid = grid;
        }
    }

    public enum FieldChangeKind { Moved, State, Elevation, Revealed, Removed, Terrain }

    /// <summary>
    /// A description of what changed, for the renderer to replay. The rules
    /// layer resolves what happened; it never animates.
    /// </summary>
    public sealed class FieldChange
    {
        public string? InteractableId;
        public Vec2 Position;
        public FieldChangeKind Kind;

        public string? FromText;
        public string? ToText;
        public double? FromNumber;
        public double? ToNumber;
        public Vec2? FromPosition;
        public Vec2? ToPosition;
    }

    public sealed class FieldOutcome
    {
        /// <summary>False when nothing in range responded; the UI should say so.</summary>
        public bool Applied;
        public List<FieldChange> Changes = new List<FieldChange>();
        public List<GameEvent> Events = new List<GameEvent>();
        /// <summary>Set when the ability could not be used at all.</summary>
        public string? Reason;
    }

    public sealed class FieldUseContext
    {
        public FieldWorld World;
        public Vec2 Origin;
        public Facing Facing;
        /// <summary>Destination tile for Lift, ignored by other effects.</summary>
        public Vec2? Destination;

        public FieldUseContext(FieldWorld world, Vec2 origin, Facing facing)
        {
            World = world;
            Origin = origin;
            Facing = facing;
        }
    }

    /// <summary>
    /// Field abilities: the overworld half of the toolkit.
    ///
    /// An ability reaches its targets through a shape - the tile in front of
    /// you, a radius, a line - and then applies its effect to whichever
    /// interactables in that shape declared they listen for it.
    /// </summary>
    public static class FieldAbilities
    {
        /// <summary>Tiles an ability reaches, in resolution order.</summary>
        public static List<Vec2> TilesInShape(FieldShape shape, Vec2 origin, Facing facing, Grid grid)
        {
            var tiles = new List<Vec2>();
            switch (shape.Kind)
            {
                case FieldShapeKind.Self:
                    tiles.Add(origin);
                    break;

                case FieldShapeKind.Facing:
                    tiles.Add(Grid.Translate(origin, facing, shape.Distance));
                    break;

                case FieldShapeKind.Line:
                    for (int step = 1; step <= shape.Length; step++)
                    {
                        var position = Grid.Translate(origin, facing, step);
                        var tile = grid.At(position);
                        if (tile == null) break;
                        tiles.Add(position);
                        if (tile.Solid) break;
                    }
                    break;

                case FieldShapeKind.Radius:
                    for (int y = origin.Y - shape.Radius; y <= origin.Y + shape.Radius; y++)
                    {
                        for (int x = origin.X - shape.Radius; x <= origin.X + shape.Radius; x++)
                        {
                            if (!grid.InBounds(x, y)) continue;
                            var candidate = new Vec2(x, y);
                            if (Grid.Manhattan(origin, candidate) > shape.Radius) continue;
                            tiles.Add(candidate);
                        }
                    }
                    break;
            }
            return tiles;
        }

        /// <summary>
        /// Resolve a field ability. Mutates the world and returns a description
        /// of every change so the presentation layer can play it back.
        /// </summary>
        public static FieldOutcome Use(FieldAbilityDef def, FieldUseContext ctx)
        {
            var world = ctx.World;
            var tiles = TilesInShape(def.Shape, ctx.Origin, ctx.Facing, world.Grid);
            var changes = new List<FieldChange>();
            var touched = new List<string>();

            foreach (var tile in tiles)
            {
                var targets = world.Interactables.FindAll(item =>
                    item.Position == tile && item.RespondsToAbility(def.Id));

                foreach (var target in targets)
                {
                    var applied = ApplyEffect(def, target, ctx, tile);
                    if (applied != null && applied.Count > 0)
                    {
                        changes.AddRange(applied);
                        touched.Add(target.Id);
                    }
                }

                // Raise and lower reshape terrain directly, object or not.
                if ((def.Effect == FieldEffectKind.Raise || def.Effect == FieldEffectKind.Lower)
                    && targets.Count == 0
                    && def.ElevationDelta.HasValue)
                {
                    var existing = world.Grid.At(tile);
                    if (existing != null)
                    {
                        double delta = def.Effect == FieldEffectKind.Raise ? def.ElevationDelta.Value : -def.ElevationDelta.Value;
                        double from = existing.Elevation;
                        world.Grid.Patch(tile.X, tile.Y, elevation: from + delta);
                        changes.Add(new FieldChange
                        {
                            Position = tile,
                            Kind = FieldChangeKind.Elevation,
                            FromNumber = from,
                            ToNumber = from + delta,
                        });
                    }
                }
            }

            if (changes.Count == 0)
            {
                return new FieldOutcome { Applied = false, Reason = "nothing-responded" };
            }

            var outcome = new FieldOutcome { Applied = true, Changes = changes };
            outcome.Events.Add(new FieldEffectEvent
            {
                AbilityId = def.Id,
                OriginX = ctx.Origin.X,
                OriginY = ctx.Origin.Y,
                TargetIds = touched,
            });
            return outcome;
        }

        private static List<FieldChange>? ApplyEffect(FieldAbilityDef def, Interactable target, FieldUseContext ctx, Vec2 tile)
        {
            var world = ctx.World;

            switch (def.Effect)
            {
                case FieldEffectKind.Lift:
                {
                    if (!ctx.Destination.HasValue) return null;
                    var destination = ctx.Destination.Value;
                    var tileAt = world.Grid.At(destination);
                    if (tileAt == null || tileAt.Solid) return null;

                    var from = target.Position;
                    target.Position = destination;
                    return new List<FieldChange>
                    {
                        new FieldChange
                        {
                            InteractableId = target.Id,
                            Position = destination,
                            Kind = FieldChangeKind.Moved,
                            FromPosition = from,
                            ToPosition = destination,
                        },
                    };
                }

                case FieldEffectKind.Push:
                {
                    var destination = Grid.Translate(tile, ctx.Facing, 1);
                    var tileAt = world.Grid.At(destination);
                    if (tileAt == null || tileAt.Solid) return null;

                    bool blocked = world.Interactables.Exists(item =>
                        item.Id != target.Id && item.Blocking && item.Position == destination);
                    if (blocked) return null;

                    var from = target.Position;
                    target.Position = destination;
                    return new List<FieldChange>
                    {
                        new FieldChange
                        {
                            InteractableId = target.Id,
                            Position = destination,
                            Kind = FieldChangeKind.Moved,
                            FromPosition = from,
                            ToPosition = destination,
                        },
                    };
                }

                case FieldEffectKind.Toggle:
                {
                    string from = target.State ?? "off";
                    target.State = from == "on" ? "off" : "on";
                    if (target.ClearsBlocking) target.Blocking = target.State != "on";
                    return new List<FieldChange>
                    {
                        new FieldChange
                        {
                            InteractableId = target.Id,
                            Position = tile,
                            Kind = FieldChangeKind.State,
                            FromText = from,
                            ToText = target.State,
                        },
                    };
                }

                case FieldEffectKind.Reveal:
                {
                    if (target.Kind != InteractableKind.Hidden && target.Kind != InteractableKind.Inert) return null;
                    target.State = "revealed";
                    return new List<FieldChange>
                    {
                        new FieldChange
                        {
                            InteractableId = target.Id,
                            Position = tile,
                            Kind = FieldChangeKind.Revealed,
                            ToText = "revealed",
                        },
                    };
                }

                case FieldEffectKind.Burn:
                {
                    target.Spent = true;
                    target.Blocking = false;
                    var changes = new List<FieldChange>
                    {
                        new FieldChange { InteractableId = target.Id, Position = tile, Kind = FieldChangeKind.Removed },
                    };
                    if (def.SetsTerrain != null)
                    {
                        world.Grid.Patch(tile.X, tile.Y, solid: false, terrain: def.SetsTerrain);
                        changes.Add(new FieldChange { Position = tile, Kind = FieldChangeKind.Terrain, ToText = def.SetsTerrain });
                    }
                    return changes;
                }

                case FieldEffectKind.Freeze:
                case FieldEffectKind.Grow:
                case FieldEffectKind.Raise:
                case FieldEffectKind.Lower:
                {
                    var existing = world.Grid.At(tile);
                    if (existing == null) return null;
                    var changes = new List<FieldChange>();

                    double? targetElevation = target.BecomesElevation;
                    if (!targetElevation.HasValue && def.ElevationDelta.HasValue)
                    {
                        targetElevation = existing.Elevation
                            + (def.Effect == FieldEffectKind.Lower ? -def.ElevationDelta.Value : def.ElevationDelta.Value);
                    }

                    if (targetElevation.HasValue)
                    {
                        double from = existing.Elevation;
                        world.Grid.Patch(tile.X, tile.Y, solid: false, elevation: targetElevation.Value);
                        changes.Add(new FieldChange
                        {
                            Position = tile,
                            Kind = FieldChangeKind.Elevation,
                            FromNumber = from,
                            ToNumber = targetElevation.Value,
                        });
                    }

                    string? terrain = target.BecomesTerrain ?? def.SetsTerrain;
                    if (terrain != null)
                    {
                        string? fromTerrain = existing.Terrain;
                        world.Grid.Patch(tile.X, tile.Y, terrain: terrain);
                        changes.Add(new FieldChange
                        {
                            Position = tile,
                            Kind = FieldChangeKind.Terrain,
                            FromText = fromTerrain,
                            ToText = terrain,
                        });
                    }

                    string fromState = target.State ?? "idle";
                    target.State = def.Effect.ToString().ToLowerInvariant();
                    target.Spent = true;
                    changes.Add(new FieldChange
                    {
                        InteractableId = target.Id,
                        Position = tile,
                        Kind = FieldChangeKind.State,
                        FromText = fromState,
                        ToText = target.State,
                    });

                    return changes;
                }

                default:
                    return null;
            }
        }

        /// <summary>Field abilities available to a party, from set motes and known arts.</summary>
        public static List<string> Available(IEnumerable<string?> moteFieldAbilityIds, IEnumerable<string?> artFieldEffectIds)
        {
            var seen = new HashSet<string>();
            var ids = new List<string>();
            foreach (var id in moteFieldAbilityIds) if (id != null && seen.Add(id)) ids.Add(id);
            foreach (var id in artFieldEffectIds) if (id != null && seen.Add(id)) ids.Add(id);
            return ids;
        }
    }
}
