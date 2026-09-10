using UnityEngine;

namespace Aetherlight.Unity
{
    /// <summary>
    /// Tile coordinates to Unity world space.
    ///
    /// The rules layer stores a flat grid plus an elevation per tile. In Unity
    /// that maps straight onto three axes and the engine's own camera does the
    /// projection - which is why this package does not use the core's
    /// Presentation.Camera. That module exists for backends which must project
    /// to screen space themselves, such as a 2D canvas. Here, elevation is
    /// simply height, and depth sorting falls out of the camera.
    /// </summary>
    public static class IsoPlacement
    {
        /// <summary>
        /// world = (tileX * tileSize, elevation * heightUnit, tileY * tileSize)
        ///
        /// heightUnit is usually a fraction of tileSize - about half reads
        /// clearly without dwarfing the sprites.
        /// </summary>
        public static Vector3 World(double tileX, double tileY, double elevation, float tileSize, float heightUnit) =>
            new Vector3((float)tileX * tileSize, (float)elevation * heightUnit, (float)tileY * tileSize);

        /// <summary>
        /// Snap a world position to a virtual pixel grid.
        ///
        /// Sub-pixel sprite placement under a moving camera is what makes pixel
        /// art crawl. Call this on anything that holds still relative to the
        /// ground; skip it for things deliberately in motion, where snapping
        /// reads as stutter instead.
        /// </summary>
        public static Vector3 SnapToPixelGrid(Vector3 world, float pixelsPerUnit)
        {
            if (pixelsPerUnit <= 0) return world;
            return new Vector3(
                Mathf.Round(world.x * pixelsPerUnit) / pixelsPerUnit,
                Mathf.Round(world.y * pixelsPerUnit) / pixelsPerUnit,
                Mathf.Round(world.z * pixelsPerUnit) / pixelsPerUnit);
        }
    }
}
