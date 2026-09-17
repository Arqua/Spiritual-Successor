/**
 * The content registry.
 *
 * All game content - actors, motes, classes, arts, gear, summons, statuses,
 * enemies - is data, loaded from one or more content packs. The rules layer
 * only ever reads through this registry, so a project can ship its own packs,
 * merge community packs on top, or hot-reload during development without the
 * systems knowing anything changed.
 *
 * Packs merge in load order: a later pack overrides an earlier entry with the
 * same id. That is what makes a "balance patch" pack or a mod pack possible
 * without editing the base content.
 */

import type { ActorDef } from '../domain/actor.js';
import type { ArtDef } from '../domain/arts.js';
import type { ClassDef } from '../domain/classes.js';
import type { GearDef } from '../domain/equipment.js';
import type { MoteDef } from '../domain/motes.js';
import type { StatusDef } from '../domain/status.js';
import type { SummonDef } from '../domain/summons.js';
import type { EnemyDef } from '../domain/enemy.js';
import type { ItemDef } from '../domain/items.js';

export interface ContentPack {
  id: string;
  version?: string;
  actors?: ActorDef[];
  motes?: MoteDef[];
  classes?: ClassDef[];
  arts?: ArtDef[];
  gear?: GearDef[];
  summons?: SummonDef[];
  statuses?: StatusDef[];
  enemies?: EnemyDef[];
  items?: ItemDef[];
}

export interface ValidationIssue {
  severity: 'error' | 'warning';
  packId: string;
  entity: string;
  id: string;
  message: string;
}

export class ContentRegistry {
  readonly actors = new Map<string, ActorDef>();
  readonly motes = new Map<string, MoteDef>();
  readonly classes = new Map<string, ClassDef>();
  readonly arts = new Map<string, ArtDef>();
  readonly gear = new Map<string, GearDef>();
  readonly summons = new Map<string, SummonDef>();
  readonly statuses = new Map<string, StatusDef>();
  readonly enemies = new Map<string, EnemyDef>();
  readonly items = new Map<string, ItemDef>();

  private readonly loadedPacks: string[] = [];

  static from(...packs: ContentPack[]): ContentRegistry {
    const registry = new ContentRegistry();
    for (const pack of packs) registry.load(pack);
    return registry;
  }

  load(pack: ContentPack): this {
    put(this.actors, pack.actors);
    put(this.motes, pack.motes);
    put(this.classes, pack.classes);
    put(this.arts, pack.arts);
    put(this.gear, pack.gear);
    put(this.summons, pack.summons);
    put(this.statuses, pack.statuses);
    put(this.enemies, pack.enemies);
    put(this.items, pack.items);
    this.loadedPacks.push(pack.id);
    return this;
  }

  get packs(): readonly string[] {
    return this.loadedPacks;
  }

  /** Class list as an array, which is what class resolution wants. */
  get classList(): ClassDef[] {
    return [...this.classes.values()];
  }

  actorDef = (id: string): ActorDef | undefined => this.actors.get(id);
  moteDef = (id: string): MoteDef | undefined => this.motes.get(id);
  artDef = (id: string): ArtDef | undefined => this.arts.get(id);
  gearDef = (id: string): GearDef | undefined => this.gear.get(id);
  summonDef = (id: string): SummonDef | undefined => this.summons.get(id);
  statusDef = (id: string): StatusDef | undefined => this.statuses.get(id);
  enemyDef = (id: string): EnemyDef | undefined => this.enemies.get(id);
  itemDef = (id: string): ItemDef | undefined => this.items.get(id);

  /** Context object accepted by the actor derivation functions. */
  actorContext() {
    return {
      actorDef: this.actorDef,
      moteDef: this.moteDef,
      classDefs: this.classList,
      gearDef: this.gearDef,
      statusDef: this.statusDef,
    };
  }

  /**
   * Check every cross-reference. Run this in a content test so a typo in an
   * art id fails the build instead of throwing halfway through a boss fight.
   */
  validate(): ValidationIssue[] {
    const issues: ValidationIssue[] = [];
    const packId = this.loadedPacks.join('+') || '(none)';

    const requireArt = (id: string | undefined, entity: string, ownerId: string) => {
      if (id && !this.arts.has(id)) {
        issues.push({ severity: 'error', packId, entity, id: ownerId, message: `unknown art "${id}"` });
      }
    };
    const requireStatus = (id: string | undefined, entity: string, ownerId: string) => {
      if (id && !this.statuses.has(id)) {
        issues.push({ severity: 'error', packId, entity, id: ownerId, message: `unknown status "${id}"` });
      }
    };

    for (const actor of this.actors.values()) {
      for (const artId of actor.baseArts ?? []) requireArt(artId, 'actor', actor.id);
    }
    for (const cls of this.classes.values()) {
      for (const grant of cls.arts ?? []) requireArt(grant.artId, 'class', cls.id);
    }
    for (const art of this.arts.values()) {
      for (const status of art.effect.statuses ?? []) requireStatus(status.statusId, 'art', art.id);
    }
    for (const mote of this.motes.values()) {
      if (mote.effect.kind === 'status') requireStatus(mote.effect.statusId, 'mote', mote.id);
    }
    for (const gear of this.gear.values()) {
      requireArt(gear.proc?.artId, 'gear', gear.id);
    }
    for (const enemy of this.enemies.values()) {
      for (const artId of enemy.arts ?? []) requireArt(artId, 'enemy', enemy.id);
    }
    for (const item of this.items.values()) {
      requireArt(item.artId, 'item', item.id);
      if (item.kind === 'consumable' && !item.artId) {
        issues.push({ severity: 'warning', packId, entity: 'item', id: item.id, message: 'consumable does nothing when used' });
      }
    }
    for (const enemy of this.enemies.values()) {
      for (const drop of enemy.drops ?? []) {
        // A drop naming a real item is the common case now that items exist;
        // the gear fallback stays for packs written before they did.
        if (!this.items.has(drop.itemId) && !this.gear.has(drop.itemId)) {
          issues.push({ severity: 'warning', packId, entity: 'enemy', id: enemy.id, message: `drop "${drop.itemId}" is neither an item nor gear` });
        }
      }
    }
    for (const summon of this.summons.values()) {
      if (Object.values(summon.cost).every((n) => !n)) {
        issues.push({ severity: 'warning', packId, entity: 'summon', id: summon.id, message: 'summon costs nothing' });
      }
    }
    return issues;
  }

  /** Throws if validation found any error-severity issue. */
  assertValid(): void {
    const errors = this.validate().filter((issue) => issue.severity === 'error');
    if (errors.length > 0) {
      const detail = errors.map((e) => `  ${e.entity} ${e.id}: ${e.message}`).join('\n');
      throw new Error(`Content validation failed with ${errors.length} error(s):\n${detail}`);
    }
  }
}

function put<T extends { id: string }>(target: Map<string, T>, entries: T[] | undefined): void {
  for (const entry of entries ?? []) target.set(entry.id, entry);
}
