using System;

namespace Aetherlight.Presentation
{
    public struct Vec3
    {
        public double X;
        public double Y;
        /// <summary>Elevation in tile units.</summary>
        public double Z;

        public Vec3(double x, double y, double z = 0)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public override string ToString() => $"({X}, {Y}, {Z})";
    }

    public struct ScreenPoint
    {
        public double X;
        public double Y;
        /// <summary>Painter's-algorithm sort key. Higher draws later (in front).</summary>
        public double Depth;
    }

    public sealed class CameraConfig
    {
        /// <summary>Half-width of a tile in screen pixels.</summary>
        public double TileHalfWidth = 32;

        /// <summary>Half-height of a tile. Lower means a flatter angle.</summary>
        public double TileHalfHeight = 16;

        /// <summary>Screen pixels one unit of elevation raises a point.</summary>
        public double ElevationUnit = 20;

        /// <summary>Camera focus in world tile coordinates.</summary>
        public Vec3 Focus;

        public double ViewportWidth;
        public double ViewportHeight;

        /// <summary>Rendering scale. Keep this an integer to preserve crisp pixels.</summary>
        public double Zoom = 1;

        /// <summary>
        /// Vertical framing offset in screen pixels, applied after projection.
        ///
        /// Sprites are drawn upward from their ground point, so a scene centred
        /// on the ground plane sits high in the frame. A positive offset pushes
        /// the world down to rebalance it, and it is also how you leave room for
        /// a UI bar without moving the focus off the action.
        /// </summary>
        public double OffsetY;

        public static CameraConfig Default(double viewportWidth, double viewportHeight) => new CameraConfig
        {
            ViewportWidth = viewportWidth,
            ViewportHeight = viewportHeight,
        };

        public CameraConfig Clone() => new CameraConfig
        {
            TileHalfWidth = TileHalfWidth,
            TileHalfHeight = TileHalfHeight,
            ElevationUnit = ElevationUnit,
            Focus = Focus,
            ViewportWidth = ViewportWidth,
            ViewportHeight = ViewportHeight,
            Zoom = Zoom,
            OffsetY = OffsetY,
        };
    }

    /// <summary>
    /// 2.5D projection.
    ///
    /// The rules layer stores a flat grid plus an elevation per tile. This
    /// turns (x, y, z) into screen coordinates and a depth key. Pure maths with
    /// no engine types, so a Unity renderer reuses it verbatim.
    ///
    /// The projection is dimetric: the world is rotated 45 degrees and squashed
    /// vertically, then elevation is subtracted from screen Y. That last part is
    /// the whole trick - height in the world is simply "further up the screen",
    /// and the depth key keeps the draw order correct.
    /// </summary>
    public static class Camera
    {
        public static ScreenPoint Project(Vec3 world, CameraConfig camera)
        {
            double dx = world.X - camera.Focus.X;
            double dy = world.Y - camera.Focus.Y;
            double dz = world.Z - camera.Focus.Z;

            double screenX = (dx - dy) * camera.TileHalfWidth * camera.Zoom;
            double screenY = ((dx + dy) * camera.TileHalfHeight - dz * camera.ElevationUnit) * camera.Zoom;

            return new ScreenPoint
            {
                X = screenX + camera.ViewportWidth / 2,
                Y = screenY + camera.ViewportHeight / 2 + camera.OffsetY,
                // Ties on (x + y) break by elevation, so higher objects draw in front.
                Depth = (world.X + world.Y) * 16 + world.Z,
            };
        }

        /// <summary>Screen pixels back to world tile coordinates on an elevation plane.</summary>
        public static Vec3 Unproject(double screenX, double screenY, CameraConfig camera, double planeZ = 0)
        {
            double localX = (screenX - camera.ViewportWidth / 2) / camera.Zoom;
            double localY = (screenY - camera.ViewportHeight / 2 - camera.OffsetY) / camera.Zoom
                            + planeZ * camera.ElevationUnit;

            double a = localX / camera.TileHalfWidth;
            double b = localY / camera.TileHalfHeight;

            return new Vec3((b + a) / 2 + camera.Focus.X, (b - a) / 2 + camera.Focus.Y, planeZ);
        }

        /// <summary>
        /// Snap to whole pixels. Sub-pixel sprite positions are what make pixel
        /// art shimmer under a moving camera, so everything drawn passes here.
        /// </summary>
        public static ScreenPoint SnapToPixel(ScreenPoint point) => new ScreenPoint
        {
            X = Math.Round(point.X, MidpointRounding.AwayFromZero),
            Y = Math.Round(point.Y, MidpointRounding.AwayFromZero),
            Depth = point.Depth,
        };

        /// <summary>Move the camera toward a target, framerate-independent.</summary>
        public static CameraConfig Follow(CameraConfig camera, Vec3 target, double smoothing, double dtMs)
        {
            // Exponential smoothing expressed so the result does not depend on
            // how often it is called.
            double factor = 1 - Math.Pow(1 - smoothing, dtMs / 16.667);
            var moved = camera.Clone();
            moved.Focus = new Vec3(
                camera.Focus.X + (target.X - camera.Focus.X) * factor,
                camera.Focus.Y + (target.Y - camera.Focus.Y) * factor,
                camera.Focus.Z + (target.Z - camera.Focus.Z) * factor);
            return moved;
        }

        public static bool IsVisible(ScreenPoint point, CameraConfig camera, double margin = 64) =>
            point.X >= -margin
            && point.Y >= -margin
            && point.X <= camera.ViewportWidth + margin
            && point.Y <= camera.ViewportHeight + margin;
    }
}
