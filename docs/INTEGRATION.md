# Integrating with an engine

The core is plain TypeScript with zero runtime dependencies. It computes rules
and returns data. Everything below is about getting that data into your engine
of choice.

## The shape of a frame

Whatever the engine, the loop is the same:

1. Collect the player's commands for the round.
2. Ask the AI for the enemies' commands.
3. Call `resolveRound()`. It returns immediately with the full event list.
4. Hand the events to your presentation layer and let it animate at its pace.
5. When the animation finishes, if `state.phase !== 'ended'`, go to 1.

```ts
import {
  ContentRegistry, createBattle, resolveRound, enemyCommands, concludeBattle,
} from 'aetherlight-core';
import { starterPack } from 'aetherlight-core/data/starterPack.js';

const registry = ContentRegistry.from(starterPack, myPack);
registry.assertValid();                    // fail fast on content typos

const state = createBattle({ party, encounter, seed: Date.now() }, registry);

while (state.phase !== 'ended') {
  const commands = [...await ui.collectCommands(state), ...enemyCommands(state, registry)];
  const { events } = resolveRound(state, commands, registry);
  await presentation.play(events);
}

const aftermath = concludeBattle(state, party, registry);
```

## Choosing how to embed it

### Web / Electron / React Native

Import it directly. This is the native case; there is nothing to bridge.

### Godot 4

Two options.

**Run the core in JavaScript** if you are exporting to web: use the JavaScript
bridge and pass events across as JSON.

**Port or transpile** for native exports. The rules layer is deliberately
written to make this cheap — pure functions, plain data, no classes with
behaviour except `Grid`, `ContentRegistry` and the event helpers. A direct
transliteration to GDScript or C# is mechanical. Keep the content packs as JSON
so both implementations read identical data, and use the same seeds to verify
the port against the TypeScript original.

### Unity / C#

Port the rules layer (see above) and keep content as JSON, deserialized with
`System.Text.Json` into records mirroring the interfaces in `src/domain`. The
event list maps cleanly onto a `Queue<GameEvent>` feeding a coroutine.

### Unreal

Same approach: mirror the domain types as `USTRUCT`s, load content packs as
JSON data assets, and drive presentation from the event queue. The rules layer
is small enough that a C++ port is a contained job, and the test suite in
`test/` doubles as the specification to port against.

### Bevy / Rust

The domain types are all plain data and map onto `serde`-derived structs. The
seeded RNG is documented in `src/core/rng.ts`; reimplement it exactly if you
want cross-language replay compatibility, or swap in any seeded PRNG if you do
not.

## Porting checklist

If you reimplement the rules in another language, port in this order — each
layer only depends on the ones above it:

1. `core/rng` — everything downstream needs deterministic randomness.
2. `domain/elements`, `domain/stats` — pure maths, no dependencies.
3. `domain/motes`, `domain/classes` — the class-derivation rules.
4. `domain/arts`, `domain/equipment`, `domain/summons`, `domain/status`.
5. `domain/actor` — stat derivation, which composes everything above.
6. `battle/damage`, `battle/order`, `battle/resolve` — the fight.
7. `field/*` — the overworld toolkit.

Port the tests alongside each layer. `test/` is written as a specification
rather than as coverage-chasing: if your port passes the same assertions with
the same seeds, it behaves identically.

## Content is data, not code

`ContentRegistry` merges packs in load order, later entries overriding earlier
ones by id. This gives you, for free:

- **Balance patches** — ship a small pack that redefines a handful of arts.
- **Mods** — load user packs after the base pack.
- **Localization** — a pack that overrides only `name` and `description`.
- **Difficulty modes** — a pack that redefines enemy stat blocks.

`registry.validate()` checks every cross-reference and returns structured
issues. Run `assertValid()` in a content test so a typo fails CI rather than a
boss fight.

## Determinism and netplay

The RNG state lives inside `BattleState`. Two clients starting from the same
state and applying the same commands produce byte-identical results, so you can
send commands rather than outcomes across the wire. The same property makes
replays cheap: store the seed and the command list, not the event log.

Do not call `Math.random()` anywhere in your rules extensions, and do not let
the presentation layer feed anything back into battle state.

## Saving

`createSave()`, `serialize()` and `deserialize()` handle versioning and
migration. All runtime state is already JSON-safe, so there is no custom
serialization to write. When you add fields, bump `SAVE_VERSION` and add a
migration block in `migrate()`.
