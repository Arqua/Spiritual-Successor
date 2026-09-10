using System;
using System.Collections.Generic;

namespace Aetherlight.Field
{
    public struct Vec2 : IEquatable<Vec2>
    {
        public int X;
        public int Y;

        public Vec2(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(Vec2 other) => X == other.X && Y == other.Y;
        public override bool Equals(object? obj) => obj is Vec2 other && Equals(other);
        public override int GetHashCode() => unchecked(X * 397 ^ Y);
        public override string ToString() => $"({X}, {Y})";

        public static bool operator ==(Vec2 a, Vec2 b) => a.Equals(b);
        public static bool operator !=(Vec2 a, Vec2 b) => !a.Equals(b);
    }

    public enum Facing { North, South, East, West }

    public sealed class Tile
    {
        /// <summary>Blocks movement outright regardless of height.</summary>
        public bool Solid;

        /// <summary>Height in tile units.</summary>
        public double Elevation;

        /// <summary>Free-form terrain tag the renderer and abilities key off.</summary>
        public string? Terrain;

        /// <summary>Blocks line of sight for targeting and vision cones.</summary>
        public bool Opaque;

        public Tile Clone() => new Tile { Solid = Solid, Elevation = Elevation, Terrain = Terrain, Opaque = Opaque };
    }

    /// <summary>
    /// The field grid.
    ///
    /// Tiles carry an elevation as well as walkability. That single extra
    /// number is what lets a 2.5D renderer stack a map into layers - a bridge
    /// over a river, a ledge you can drop off but not climb - while the rules
    /// layer stays a flat 2D array. Movement between tiles is allowed when the
    /// height difference is within StepHeight; dropping further is allowed but
    /// climbing is not, which is the classic ledge rule and costs nothing to
    /// express.
    /// </summary>
    public sealed class Grid
    {
        public int Width { get; }
        public int Height { get; }

        /// <summary>Maximum climbable height difference between adjacent tiles.</summary>
        public double StepHeight { get; }

        /// <summary>Maximum drop before movement is blocked. Infinity allows any fall.</summary>
        public double MaxDrop { get; }

        private readonly Tile[] _tiles;

        public Grid(int width, int height, IReadOnlyList<Tile>? tiles = null, double stepHeight = 1, double maxDrop = double.PositiveInfinity)
        {
            Width = width;
            Height = height;
            StepHeight = stepHeight;
            MaxDrop = maxDrop;

            if (tiles == null)
            {
                _tiles = new Tile[width * height];
                for (int i = 0; i < _tiles.Length; i++) _tiles[i] = new Tile();
            }
            else
            {
                if (tiles.Count != width * height)
                    throw new ArgumentException($"Grid expects {width * height} tiles, received {tiles.Count}", nameof(tiles));
                _tiles = new Tile[tiles.Count];
                for (int i = 0; i < tiles.Count; i++) _tiles[i] = tiles[i];
            }
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public Tile? At(int x, int y) => InBounds(x, y) ? _tiles[y * Width + x] : null;

        public Tile? At(Vec2 position) => At(position.X, position.Y);

        public void Set(int x, int y, Tile tile)
        {
            if (!InBounds(x, y)) return;
            _tiles[y * Width + x] = tile;
        }

        /// <summary>Mutate part of a tile, for abilities that reshape terrain.</summary>
        public void Patch(int x, int y, bool? solid = null, double? elevation = null, string? terrain = null, bool? opaque = null)
        {
            var tile = At(x, y);
            if (tile == null) return;
            if (solid.HasValue) tile.Solid = solid.Value;
            if (elevation.HasValue) tile.Elevation = elevation.Value;
            if (terrain != null) tile.Terrain = terrain;
            if (opaque.HasValue) tile.Opaque = opaque.Value;
        }

        /// <summary>
        /// Whether an actor standing on <paramref name="from"/> may step onto
        /// <paramref name="to"/>. Climbing is capped by StepHeight; falling is
        /// capped by MaxDrop.
        /// </summary>
        public bool CanStep(Vec2 from, Vec2 to)
        {
            var source = At(from);
            var target = At(to);
            if (source == null || target == null || target.Solid) return false;

            double delta = target.Elevation - source.Elevation;
            if (delta > StepHeight) return false;
            if (-delta > MaxDrop) return false;
            return true;
        }

        /// <summary>Straight-line visibility, for targeting and NPC sight cones.</summary>
        public bool HasLineOfSight(Vec2 from, Vec2 to)
        {
            foreach (var point in Bresenham(from, to))
            {
                if (point == from || point == to) continue;
                var tile = At(point);
                if (tile == null || tile.Solid || tile.Opaque) return false;
            }
            return true;
        }

        /// <summary>Serializable form, for save files and shipping maps as JSON.</summary>
        public GridData ToData()
        {
            var data = new GridData
            {
                Width = Width,
                Height = Height,
                StepHeight = StepHeight,
                // JSON has no infinity, so the sentinel travels as -1.
                MaxDrop = double.IsPositiveInfinity(MaxDrop) ? -1 : MaxDrop,
            };
            foreach (var tile in _tiles) data.Tiles.Add(tile.Clone());
            return data;
        }

        public static Grid FromData(GridData data) => new Grid(
            data.Width,
            data.Height,
            data.Tiles,
            data.StepHeight,
            data.MaxDrop < 0 ? double.PositiveInfinity : data.MaxDrop);

        // --- free functions --------------------------------------------------

        public static readonly IReadOnlyDictionary<Facing, Vec2> FacingVectors = new Dictionary<Facing, Vec2>
        {
            [Facing.North] = new Vec2(0, -1),
            [Facing.South] = new Vec2(0, 1),
            [Facing.East] = new Vec2(1, 0),
            [Facing.West] = new Vec2(-1, 0),
        };

        public static Vec2 Translate(Vec2 position, Facing facing, int distance = 1)
        {
            var vector = FacingVectors[facing];
            return new Vec2(position.X + vector.X * distance, position.Y + vector.Y * distance);
        }

        public static int Manhattan(Vec2 a, Vec2 b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

        /// <summary>Integer line walk, inclusive of both endpoints.</summary>
        public static List<Vec2> Bresenham(Vec2 from, Vec2 to)
        {
            var points = new List<Vec2>();
            int x0 = from.X;
            int y0 = from.Y;
            int dx = Math.Abs(to.X - x0);
            int dy = -Math.Abs(to.Y - y0);
            int sx = x0 < to.X ? 1 : -1;
            int sy = y0 < to.Y ? 1 : -1;
            int error = dx + dy;

            while (true)
            {
                points.Add(new Vec2(x0, y0));
                if (x0 == to.X && y0 == to.Y) break;
                int doubled = 2 * error;
                if (doubled >= dy)
                {
                    error += dy;
                    x0 += sx;
                }
                if (doubled <= dx)
                {
                    error += dx;
                    y0 += sy;
                }
            }
            return points;
        }
    }

    public sealed class GridData
    {
        public int Width;
        public int Height;
        public double StepHeight = 1;
        /// <summary>-1 stands in for "no limit", since JSON has no infinity.</summary>
        public double MaxDrop = -1;
        public List<Tile> Tiles = new List<Tile>();
    }
}
