import { describe, expect, it } from 'vitest';
import { ContentRegistry } from '../src/core/registry.js';
import { starterPack } from '../src/data/starterPack.js';
import { createActorState } from '../src/domain/actor.js';
import { makeMote } from '../src/domain/motes.js';
import { createSave, deserialize, migrate, SAVE_VERSION, serialize } from '../src/save/serialize.js';

const registry = ContentRegistry.from(starterPack);
const ctx = registry.actorContext();

function sampleSave() {
  const rell = createActorState(registry.actorDef('rell')!, 8, ctx);
  rell.motes = [makeMote('boulder'), makeMote('cinder', 'standby')];
  rell.equipment = { weapon: 'iron-blade' };
  return createSave({
    packs: ['starter'],
    party: [rell],
    coin: 500,
    inventory: [{ itemId: 'oak-shield', count: 2 }],
    looseMotes: ['zephyr'],
    location: { mapId: 'overworld', position: { x: 10, y: 4 }, facing: 'south' },
    flags: { metTheGuide: true },
  });
}

describe('save files', () => {
  it('round-trips through JSON without loss', () => {
    const save = sampleSave();
    const restored = deserialize(serialize(save));
    expect(restored).toEqual(save);
  });

  it('stamps the current version', () => {
    expect(sampleSave().version).toBe(SAVE_VERSION);
  });

  it('preserves mote states exactly', () => {
    const restored = deserialize(serialize(sampleSave()));
    const motes = restored.party[0]!.motes;
    expect(motes.find((m) => m.defId === 'boulder')?.state).toBe('set');
    expect(motes.find((m) => m.defId === 'cinder')?.state).toBe('standby');
  });

  it('deep-copies actors so later mutation does not leak into the save', () => {
    const rell = createActorState(registry.actorDef('rell')!, 8, ctx);
    rell.motes = [makeMote('boulder')];
    const save = createSave({
      packs: ['starter'],
      party: [rell],
      location: { mapId: 'x', position: { x: 0, y: 0 }, facing: 'north' },
    });
    rell.motes[0]!.state = 'standby';
    rell.level = 99;
    expect(save.party[0]!.motes[0]!.state).toBe('set');
    expect(save.party[0]!.level).toBe(8);
  });

  it('rejects data that is not a save file', () => {
    expect(() => deserialize('{"nope":true}')).toThrow();
  });

  it('fills in fields missing from a hand-edited save', () => {
    const partial = {
      version: 1,
      packs: [],
      savedAt: '',
      playTimeSeconds: 0,
      party: [{ id: 'a', defId: 'rell', level: 1, xp: 0, hp: 1, aether: 0 }],
      location: { mapId: 'x', position: { x: 0, y: 0 }, facing: 'north' },
    } as never;
    const migrated = migrate(partial);
    expect(migrated.party[0]!.motes).toEqual([]);
    expect(migrated.party[0]!.statuses).toEqual([]);
    expect(migrated.inventory).toEqual([]);
    expect(migrated.flags).toEqual({});
  });
});
