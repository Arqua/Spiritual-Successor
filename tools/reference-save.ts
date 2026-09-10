/**
 * Produce the reference save both implementations read in their tests.
 *
 * Deliberately includes a status with an infinite remaining duration - one that
 * lasts until cured. JSON has no infinity, so it has to travel as something,
 * and both engines must agree on what. Generating the fixture from this side
 * pins the wire format.
 *
 *   npx tsx tools/reference-save.ts
 */

import { writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { createSave, serialize } from '../src/save/serialize.js';
import type { ActorState } from '../src/domain/actor.js';

const rell: ActorState = {
  id: 'rell',
  defId: 'rell',
  level: 14,
  xp: 5200,
  hp: 118,
  aether: 24,
  motes: [
    { defId: 'boulder', state: 'set', recoveryLeft: 0 },
    { defId: 'cinder', state: 'standby', recoveryLeft: 0 },
    { defId: 'loam', state: 'recovering', recoveryLeft: 2 },
  ],
  equipment: { weapon: 'iron-blade', armor: 'warded-mail' },
  statuses: [
    { statusId: 'scorch', remaining: 2, sourceId: 'foe:ash-wisp:0' },
    // Lasts until cured: remaining is Infinity on this side.
    { statusId: 'venom', remaining: Number.POSITIVE_INFINITY, sourceId: null },
  ],
  tempModifiers: [],
};

const save = createSave({
  packs: ['starter'],
  party: [rell],
  coin: 1450,
  inventory: [{ itemId: 'oak-shield', count: 2 }],
  looseMotes: ['zephyr', 'brine'],
  location: { mapId: 'overworld', position: { x: 12, y: 7 }, facing: 'south' },
  flags: { metTheGuide: true, chapter: 3, lastInn: 'riverside' },
  playTimeSeconds: 7384,
});

// The timestamp would churn the fixture on every run, so pin it.
save.savedAt = '2026-01-01T00:00:00.000Z';

const out = resolve(process.cwd(), 'content/reference-save.json');
writeFileSync(out, JSON.stringify(save, null, 2) + '\n', 'utf8');
console.log(`wrote ${out}`);
console.log('venom remaining serializes as:', JSON.stringify(JSON.parse(serialize(save)).party[0].statuses[1].remaining));
