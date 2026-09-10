/**
 * 2.5D projection.
 *
 * The rules layer stores a flat grid plus an `elevation` per tile. This module
 * turns (tileX, tileY, elevation) into screen coordinates and a depth key.
 * It is pure maths with no canvas or engine types, so a Godot or Unity port
 * reuses it verbatim.
 *
 * The projection is dimetric: the world is rotated 45 degrees and squashed
 * vertically, which is the classic isometric pixel-art reading, then elevation
 * is subtracted from the screen Y. That last part is the whole trick - height
 * in the world is simply "further up the screen", and the depth key keeps the
 * draw order correct.
 */

export interface Vec3 {
  x: number;
  y: number;
  /** Elevation in tile units. */
  z: number;
}

export interface ScreenPoint {
  x: number;
  y: number;
  /** Painter's-algorithm sort key. Higher draws later (in front). */
  depth: number;
}

export interface CameraConfig {
  /** Half-width of a tile in screen pixels. */
  tileHalfWidth: number;
  /** Half-height of a tile in screen pixels. Lower means a flatter angle. */
  tileHalfHeight: number;
  /** Screen pixels one unit of elevation raises a point. */
  elevationUnit: number;
  /** Camera focus in world tile coordinates. */
  focus: Vec3;
  /** Viewport size in pixels. */
  viewportWidth: number;
  viewportHeight: number;
  /** Rendering scale. Keep this an integer to preserve crisp pixels. */
  zoom: number;
  /**
   * Vertical framing offset in screen pixels, applied after projection.
   *
   * Sprites are drawn upward from their ground point, so a scene centred on
   * the ground plane sits high in the frame. A positive offset pushes the
   * world down to rebalance it, and it is also how you leave room for a UI
   * bar without moving the camera focus off the action.
   */
  offsetY?: number;
}

export function defaultCamera(viewportWidth: number, viewportHeight: number): CameraConfig {
  return {
    tileHalfWidth: 32,
    tileHalfHeight: 16,
    elevationUnit: 20,
    focus: { x: 0, y: 0, z: 0 },
    viewportWidth,
    viewportHeight,
    zoom: 1,
    offsetY: 0,
  };
}

/**
 * World tile coordinates to screen pixels.
 *
 * Depth is computed from the world position rather than the screen position so
 * that a tall object on a low tile still sorts behind a short object on a high
 * tile when it should.
 */
export function project(world: Vec3, camera: CameraConfig): ScreenPoint {
  const dx = world.x - camera.focus.x;
  const dy = world.y - camera.focus.y;
  const dz = world.z - camera.focus.z;

  const screenX = (dx - dy) * camera.tileHalfWidth * camera.zoom;
  const screenY = ((dx + dy) * camera.tileHalfHeight - dz * camera.elevationUnit) * camera.zoom;

  return {
    x: screenX + camera.viewportWidth / 2,
    y: screenY + camera.viewportHeight / 2 + (camera.offsetY ?? 0),
    // Ties on (x+y) break by elevation, so higher objects draw in front.
    depth: (world.x + world.y) * 16 + world.z,
  };
}

/** Screen pixels back to world tile coordinates on a given elevation plane. */
export function unproject(screenX: number, screenY: number, camera: CameraConfig, planeZ = 0): Vec3 {
  const localX = (screenX - camera.viewportWidth / 2) / camera.zoom;
  const localY =
    (screenY - camera.viewportHeight / 2 - (camera.offsetY ?? 0)) / camera.zoom + planeZ * camera.elevationUnit;

  const a = localX / camera.tileHalfWidth;
  const b = localY / camera.tileHalfHeight;

  return {
    x: (b + a) / 2 + camera.focus.x,
    y: (b - a) / 2 + camera.focus.y,
    z: planeZ,
  };
}

/**
 * Snap a screen point to whole pixels.
 *
 * Sub-pixel sprite positions are the cause of the shimmering that ruins
 * pixel art under a moving camera. Everything drawn should pass through here.
 */
export function snapToPixel(point: ScreenPoint): ScreenPoint {
  return { x: Math.round(point.x), y: Math.round(point.y), depth: point.depth };
}

/** Move the camera toward a target, framerate-independent. */
export function followCamera(camera: CameraConfig, target: Vec3, smoothing: number, dtMs: number): CameraConfig {
  // Exponential smoothing expressed so the result is independent of framerate.
  const factor = 1 - Math.pow(1 - smoothing, dtMs / 16.667);
  return {
    ...camera,
    focus: {
      x: camera.focus.x + (target.x - camera.focus.x) * factor,
      y: camera.focus.y + (target.y - camera.focus.y) * factor,
      z: camera.focus.z + (target.z - camera.focus.z) * factor,
    },
  };
}

/** Whether a projected point is inside the viewport, with a margin. */
export function isVisible(point: ScreenPoint, camera: CameraConfig, margin = 64): boolean {
  return (
    point.x >= -margin &&
    point.y >= -margin &&
    point.x <= camera.viewportWidth + margin &&
    point.y <= camera.viewportHeight + margin
  );
}
