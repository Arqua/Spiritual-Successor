/**
 * Read a canonical content pack JSON document into a ContentPack.
 *
 * The JSON in `content/` is the interchange format: this implementation and
 * the C# one both read it, so the two engines run identical content rather
 * than drifting copies of it.
 *
 * Loading is deliberately forgiving - unknown fields are ignored and absent
 * optional fields fall back to a default - because a content pack is data.
 * Genuine problems, such as an art referring to a status that does not exist,
 * are reported by `ContentRegistry.validate()` rather than thrown from here,
 * so one typo does not stop the whole pack loading.
 */

import type { ContentPack } from '../core/registry.js';

export interface LoadIssue {
  path: string;
  message: string;
}

export interface LoadResult {
  pack: ContentPack;
  issues: LoadIssue[];
}

type Json = Record<string, unknown>;

const isObject = (value: unknown): value is Json =>
  typeof value === 'object' && value !== null && !Array.isArray(value);

const asArray = (value: unknown): unknown[] => (Array.isArray(value) ? value : []);

const asString = (value: unknown, fallback = ''): string =>
  typeof value === 'string' ? value : fallback;

const asNumber = (value: unknown, fallback = 0): number =>
  typeof value === 'number' && Number.isFinite(value) ? value : fallback;

const asBool = (value: unknown, fallback = false): boolean =>
  typeof value === 'boolean' ? value : fallback;

/**
 * Parse and load. Accepts either a JSON string or an already-parsed value, so
 * a host can hand over whatever its file layer produced.
 */
export function loadPack(source: string | unknown): LoadResult {
  const issues: LoadIssue[] = [];

  let root: unknown;
  if (typeof source === 'string') {
    try {
      root = JSON.parse(source);
    } catch (error) {
      return {
        pack: { id: 'unparsed' },
        issues: [{ path: '', message: `not valid JSON: ${(error as Error).message}` }],
      };
    }
  } else {
    root = source;
  }

  if (!isObject(root)) {
    return { pack: { id: 'unparsed' }, issues: [{ path: '', message: 'pack must be an object' }] };
  }

  // Bind the narrowed value to a const: `root` is a mutable binding, so its
  // narrowing from the guard above does not survive into the closure below.
  const doc: Json = root;

  const pack: ContentPack = { id: asString(doc.id, 'unnamed') };
  if (typeof doc.version === 'string') pack.version = doc.version;

  /**
   * Read one definition list. Entries are copied structurally rather than
   * trusted wholesale, so a hand-edited or downloaded pack cannot smuggle a
   * function or a prototype into the registry. The shapes are structurally
   * identical to the definition interfaces once an id is present; every
   * cross-reference is checked later by `ContentRegistry.validate()`.
   */
  function section<T>(path: string): T[] {
    const loaded: T[] = [];
    asArray(doc[path]).forEach((entry, index) => {
      if (!isObject(entry)) {
        issues.push({ path: `${path}[${index}]`, message: 'entry is not an object' });
        return;
      }
      if (!asString(entry.id)) {
        issues.push({ path: `${path}[${index}]`, message: 'entry has no id' });
        return;
      }
      loaded.push(sanitize(entry) as T);
    });
    return loaded;
  }

  pack.statuses = section<NonNullable<ContentPack['statuses']>[number]>('statuses');
  pack.arts = section<NonNullable<ContentPack['arts']>[number]>('arts');
  pack.motes = section<NonNullable<ContentPack['motes']>[number]>('motes');
  pack.classes = section<NonNullable<ContentPack['classes']>[number]>('classes');
  pack.actors = section<NonNullable<ContentPack['actors']>[number]>('actors');
  pack.gear = section<NonNullable<ContentPack['gear']>[number]>('gear');
  pack.summons = section<NonNullable<ContentPack['summons']>[number]>('summons');
  pack.enemies = section<NonNullable<ContentPack['enemies']>[number]>('enemies');

  return { pack, issues };
}

/**
 * Deep-copy plain data, dropping anything that is not JSON-representable.
 * Guards against prototype pollution from a hand-edited or downloaded pack.
 */
function sanitize(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(sanitize);
  if (isObject(value)) {
    const out: Json = {};
    for (const [key, member] of Object.entries(value)) {
      if (key === '__proto__' || key === 'constructor' || key === 'prototype') continue;
      if (member === undefined) continue;
      out[key] = sanitize(member);
    }
    return out;
  }
  if (typeof value === 'number' && !Number.isFinite(value)) return 0;
  return value;
}

/** Convenience: load and throw on any structural issue. */
export function loadPackStrict(source: string | unknown): ContentPack {
  const { pack, issues } = loadPack(source);
  if (issues.length > 0) {
    const detail = issues.map((i) => `  ${i.path}: ${i.message}`).join('\n');
    throw new Error(`Content pack failed to load with ${issues.length} issue(s):\n${detail}`);
  }
  return pack;
}
