/**
 * Field abilities: the overworld half of the toolkit.
 *
 * These are the abilities that move a rock, freeze a puddle into a step, or
 * reveal something invisible - the ones that turn the map itself into a
 * puzzle. The rules layer resolves *what changed*; it never animates. The
 * returned `FieldOutcome` is a description the renderer replays.
 *
 * An ability reaches its targets through a shape (the tile in front of you, a
 * radius, a line) and then applies its effect to whichever interactables in
 * that shape declared they listen for it.
 */

import type { GameEvent } from '../core/events.js';
import type { Element } from '../domain/elements.js';
import { manhattan, translate, type Facing, type Grid, type Vec2 } from './grid.js';
import { respondsTo, type Interactable } from './interactables.js';

export type FieldShape =
  /** The single tile the user is facing. */
  | { kind: 'facing'; distance?: number }
  /** Every tile within a radius of the user. */
  | { kind: 'radius'; radius: number }
  /** A straight line ahead, stopping at the first solid tile. */
  | { kind: 'line'; length: number }
  /** The user's own tile. */
  | { kind: 'self' };

export type FieldEffectKind =
  /** Move a liftable object to a chosen tile. */
  | 'lift'
  /** Shove a pushable one tile away from the user. */
  | 'push'
  /** Flip a switch or toggle state. */
  | 'toggle'
  /** Raise the tile's elevation, creating a step. */
  | 'raise'
  /** Lower the tile's elevation. */
  | 'lower'
  /** Make hidden things visible. */
  | 'reveal'
  /** Consume a burnable. */
  | 'burn'
  /** Turn a freezable into standable terrain. */
  | 'freeze'
  /** Grow a growable into a climbable surface. */
  | 'grow';

export interface FieldAbilityDef {
  id: string;
  name: string;
  element: Element;
  description?: string;
  /** Aether cost when used on the field. Zero for free tools. */
  cost?: number;
  shape: FieldShape;
  effect: FieldEffectKind;
  /** Elevation delta for raise/lower. */
  elevationDelta?: number;
  /** Terrain the affected tile becomes. */
  setsTerrain?: string;
}

export interface FieldWorld {
  grid: Grid;
  interactables: Interactable[];
}

export interface FieldChange {
  interactableId?: string;
  position: Vec2;
  /** What changed, for the renderer to animate. */
  change: 'moved' | 'state' | 'elevation' | 'revealed' | 'removed' | 'terrain';
  from?: string | number | Vec2;
  to?: string | number | Vec2;
}

export interface FieldOutcome {
  /** False when nothing in range responded; the UI should say so. */
  applied: boolean;
  changes: FieldChange[];
  events: GameEvent[];
  /** Set when the ability could not be used at all. */
  reason?: string;
}

export interface FieldUseContext {
  world: FieldWorld;
  origin: Vec2;
  facing: Facing;
  /** Destination tile for `lift`, ignored by other effects. */
  destination?: Vec2;
}

/** Tiles an ability reaches, in resolution order. */
export function tilesInShape(shape: FieldShape, origin: Vec2, facing: Facing, grid: Grid): Vec2[] {
  switch (shape.kind) {
    case 'self':
      return [origin];
    case 'facing':
      return [translate(origin, facing, shape.distance ?? 1)];
    case 'line': {
      const tiles: Vec2[] = [];
      for (let step = 1; step <= shape.length; step++) {
        const position = translate(origin, facing, step);
        const tile = grid.at(position.x, position.y);
        if (!tile) break;
        tiles.push(position);
        if (tile.solid) break;
      }
      return tiles;
    }
    case 'radius': {
      const tiles: Vec2[] = [];
      for (let y = origin.y - shape.radius; y <= origin.y + shape.radius; y++) {
        for (let x = origin.x - shape.radius; x <= origin.x + shape.radius; x++) {
          if (!grid.inBounds(x, y)) continue;
          if (manhattan(origin, { x, y }) > shape.radius) continue;
          tiles.push({ x, y });
        }
      }
      return tiles;
    }
  }
}

/**
 * Resolve a field ability. Mutates the world and returns a description of
 * every change so the presentation layer can play it back.
 */
export function useFieldAbility(def: FieldAbilityDef, ctx: FieldUseContext): FieldOutcome {
  const { world, origin, facing } = ctx;
  const tiles = tilesInShape(def.shape, origin, facing, world.grid);
  const changes: FieldChange[] = [];
  const touched: string[] = [];

  for (const tile of tiles) {
    const targets = world.interactables.filter(
      (item) => item.position.x === tile.x && item.position.y === tile.y && respondsTo(item, def.id),
    );

    for (const target of targets) {
      const change = applyEffect(def, target, ctx, tile);
      if (change) {
        changes.push(...change);
        touched.push(target.id);
      }
    }

    // Raise and lower reshape terrain directly, with or without an object.
    if ((def.effect === 'raise' || def.effect === 'lower') && targets.length === 0) {
      const existing = world.grid.at(tile.x, tile.y);
      if (existing && def.elevationDelta) {
        const delta = def.effect === 'raise' ? def.elevationDelta : -def.elevationDelta;
        world.grid.patch(tile.x, tile.y, { elevation: existing.elevation + delta });
        changes.push({
          position: tile,
          change: 'elevation',
          from: existing.elevation,
          to: existing.elevation + delta,
        });
      }
    }
  }

  const applied = changes.length > 0;
  const events: GameEvent[] = applied
    ? [{ type: 'field-effect', abilityId: def.id, originX: origin.x, originY: origin.y, targetIds: touched }]
    : [];

  return applied ? { applied, changes, events } : { applied: false, changes, events, reason: 'nothing-responded' };
}

function applyEffect(
  def: FieldAbilityDef,
  target: Interactable,
  ctx: FieldUseContext,
  tile: Vec2,
): FieldChange[] | null {
  const { world, origin, facing } = ctx;

  switch (def.effect) {
    case 'lift': {
      const destination = ctx.destination;
      if (!destination) return null;
      const tileAt = world.grid.at(destination.x, destination.y);
      if (!tileAt || tileAt.solid) return null;
      const from = { ...target.position };
      target.position = { x: destination.x, y: destination.y };
      return [{ interactableId: target.id, position: destination, change: 'moved', from, to: destination }];
    }
    case 'push': {
      const destination = translate(tile, facing, 1);
      const tileAt = world.grid.at(destination.x, destination.y);
      if (!tileAt || tileAt.solid) return null;
      const blocked = world.interactables.some(
        (item) => item.id !== target.id && item.blocking && item.position.x === destination.x && item.position.y === destination.y,
      );
      if (blocked) return null;
      const from = { ...target.position };
      target.position = destination;
      return [{ interactableId: target.id, position: destination, change: 'moved', from, to: destination }];
    }
    case 'toggle': {
      const from = target.state ?? 'off';
      target.state = from === 'on' ? 'off' : 'on';
      const changes: FieldChange[] = [
        { interactableId: target.id, position: tile, change: 'state', from, to: target.state },
      ];
      if (target.clearsBlocking) {
        target.blocking = target.state !== 'on';
      }
      return changes;
    }
    case 'reveal': {
      if (target.kind !== 'hidden' && target.kind !== 'inert') return null;
      target.state = 'revealed';
      return [{ interactableId: target.id, position: tile, change: 'revealed', to: 'revealed' }];
    }
    case 'burn': {
      target.spent = true;
      target.blocking = false;
      const changes: FieldChange[] = [{ interactableId: target.id, position: tile, change: 'removed' }];
      if (def.setsTerrain) {
        world.grid.patch(tile.x, tile.y, { terrain: def.setsTerrain, solid: false });
        changes.push({ position: tile, change: 'terrain', to: def.setsTerrain });
      }
      return changes;
    }
    case 'freeze':
    case 'grow':
    case 'raise':
    case 'lower': {
      const existing = world.grid.at(tile.x, tile.y);
      if (!existing) return null;
      const changes: FieldChange[] = [];

      const targetElevation =
        target.becomesElevation ??
        (def.elevationDelta !== undefined
          ? existing.elevation + (def.effect === 'lower' ? -def.elevationDelta : def.elevationDelta)
          : undefined);

      if (targetElevation !== undefined) {
        world.grid.patch(tile.x, tile.y, { elevation: targetElevation, solid: false });
        changes.push({ position: tile, change: 'elevation', from: existing.elevation, to: targetElevation });
      }

      const terrain = target.becomesTerrain ?? def.setsTerrain;
      if (terrain) {
        world.grid.patch(tile.x, tile.y, { terrain });
        changes.push({ position: tile, change: 'terrain', from: existing.terrain, to: terrain });
      }

      const from = target.state ?? 'idle';
      target.state = def.effect;
      target.spent = true;
      changes.push({ interactableId: target.id, position: tile, change: 'state', from, to: def.effect });
      return changes.length > 0 ? changes : null;
    }
  }
}

/** Field abilities available to a party, from set motes and known arts. */
export function availableFieldAbilities(
  moteFieldAbilityIds: readonly (string | undefined)[],
  artFieldEffectIds: readonly (string | undefined)[],
): string[] {
  const ids = new Set<string>();
  for (const id of moteFieldAbilityIds) if (id) ids.add(id);
  for (const id of artFieldEffectIds) if (id) ids.add(id);
  return [...ids];
}
