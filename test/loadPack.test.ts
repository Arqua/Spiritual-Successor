import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { loadPack, loadPackStrict } from '../src/data/loadPack.js';
import { ContentRegistry } from '../src/core/registry.js';
import { starterPack } from '../src/data/starterPack.js';
import { createActorState, deriveClass } from '../src/domain/actor.js';
import { makeMote } from '../src/domain/motes.js';

const canonical = readFileSync(resolve(process.cwd(), 'content/starter.json'), 'utf8');

describe('content pack loading', () => {
  it('loads the canonical pack without issues', () => {
    const { issues } = loadPack(canonical);
    expect(issues).toEqual([]);
  });

  it('produces a registry that validates cleanly', () => {
    const registry = ContentRegistry.from(loadPackStrict(canonical));
    expect(registry.validate().filter((i) => i.severity === 'error')).toEqual([]);
  });

  it('round-trips the authored pack without losing anything', () => {
    // The exported JSON is generated from starterPack, so loading it back must
    // reproduce the same content. This is what keeps the authored TypeScript
    // and the committed JSON from drifting.
    const loaded = loadPackStrict(canonical);
    const authored = JSON.parse(JSON.stringify(starterPack));

    for (const section of ['actors', 'arts', 'classes', 'motes', 'statuses', 'summons', 'gear', 'enemies'] as const) {
      const loadedSection = loaded[section] ?? [];
      const authoredSection = authored[section] ?? [];
      expect(loadedSection.length, `${section} count`).toBe(authoredSection.length);

      const byId = new Map(loadedSection.map((entry: { id: string }) => [entry.id, entry]));
      for (const entry of authoredSection as { id: string }[]) {
        expect(byId.get(entry.id), `${section}/${entry.id}`).toEqual(entry);
      }
    }
  });

  it('preserves the behaviour the data drives', () => {
    const registry = ContentRegistry.from(loadPackStrict(canonical));
    const ctx = registry.actorContext();
    const rell = createActorState(registry.actorDef('rell')!, 20, ctx);

    rell.motes = [makeMote('boulder')];
    expect(deriveClass(rell, ctx)?.id).toBe('geomancer');

    rell.motes = [makeMote('boulder'), makeMote('cinder')];
    expect(deriveClass(rell, ctx)?.id).toBe('ashwarden');
  });

  it('reports malformed JSON rather than throwing', () => {
    const { issues } = loadPack('{ not json');
    expect(issues).toHaveLength(1);
    expect(issues[0]?.message).toMatch(/not valid JSON/);
  });

  it('reports entries without an id instead of loading them', () => {
    const { pack, issues } = loadPack(JSON.stringify({ id: 'broken', arts: [{ name: 'Nameless' }] }));
    expect(pack.arts).toEqual([]);
    expect(issues[0]?.message).toMatch(/no id/);
  });

  it('ignores a section that is not an array', () => {
    const { pack } = loadPack(JSON.stringify({ id: 'odd', arts: 'not-an-array' }));
    expect(pack.arts).toEqual([]);
  });

  it('strips prototype-polluting keys from a hand-edited pack', () => {
    const hostile = '{"id":"x","arts":[{"id":"a","__proto__":{"polluted":true}}]}';
    const { pack } = loadPack(hostile);
    expect(({} as Record<string, unknown>).polluted).toBeUndefined();
    expect(pack.arts?.[0]?.id).toBe('a');
  });

  it('throws from the strict loader when something is wrong', () => {
    expect(() => loadPackStrict('{ not json')).toThrow(/failed to load/);
  });
});
