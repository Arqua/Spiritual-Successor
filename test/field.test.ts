import { describe, expect, it } from 'vitest';
import { Grid, bresenham, manhattan, translate, type Tile } from '../src/field/grid.js';
import { useFieldAbility, tilesInShape, type FieldAbilityDef, type FieldWorld } from '../src/field/abilities.js';
import type { Interactable } from '../src/field/interactables.js';

function flatGrid(width = 6, height = 6, elevation = 0): Grid {
  const tiles: Tile[] = Array.from({ length: width * height }, () => ({ solid: false, elevation }));
  return new Grid(width, height, tiles, { stepHeight: 1 });
}

describe('grid', () => {
  it('reports bounds correctly', () => {
    const grid = flatGrid(4, 3);
    expect(grid.inBounds(0, 0)).toBe(true);
    expect(grid.inBounds(3, 2)).toBe(true);
    expect(grid.inBounds(4, 2)).toBe(false);
    expect(grid.at(9, 9)).toBeUndefined();
  });

  it('blocks movement into solid tiles', () => {
    const grid = flatGrid();
    grid.patch(1, 0, { solid: true });
    expect(grid.canStep({ x: 0, y: 0 }, { x: 1, y: 0 })).toBe(false);
  });

  it('allows a step up within stepHeight but not beyond it', () => {
    const grid = flatGrid();
    grid.patch(1, 0, { elevation: 1 });
    grid.patch(2, 0, { elevation: 3 });
    expect(grid.canStep({ x: 0, y: 0 }, { x: 1, y: 0 })).toBe(true);
    expect(grid.canStep({ x: 0, y: 0 }, { x: 2, y: 0 })).toBe(false);
  });

  it('allows dropping down further than it allows climbing', () => {
    const grid = flatGrid();
    grid.patch(0, 0, { elevation: 5 });
    // Down from 5 to 0 is fine; back up is not.
    expect(grid.canStep({ x: 0, y: 0 }, { x: 1, y: 0 })).toBe(true);
    expect(grid.canStep({ x: 1, y: 0 }, { x: 0, y: 0 })).toBe(false);
  });

  it('respects maxDrop when configured', () => {
    const grid = new Grid(3, 1, [
      { solid: false, elevation: 9 },
      { solid: false, elevation: 0 },
      { solid: false, elevation: 0 },
    ], { stepHeight: 1, maxDrop: 2 });
    expect(grid.canStep({ x: 0, y: 0 }, { x: 1, y: 0 })).toBe(false);
  });

  it('breaks line of sight on opaque tiles', () => {
    const grid = flatGrid();
    expect(grid.hasLineOfSight({ x: 0, y: 0 }, { x: 4, y: 0 })).toBe(true);
    grid.patch(2, 0, { opaque: true });
    expect(grid.hasLineOfSight({ x: 0, y: 0 }, { x: 4, y: 0 })).toBe(false);
  });

  it('round-trips through JSON', () => {
    const grid = flatGrid(3, 3);
    grid.patch(1, 1, { elevation: 2, terrain: 'ice' });
    const restored = Grid.fromJSON(grid.toJSON());
    expect(restored.at(1, 1)).toEqual({ solid: false, elevation: 2, terrain: 'ice' });
  });

  it('walks a straight line inclusive of both ends', () => {
    const line = bresenham({ x: 0, y: 0 }, { x: 3, y: 0 });
    expect(line).toEqual([{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 2, y: 0 }, { x: 3, y: 0 }]);
  });

  it('translates by facing', () => {
    expect(translate({ x: 2, y: 2 }, 'north')).toEqual({ x: 2, y: 1 });
    expect(translate({ x: 2, y: 2 }, 'east', 3)).toEqual({ x: 5, y: 2 });
    expect(manhattan({ x: 0, y: 0 }, { x: 2, y: 3 })).toBe(5);
  });
});

describe('field ability shapes', () => {
  const grid = flatGrid();

  it('targets the tile being faced', () => {
    expect(tilesInShape({ kind: 'facing' }, { x: 2, y: 2 }, 'north', grid)).toEqual([{ x: 2, y: 1 }]);
  });

  it('stops a line at the first solid tile', () => {
    const blocked = flatGrid();
    blocked.patch(2, 0, { solid: true });
    const tiles = tilesInShape({ kind: 'line', length: 5 }, { x: 0, y: 0 }, 'east', blocked);
    expect(tiles).toEqual([{ x: 1, y: 0 }, { x: 2, y: 0 }]);
  });

  it('covers a diamond for radius shapes', () => {
    const tiles = tilesInShape({ kind: 'radius', radius: 1 }, { x: 2, y: 2 }, 'north', grid);
    expect(tiles).toHaveLength(5);
  });
});

describe('field abilities', () => {
  function world(interactables: Interactable[]): FieldWorld {
    return { grid: flatGrid(), interactables };
  }

  const burn: FieldAbilityDef = {
    id: 'kindle',
    name: 'Kindle',
    element: 'pyre',
    shape: { kind: 'facing' },
    effect: 'burn',
    setsTerrain: 'ash',
  };

  it('burns away brush that listens for the ability', () => {
    const brush: Interactable = {
      id: 'brush-1',
      position: { x: 2, y: 1 },
      kind: 'burnable',
      respondsTo: ['kindle'],
      blocking: true,
    };
    const w = world([brush]);
    const outcome = useFieldAbility(burn, { world: w, origin: { x: 2, y: 2 }, facing: 'north' });

    expect(outcome.applied).toBe(true);
    expect(brush.spent).toBe(true);
    expect(brush.blocking).toBe(false);
    expect(w.grid.at(2, 1)?.terrain).toBe('ash');
    expect(outcome.events[0]).toMatchObject({ type: 'field-effect', abilityId: 'kindle' });
  });

  it('does nothing when the object does not listen for that ability', () => {
    const rock: Interactable = {
      id: 'rock',
      position: { x: 2, y: 1 },
      kind: 'liftable',
      respondsTo: ['heave'],
    };
    const outcome = useFieldAbility(burn, { world: world([rock]), origin: { x: 2, y: 2 }, facing: 'north' });
    expect(outcome.applied).toBe(false);
    expect(outcome.reason).toBe('nothing-responded');
  });

  it('does not re-trigger a spent object', () => {
    const brush: Interactable = {
      id: 'brush',
      position: { x: 2, y: 1 },
      kind: 'burnable',
      respondsTo: ['kindle'],
      spent: true,
    };
    expect(useFieldAbility(burn, { world: world([brush]), origin: { x: 2, y: 2 }, facing: 'north' }).applied).toBe(false);
  });

  it('pushes a block one tile and refuses to push it into a wall', () => {
    const push: FieldAbilityDef = {
      id: 'shove',
      name: 'Shove',
      element: 'terra',
      shape: { kind: 'facing' },
      effect: 'push',
    };
    const block: Interactable = {
      id: 'block',
      position: { x: 2, y: 1 },
      kind: 'pushable',
      respondsTo: ['shove'],
      blocking: true,
    };
    const w = world([block]);
    expect(useFieldAbility(push, { world: w, origin: { x: 2, y: 2 }, facing: 'north' }).applied).toBe(true);
    expect(block.position).toEqual({ x: 2, y: 0 });

    // Now it is against the north edge and cannot move further.
    expect(useFieldAbility(push, { world: w, origin: { x: 2, y: 1 }, facing: 'north' }).applied).toBe(false);
  });

  it('freezing water raises a standable platform', () => {
    const glaciate: FieldAbilityDef = {
      id: 'glaciate',
      name: 'Glaciate',
      element: 'rime',
      shape: { kind: 'facing' },
      effect: 'freeze',
      setsTerrain: 'ice',
    };
    const water: Interactable = {
      id: 'pool',
      position: { x: 2, y: 1 },
      kind: 'freezable',
      respondsTo: ['glaciate'],
      becomesElevation: 1,
    };
    const w = world([water]);
    w.grid.patch(2, 1, { solid: true, terrain: 'water' });

    const outcome = useFieldAbility(glaciate, { world: w, origin: { x: 2, y: 2 }, facing: 'north' });
    expect(outcome.applied).toBe(true);
    expect(w.grid.at(2, 1)).toMatchObject({ solid: false, elevation: 1, terrain: 'ice' });
    expect(w.grid.canStep({ x: 2, y: 2 }, { x: 2, y: 1 })).toBe(true);
  });

  it('lifts an object to a chosen destination', () => {
    const heave: FieldAbilityDef = {
      id: 'heave',
      name: 'Heave',
      element: 'terra',
      shape: { kind: 'facing' },
      effect: 'lift',
    };
    const rock: Interactable = {
      id: 'rock',
      position: { x: 2, y: 1 },
      kind: 'liftable',
      respondsTo: ['heave'],
    };
    const w = world([rock]);
    const outcome = useFieldAbility(heave, {
      world: w,
      origin: { x: 2, y: 2 },
      facing: 'north',
      destination: { x: 4, y: 4 },
    });
    expect(outcome.applied).toBe(true);
    expect(rock.position).toEqual({ x: 4, y: 4 });
  });

  it('raises bare terrain when no object is present', () => {
    const updraft: FieldAbilityDef = {
      id: 'updraft',
      name: 'Updraft',
      element: 'aeris',
      shape: { kind: 'facing' },
      effect: 'raise',
      elevationDelta: 1,
    };
    const w = world([]);
    const outcome = useFieldAbility(updraft, { world: w, origin: { x: 2, y: 2 }, facing: 'north' });
    expect(outcome.applied).toBe(true);
    expect(w.grid.at(2, 1)?.elevation).toBe(1);
  });
});
