using System.Collections.Generic;
using Xunit;
using Aetherlight.Domain;
using Aetherlight.Field;

namespace Aetherlight.Tests
{
    /// <summary>Ported from test/field.test.ts.</summary>
    public class GridTests
    {
        private static Grid Flat(int width = 6, int height = 6, double elevation = 0)
        {
            var tiles = new List<Tile>();
            for (int i = 0; i < width * height; i++) tiles.Add(new Tile { Elevation = elevation });
            return new Grid(width, height, tiles, stepHeight: 1);
        }

        [Fact]
        public void ReportsBoundsCorrectly()
        {
            var grid = Flat(4, 3);
            Assert.True(grid.InBounds(0, 0));
            Assert.True(grid.InBounds(3, 2));
            Assert.False(grid.InBounds(4, 2));
            Assert.Null(grid.At(9, 9));
        }

        [Fact]
        public void RejectsATileCountThatDoesNotMatchTheDimensions()
        {
            Assert.Throws<System.ArgumentException>(() => new Grid(4, 4, new List<Tile> { new Tile() }));
        }

        [Fact]
        public void BlocksMovementIntoSolidTiles()
        {
            var grid = Flat();
            grid.Patch(1, 0, solid: true);
            Assert.False(grid.CanStep(new Vec2(0, 0), new Vec2(1, 0)));
        }

        [Fact]
        public void AllowsAStepUpWithinStepHeightButNotBeyond()
        {
            var grid = Flat();
            grid.Patch(1, 0, elevation: 1);
            grid.Patch(2, 0, elevation: 3);
            Assert.True(grid.CanStep(new Vec2(0, 0), new Vec2(1, 0)));
            Assert.False(grid.CanStep(new Vec2(0, 0), new Vec2(2, 0)));
        }

        [Fact]
        public void AllowsDroppingFurtherThanItAllowsClimbing()
        {
            var grid = Flat();
            grid.Patch(0, 0, elevation: 5);
            // Down from 5 to 0 is fine; back up is not.
            Assert.True(grid.CanStep(new Vec2(0, 0), new Vec2(1, 0)));
            Assert.False(grid.CanStep(new Vec2(1, 0), new Vec2(0, 0)));
        }

        [Fact]
        public void RespectsMaxDropWhenConfigured()
        {
            var grid = new Grid(3, 1, new List<Tile>
            {
                new Tile { Elevation = 9 },
                new Tile { Elevation = 0 },
                new Tile { Elevation = 0 },
            }, stepHeight: 1, maxDrop: 2);
            Assert.False(grid.CanStep(new Vec2(0, 0), new Vec2(1, 0)));
        }

        [Fact]
        public void BreaksLineOfSightOnOpaqueTiles()
        {
            var grid = Flat();
            Assert.True(grid.HasLineOfSight(new Vec2(0, 0), new Vec2(4, 0)));
            grid.Patch(2, 0, opaque: true);
            Assert.False(grid.HasLineOfSight(new Vec2(0, 0), new Vec2(4, 0)));
        }

        [Fact]
        public void RoundTripsThroughSerializableData()
        {
            var grid = Flat(3, 3);
            grid.Patch(1, 1, elevation: 2, terrain: "ice");
            var restored = Grid.FromData(grid.ToData());
            var tile = restored.At(1, 1)!;
            Assert.False(tile.Solid);
            Assert.Equal(2, tile.Elevation);
            Assert.Equal("ice", tile.Terrain);
        }

        [Fact]
        public void PreservesUnlimitedDropThroughSerialization()
        {
            // JSON has no infinity, so the sentinel has to survive the trip.
            var grid = Flat(2, 2);
            var restored = Grid.FromData(grid.ToData());
            Assert.True(double.IsPositiveInfinity(restored.MaxDrop));
        }

        [Fact]
        public void WalksAStraightLineInclusiveOfBothEnds()
        {
            var line = Grid.Bresenham(new Vec2(0, 0), new Vec2(3, 0));
            Assert.Equal(new[] { new Vec2(0, 0), new Vec2(1, 0), new Vec2(2, 0), new Vec2(3, 0) }, line);
        }

        [Fact]
        public void TranslatesByFacing()
        {
            Assert.Equal(new Vec2(2, 1), Grid.Translate(new Vec2(2, 2), Facing.North));
            Assert.Equal(new Vec2(5, 2), Grid.Translate(new Vec2(2, 2), Facing.East, 3));
            Assert.Equal(5, Grid.Manhattan(new Vec2(0, 0), new Vec2(2, 3)));
        }
    }

    public class FieldShapeTests
    {
        private static Grid Flat(int width = 6, int height = 6)
        {
            var tiles = new List<Tile>();
            for (int i = 0; i < width * height; i++) tiles.Add(new Tile());
            return new Grid(width, height, tiles);
        }

        [Fact]
        public void TargetsTheTileBeingFaced()
        {
            var tiles = FieldAbilities.TilesInShape(FieldShape.FacingTile(), new Vec2(2, 2), Facing.North, Flat());
            Assert.Equal(new[] { new Vec2(2, 1) }, tiles);
        }

        [Fact]
        public void StopsALineAtTheFirstSolidTile()
        {
            var grid = Flat();
            grid.Patch(2, 0, solid: true);
            var tiles = FieldAbilities.TilesInShape(FieldShape.LineAhead(5), new Vec2(0, 0), Facing.East, grid);
            Assert.Equal(new[] { new Vec2(1, 0), new Vec2(2, 0) }, tiles);
        }

        [Fact]
        public void CoversADiamondForRadiusShapes()
        {
            var tiles = FieldAbilities.TilesInShape(FieldShape.WithinRadius(1), new Vec2(2, 2), Facing.North, Flat());
            Assert.Equal(5, tiles.Count);
        }
    }

    public class FieldAbilityTests
    {
        private static FieldWorld World(params Interactable[] interactables)
        {
            var tiles = new List<Tile>();
            for (int i = 0; i < 36; i++) tiles.Add(new Tile());
            var world = new FieldWorld(new Grid(6, 6, tiles));
            world.Interactables.AddRange(interactables);
            return world;
        }

        private static readonly FieldAbilityDef Burn = new FieldAbilityDef
        {
            Id = "kindle",
            Name = "Kindle",
            Element = Element.Pyre,
            Shape = FieldShape.FacingTile(),
            Effect = FieldEffectKind.Burn,
            SetsTerrain = "ash",
        };

        [Fact]
        public void BurnsAwayBrushThatListensForTheAbility()
        {
            var brush = new Interactable
            {
                Id = "brush-1",
                Position = new Vec2(2, 1),
                Kind = InteractableKind.Burnable,
                RespondsTo = { "kindle" },
                Blocking = true,
            };
            var world = World(brush);
            var outcome = FieldAbilities.Use(Burn, new FieldUseContext(world, new Vec2(2, 2), Facing.North));

            Assert.True(outcome.Applied);
            Assert.True(brush.Spent);
            Assert.False(brush.Blocking);
            Assert.Equal("ash", world.Grid.At(2, 1)!.Terrain);
            Assert.Single(outcome.Events);
        }

        [Fact]
        public void DoesNothingWhenTheObjectDoesNotListenForThatAbility()
        {
            var rock = new Interactable
            {
                Id = "rock",
                Position = new Vec2(2, 1),
                Kind = InteractableKind.Liftable,
                RespondsTo = { "heave" },
            };
            var outcome = FieldAbilities.Use(Burn, new FieldUseContext(World(rock), new Vec2(2, 2), Facing.North));
            Assert.False(outcome.Applied);
            Assert.Equal("nothing-responded", outcome.Reason);
        }

        [Fact]
        public void DoesNotRetriggerASpentObject()
        {
            var brush = new Interactable
            {
                Id = "brush",
                Position = new Vec2(2, 1),
                Kind = InteractableKind.Burnable,
                RespondsTo = { "kindle" },
                Spent = true,
            };
            Assert.False(FieldAbilities.Use(Burn, new FieldUseContext(World(brush), new Vec2(2, 2), Facing.North)).Applied);
        }

        [Fact]
        public void PushesABlockOneTileAndRefusesToPushItIntoAWall()
        {
            var push = new FieldAbilityDef
            {
                Id = "shove", Name = "Shove", Element = Element.Terra,
                Shape = FieldShape.FacingTile(), Effect = FieldEffectKind.Push,
            };
            var block = new Interactable
            {
                Id = "block",
                Position = new Vec2(2, 1),
                Kind = InteractableKind.Pushable,
                RespondsTo = { "shove" },
                Blocking = true,
            };
            var world = World(block);

            Assert.True(FieldAbilities.Use(push, new FieldUseContext(world, new Vec2(2, 2), Facing.North)).Applied);
            Assert.Equal(new Vec2(2, 0), block.Position);

            // Now against the north edge, so it cannot move further.
            Assert.False(FieldAbilities.Use(push, new FieldUseContext(world, new Vec2(2, 1), Facing.North)).Applied);
        }

        [Fact]
        public void FreezingWaterRaisesAStandablePlatform()
        {
            var glaciate = new FieldAbilityDef
            {
                Id = "glaciate", Name = "Glaciate", Element = Element.Rime,
                Shape = FieldShape.FacingTile(), Effect = FieldEffectKind.Freeze,
                SetsTerrain = "ice",
            };
            var water = new Interactable
            {
                Id = "pool",
                Position = new Vec2(2, 1),
                Kind = InteractableKind.Freezable,
                RespondsTo = { "glaciate" },
                BecomesElevation = 1,
            };
            var world = World(water);
            world.Grid.Patch(2, 1, solid: true, terrain: "water");

            var outcome = FieldAbilities.Use(glaciate, new FieldUseContext(world, new Vec2(2, 2), Facing.North));
            Assert.True(outcome.Applied);

            var tile = world.Grid.At(2, 1)!;
            Assert.False(tile.Solid);
            Assert.Equal(1, tile.Elevation);
            Assert.Equal("ice", tile.Terrain);
            Assert.True(world.Grid.CanStep(new Vec2(2, 2), new Vec2(2, 1)));
        }

        [Fact]
        public void LiftsAnObjectToAChosenDestination()
        {
            var heave = new FieldAbilityDef
            {
                Id = "heave", Name = "Heave", Element = Element.Terra,
                Shape = FieldShape.FacingTile(), Effect = FieldEffectKind.Lift,
            };
            var rock = new Interactable
            {
                Id = "rock", Position = new Vec2(2, 1),
                Kind = InteractableKind.Liftable, RespondsTo = { "heave" },
            };
            var world = World(rock);
            var ctx = new FieldUseContext(world, new Vec2(2, 2), Facing.North) { Destination = new Vec2(4, 4) };

            Assert.True(FieldAbilities.Use(heave, ctx).Applied);
            Assert.Equal(new Vec2(4, 4), rock.Position);
        }

        [Fact]
        public void RaisesBareTerrainWhenNoObjectIsPresent()
        {
            var updraft = new FieldAbilityDef
            {
                Id = "updraft", Name = "Updraft", Element = Element.Aeris,
                Shape = FieldShape.FacingTile(), Effect = FieldEffectKind.Raise,
                ElevationDelta = 1,
            };
            var world = World();
            var outcome = FieldAbilities.Use(updraft, new FieldUseContext(world, new Vec2(2, 2), Facing.North));
            Assert.True(outcome.Applied);
            Assert.Equal(1, world.Grid.At(2, 1)!.Elevation);
        }

        [Fact]
        public void TogglesASwitchAndClearsBlockingWhenAsked()
        {
            var toggle = new FieldAbilityDef
            {
                Id = "pulse", Name = "Pulse", Element = Element.Aeris,
                Shape = FieldShape.FacingTile(), Effect = FieldEffectKind.Toggle,
            };
            var gate = new Interactable
            {
                Id = "gate", Position = new Vec2(2, 1),
                Kind = InteractableKind.Switch, RespondsTo = { "pulse" },
                Blocking = true, ClearsBlocking = true,
            };
            var world = World(gate);

            Assert.True(FieldAbilities.Use(toggle, new FieldUseContext(world, new Vec2(2, 2), Facing.North)).Applied);
            Assert.Equal("on", gate.State);
            Assert.False(gate.Blocking);

            // A switch is not one-shot: toggling again closes it.
            Assert.True(FieldAbilities.Use(toggle, new FieldUseContext(world, new Vec2(2, 2), Facing.North)).Applied);
            Assert.Equal("off", gate.State);
            Assert.True(gate.Blocking);
        }

        [Fact]
        public void AbilityAvailabilityMergesMoteAndArtSourcesWithoutDuplicates()
        {
            var available = FieldAbilities.Available(
                new string?[] { "heave", null, "glaciate" },
                new string?[] { "glaciate", "kindle", null });
            Assert.Equal(new[] { "heave", "glaciate", "kindle" }, available);
        }
    }
}
