/**
 * Enemies.
 *
 * Enemies carry a flat stat block rather than a growth curve - encounter
 * design is easier when a slime's numbers are written down instead of derived.
 * Behaviour is a weighted list of moves, which is enough for most encounters;
 * a boss that needs phases supplies `behaviorTag` and the project's own
 * scripting layer takes over.
 */

import type { Element } from './elements.js';
import type { StatBlock } from './stats.js';

export interface EnemyMove {
  /** Art id, or the sentinel "attack" for a basic strike. */
  artId: string;
  weight: number;
  /** Only considered when the enemy is at or below this HP fraction. */
  hpBelow?: number;
  /** Only considered from this round onward. */
  fromRound?: number;
  /** Not usable again for this many rounds. */
  cooldown?: number;
}

export interface EnemyDrop {
  itemId: string;
  chance: number;
}

export interface EnemyDef {
  id: string;
  name: string;
  innate: Element;
  stats: StatBlock;
  arts?: string[];
  moves?: EnemyMove[];
  xp: number;
  coin: number;
  drops?: EnemyDrop[];
  spriteId?: string;
  /** Hook for project-specific boss scripting. */
  behaviorTag?: string;
  /** Resistance to being fled from, 0..1. */
  fleeResistance?: number;
}

export interface EncounterMember {
  enemyId: string;
  /** Formation slot; adjacency drives spread damage. */
  slot: number;
}

export interface EncounterDef {
  id: string;
  members: EncounterMember[];
  /** Cannot be fled. */
  noFlee?: boolean;
  musicId?: string;
  backgroundId?: string;
}
