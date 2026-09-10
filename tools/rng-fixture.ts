import { seedRng, nextUint32, nextFloat, nextInt } from '/home/user/Spiritual-Successor/src/core/rng.js';
const out: Record<string, unknown> = {};
for (const seed of ['demo', 'battle-42', 'x', '', 'a-longer-seed-string-0123456789']) {
  const s = seedRng(seed);
  out[`seed:${seed}`] = { state: { ...s }, uints: Array.from({ length: 8 }, () => nextUint32(s)) };
}
{ const s = seedRng('floats'); out['floats'] = Array.from({ length: 6 }, () => nextFloat(s)); }
{ const s = seedRng('ints'); out['ints'] = Array.from({ length: 10 }, () => nextInt(s, 3, 9)); }
{ const s = seedRng(12345); out['numeric-seed'] = { state: { ...s }, uints: Array.from({ length: 4 }, () => nextUint32(s)) }; }
console.log(JSON.stringify(out, null, 1));
