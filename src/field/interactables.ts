/**
 * Field entities and interactables.
 *
 * An interactable declares which field abilities it responds to, rather than
 * an ability knowing what it can affect. That inversion is what keeps the
 * puzzle toolkit open: adding a new liftable rock is content, not code, and a
 * new ability automatically works on everything that already listens for it.
 */

import type { Vec2 } from './grid.js';

export interface FieldEntity {
  id: string;
  position: Vec2;
  /** Height above the tile, for entities on ledges or in the air. */
  elevation?: number;
  /** Renderer hint; the rules layer never reads it. */
  spriteId?: string;
  /** Blocks movement while present. */
  blocking?: boolean;
}

export type InteractableKind =
  /** Can be picked up, carried, and set down. */
  | 'liftable'
  /** Can be pushed one tile at a time. */
  | 'pushable'
  /** Toggles between two states. */
  | 'switch'
  /** Can be frozen into a solid platform. */
  | 'freezable'
  /** Can be grown into a climbable surface. */
  | 'growable'
  /** Can be burned away. */
  | 'burnable'
  /** Reveals something otherwise invisible. */
  | 'hidden'
  /** Reads as scenery until an ability wakes it. */
  | 'inert';

export interface Interactable extends FieldEntity {
  kind: InteractableKind;
  /** Field ability ids this object reacts to. */
  respondsTo: string[];
  /** Current state, toggled by abilities. Free-form so content owns meaning. */
  state?: string;
  /** Set true once consumed, so one-shot puzzles do not repeat. */
  spent?: boolean;
  /** Elevation the tile becomes when this object is activated. */
  becomesElevation?: number;
  /** Terrain the tile becomes when activated. */
  becomesTerrain?: string;
  /** Whether activation removes the blocking flag. */
  clearsBlocking?: boolean;
}

export function isInteractable(entity: FieldEntity): entity is Interactable {
  return 'kind' in entity && 'respondsTo' in entity;
}

export function respondsTo(interactable: Interactable, abilityId: string): boolean {
  return !interactable.spent && interactable.respondsTo.includes(abilityId);
}
