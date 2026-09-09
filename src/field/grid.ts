/**
 * The field grid.
 *
 * Tiles carry an `elevation` as well as walkability. That single extra number
 * is what lets a 2.5D renderer stack a map into layers - a bridge over a
 * river, a ledge you can drop off but not climb - while the rules layer stays
 * a flat 2D array. Movement between tiles is allowed when the height
 * difference is within `stepHeight`; dropping further is allowed but climbing
 * is not, which is the classic ledge rule and costs nothing to express.
 */

export interface Tile {
  /** Blocks movement outright regardless of height. */
  solid: boolean;
  /** Height in tile units. */
  elevation: number;
  /** Free-form terrain tag the renderer and abilities can key off. */
  terrain?: string;
  /** Blocks line of sight for targeting and vision cones. */
  opaque?: boolean;
}

export interface Vec2 {
  x: number;
  y: number;
}

export type Facing = 'north' | 'south' | 'east' | 'west';

export const FACING_VECTORS: Readonly<Record<Facing, Vec2>> = Object.freeze({
  north: { x: 0, y: -1 },
  south: { x: 0, y: 1 },
  east: { x: 1, y: 0 },
  west: { x: -1, y: 0 },
});

export interface GridOptions {
  /** Maximum climbable height difference between adjacent tiles. */
  stepHeight?: number;
  /** Maximum drop before movement is blocked. Infinity allows any fall. */
  maxDrop?: number;
}

export class Grid {
  readonly width: number;
  readonly height: number;
  readonly stepHeight: number;
  readonly maxDrop: number;
  private readonly tiles: Tile[];

  constructor(width: number, height: number, tiles?: Tile[], options: GridOptions = {}) {
    this.width = width;
    this.height = height;
    this.stepHeight = options.stepHeight ?? 1;
    this.maxDrop = options.maxDrop ?? Number.POSITIVE_INFINITY;
    this.tiles =
      tiles ?? Array.from({ length: width * height }, () => ({ solid: false, elevation: 0 }) satisfies Tile);
    if (this.tiles.length !== width * height) {
      throw new Error(`Grid expects ${width * height} tiles, received ${this.tiles.length}`);
    }
  }

  inBounds(x: number, y: number): boolean {
    return x >= 0 && y >= 0 && x < this.width && y < this.height;
  }

  at(x: number, y: number): Tile | undefined {
    if (!this.inBounds(x, y)) return undefined;
    return this.tiles[y * this.width + x];
  }

  set(x: number, y: number, tile: Tile): void {
    if (!this.inBounds(x, y)) return;
    this.tiles[y * this.width + x] = tile;
  }

  /** Mutate one field of a tile, for abilities that raise or lower terrain. */
  patch(x: number, y: number, patch: Partial<Tile>): void {
    const tile = this.at(x, y);
    if (!tile) return;
    this.set(x, y, { ...tile, ...patch });
  }

  /**
   * Whether an actor standing on `from` may step onto `to`. Climbing is capped
   * by `stepHeight`; falling is capped by `maxDrop`.
   */
  canStep(from: Vec2, to: Vec2): boolean {
    const source = this.at(from.x, from.y);
    const target = this.at(to.x, to.y);
    if (!source || !target || target.solid) return false;
    const delta = target.elevation - source.elevation;
    if (delta > this.stepHeight) return false;
    if (-delta > this.maxDrop) return false;
    return true;
  }

  /** Straight-line visibility, used for targeting and NPC sight cones. */
  hasLineOfSight(from: Vec2, to: Vec2): boolean {
    for (const point of bresenham(from, to)) {
      if (point.x === from.x && point.y === from.y) continue;
      if (point.x === to.x && point.y === to.y) continue;
      const tile = this.at(point.x, point.y);
      if (!tile || tile.solid || tile.opaque) return false;
    }
    return true;
  }

  /** Serializable form, for save files and for shipping maps as JSON. */
  toJSON(): { width: number; height: number; stepHeight: number; maxDrop: number; tiles: Tile[] } {
    return {
      width: this.width,
      height: this.height,
      stepHeight: this.stepHeight,
      maxDrop: this.maxDrop === Number.POSITIVE_INFINITY ? -1 : this.maxDrop,
      tiles: this.tiles.map((tile) => ({ ...tile })),
    };
  }

  static fromJSON(data: ReturnType<Grid['toJSON']>): Grid {
    return new Grid(data.width, data.height, data.tiles, {
      stepHeight: data.stepHeight,
      maxDrop: data.maxDrop < 0 ? Number.POSITIVE_INFINITY : data.maxDrop,
    });
  }
}

export function translate(position: Vec2, facing: Facing, distance = 1): Vec2 {
  const vector = FACING_VECTORS[facing];
  return { x: position.x + vector.x * distance, y: position.y + vector.y * distance };
}

export function manhattan(a: Vec2, b: Vec2): number {
  return Math.abs(a.x - b.x) + Math.abs(a.y - b.y);
}

export function samePosition(a: Vec2, b: Vec2): boolean {
  return a.x === b.x && a.y === b.y;
}

/** Integer line walk, inclusive of both endpoints. */
export function bresenham(from: Vec2, to: Vec2): Vec2[] {
  const points: Vec2[] = [];
  let x0 = from.x;
  let y0 = from.y;
  const dx = Math.abs(to.x - x0);
  const dy = -Math.abs(to.y - y0);
  const sx = x0 < to.x ? 1 : -1;
  const sy = y0 < to.y ? 1 : -1;
  let error = dx + dy;

  for (;;) {
    points.push({ x: x0, y: y0 });
    if (x0 === to.x && y0 === to.y) break;
    const doubled = 2 * error;
    if (doubled >= dy) {
      error += dy;
      x0 += sx;
    }
    if (doubled <= dx) {
      error += dx;
      y0 += sy;
    }
  }
  return points;
}
