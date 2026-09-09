import { describe, expect, it } from 'vitest';
import { ContentRegistry, type ContentPack } from '../src/core/registry.js';
import { starterPack } from '../src/data/starterPack.js';

describe('content registry', () => {
  it('validates the shipped starter pack with no errors', () => {
    const registry = ContentRegistry.from(starterPack);
    const errors = registry.validate().filter((issue) => issue.severity === 'error');
    expect(errors).toEqual([]);
    expect(() => registry.assertValid()).not.toThrow();
  });

  it('catches a reference to an art that does not exist', () => {
    const broken: ContentPack = {
      id: 'broken',
      classes: [{ id: 'c', name: 'C', requires: {}, priority: 1, arts: [{ artId: 'ghost-art', level: 1 }] }],
    };
    const registry = ContentRegistry.from(broken);
    expect(registry.validate().some((issue) => issue.message.includes('ghost-art'))).toBe(true);
    expect(() => registry.assertValid()).toThrow(/validation failed/i);
  });

  it('catches a reference to a status that does not exist', () => {
    const broken: ContentPack = {
      id: 'broken',
      arts: [{
        id: 'a', name: 'A', element: 'pyre', kind: 'aetheric', targeting: 'one-foe', cost: 1,
        effect: { power: 10, statuses: [{ statusId: 'ghost-status', chance: 1 }] },
      }],
    };
    expect(ContentRegistry.from(broken).validate().some((i) => i.message.includes('ghost-status'))).toBe(true);
  });

  it('lets a later pack override an earlier entry by id', () => {
    const patch: ContentPack = {
      id: 'balance-patch',
      arts: [{ id: 'ember', name: 'Ember (nerfed)', element: 'pyre', kind: 'aetheric', targeting: 'one-foe', cost: 5, effect: { power: 5 } }],
    };
    const registry = ContentRegistry.from(starterPack, patch);
    expect(registry.artDef('ember')?.effect.power).toBe(5);
    // Everything else from the base pack survives the merge.
    expect(registry.artDef('quake')).toBeDefined();
    expect(registry.packs).toEqual(['starter', 'balance-patch']);
  });

  it('exposes an actor context wired to its own lookups', () => {
    const registry = ContentRegistry.from(starterPack);
    const ctx = registry.actorContext();
    expect(ctx.actorDef('rell')?.name).toBe('Rell');
    expect(ctx.moteDef('boulder')?.element).toBe('terra');
    expect(ctx.classDefs.length).toBeGreaterThan(0);
  });
});
