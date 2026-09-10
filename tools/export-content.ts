/**
 * Export a TypeScript content pack to canonical JSON.
 *
 * The JSON is the interchange format both implementations read, so this tool
 * is what keeps `src/data/starterPack.ts` and `content/starter.json` from
 * drifting. Re-run it after editing the pack.
 *
 *   npx tsx tools/export-content.ts
 */

import { writeFileSync, mkdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { starterPack, starterFieldAbilities } from '../src/data/starterPack.js';

const out = resolve(process.cwd(), 'content/starter.json');
mkdirSync(dirname(out), { recursive: true });

// Sort keys so the output is byte-stable across runs; an unstable export would
// churn the diff and defeat the point of committing the file.
function sortKeys(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(sortKeys);
  if (value && typeof value === 'object') {
    const entries = Object.entries(value as Record<string, unknown>)
      .filter(([, v]) => v !== undefined)
      .sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0));
    return Object.fromEntries(entries.map(([k, v]) => [k, sortKeys(v)]));
  }
  return value;
}

const payload = sortKeys({ ...starterPack, fieldAbilities: starterFieldAbilities });
writeFileSync(out, JSON.stringify(payload, null, 2) + '\n', 'utf8');
console.log(`wrote ${out}`);
