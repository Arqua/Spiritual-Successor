/**
 * The rules layer never draws anything. Instead it emits a flat, ordered list
 * of events describing what happened, and the presentation layer replays that
 * list as animation. This is what makes the same core usable from a 2.5D
 * renderer, a text client, or a headless balance simulation.
 *
 * Events are plain JSON-safe data: they can be logged, sent over a wire, or
 * diffed in a test.
 */

import type { Element } from '../domain/elements.js';

export type GameEvent =
  | { type: 'round-start'; round: number }
  | { type: 'round-end'; round: number }
  | { type: 'turn-start'; combatantId: string }
  | { type: 'turn-skipped'; combatantId: string; reason: string }
  | { type: 'action-declared'; combatantId: string; action: string; targetIds: string[] }
  | { type: 'damage'; sourceId: string | null; targetId: string; amount: number; element: Element | null; critical: boolean; effective: number }
  | { type: 'heal'; sourceId: string | null; targetId: string; amount: number }
  | { type: 'aether-spent'; combatantId: string; amount: number }
  | { type: 'aether-restored'; combatantId: string; amount: number }
  | { type: 'miss'; sourceId: string; targetId: string }
  | { type: 'status-applied'; targetId: string; statusId: string; duration: number }
  | { type: 'status-resisted'; targetId: string; statusId: string }
  | { type: 'status-expired'; targetId: string; statusId: string }
  | { type: 'stat-stage-changed'; targetId: string; stat: string; delta: number; stage: number }
  | { type: 'mote-unleashed'; combatantId: string; moteId: string; element: Element }
  | { type: 'mote-recovered'; combatantId: string; moteId: string }
  | { type: 'class-changed'; combatantId: string; fromClassId: string | null; toClassId: string }
  | { type: 'summon-called'; combatantId: string; summonId: string; motesSpent: number }
  | { type: 'downed'; combatantId: string }
  | { type: 'revived'; combatantId: string; hp: number }
  | { type: 'flee-succeeded'; side: string }
  | { type: 'flee-failed'; side: string }
  | { type: 'battle-ended'; outcome: 'victory' | 'defeat' | 'fled' }
  | { type: 'reward'; xp: number; coin: number; itemIds: string[] }
  | { type: 'field-effect'; abilityId: string; originX: number; originY: number; targetIds: string[] }
  | { type: 'item-used'; combatantId: string; itemId: string; targetIds: string[]; consumed: boolean }
  | { type: 'message'; text: string };

export type GameEventType = GameEvent['type'];

/** Narrow a heterogeneous event list to a single event type. */
export function eventsOfType<T extends GameEventType>(
  events: readonly GameEvent[],
  type: T,
): Extract<GameEvent, { type: T }>[] {
  return events.filter((e): e is Extract<GameEvent, { type: T }> => e.type === type);
}

/** Accumulator used by the resolver; also handy in tests. */
export class EventLog {
  private readonly events: GameEvent[] = [];

  push(event: GameEvent): void {
    this.events.push(event);
  }

  pushAll(events: readonly GameEvent[]): void {
    for (const event of events) this.events.push(event);
  }

  drain(): GameEvent[] {
    return this.events.splice(0, this.events.length);
  }

  toArray(): readonly GameEvent[] {
    return this.events;
  }

  get length(): number {
    return this.events.length;
  }
}

/** Minimal typed subscriber hub for engines that prefer callbacks to polling. */
export class EventBus {
  private readonly handlers = new Map<string, Set<(event: GameEvent) => void>>();

  on<T extends GameEventType>(type: T, handler: (event: Extract<GameEvent, { type: T }>) => void): () => void {
    const set = this.handlers.get(type) ?? new Set();
    set.add(handler as (event: GameEvent) => void);
    this.handlers.set(type, set);
    return () => set.delete(handler as (event: GameEvent) => void);
  }

  emit(event: GameEvent): void {
    for (const handler of this.handlers.get(event.type) ?? []) handler(event);
    for (const handler of this.handlers.get('*') ?? []) handler(event);
  }

  emitAll(events: readonly GameEvent[]): void {
    for (const event of events) this.emit(event);
  }
}
