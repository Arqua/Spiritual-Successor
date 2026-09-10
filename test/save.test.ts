import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
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

describe('durations that outlive a save', () => {
  it('restores an unlimited duration rather than leaving a null behind', () => {
    // JSON has no infinity, so a status that lasts until cured serializes as
    // null. Loading must turn it back into a number: `null` passes the
    // isFinite check by accident, but `null - 1` is -1, so any arithmetic
    // expires a permanent status immediately.
    const rell = createActorState(registry.actorDef('rell')!, 8, ctx);
    rell.statuses = [{ statusId: 'venom', remaining: Number.POSITIVE_INFINITY, sourceId: null }];

    const save = createSave({
      packs: ['starter'],
      party: [rell],
      location: { mapId: 'x', position: { x: 0, y: 0 }, facing: 'north' },
    });

    const restored = deserialize(serialize(save));
    const venom = restored.party[0]!.statuses[0]!;

    expect(typeof venom.remaining).toBe('number');
    expect(venom.remaining).toBe(Number.POSITIVE_INFINITY);
    expect(venom.remaining - 1).toBe(Number.POSITIVE_INFINITY);
  });

  it('keeps a finite duration finite', () => {
    const rell = createActorState(registry.actorDef('rell')!, 8, ctx);
    rell.statuses = [{ statusId: 'scorch', remaining: 3, sourceId: null }];
    const save = createSave({
      packs: ['starter'],
      party: [rell],
      location: { mapId: 'x', position: { x: 0, y: 0 }, facing: 'north' },
    });
    expect(deserialize(serialize(save)).party[0]!.statuses[0]!.remaining).toBe(3);
  });

  it('loads the shared reference save the C# tests also read', () => {
    const save = deserialize(readFileSync(resolve(process.cwd(), 'content/reference-save.json'), 'utf8'));
    expect(save.party[0]!.level).toBe(14);
    expect(save.coin).toBe(1450);
    expect(save.party[0]!.motes.find((m) => m.defId === 'cinder')?.state).toBe('standby');
    expect(save.party[0]!.statuses.find((s) => s.statusId === 'venom')?.remaining).toBe(Number.POSITIVE_INFINITY);
  });
});
