# Working in this repository

Two implementations of one game, plus a browser reference renderer.

```
src/                     TypeScript rules core + presentation layer (source of truth)
unity/Aetherlight.Core/  C# port, consumed by Unity as a local UPM package
unity/Aetherlight.Core.Tests/   xUnit tests for the port
demo/                    browser demo driving the TypeScript core
```

## Verify before you claim

```bash
npm test                                   # 130 TypeScript tests
npm run typecheck                          # core (DOM-free) + browser configs
cd unity/Aetherlight.Core.Tests && dotnet test   # 132 C# tests
```

Both typechecks must pass. `tsconfig.json` compiles the core **without** the DOM
lib on purpose - it is what stops browser APIs leaking into the portable
layers. If you need a DOM type, it belongs in `src/presentation/renderer/`.

Do not pipe a compiler through `head` and then `&& echo OK` - `head`'s exit
status masks the compiler's, and it will report success over a failure. Check
the exit code.

## Content is shared, not duplicated

`content/starter.json` is the canonical pack and both implementations read it.
It is generated from `src/data/starterPack.ts` - after editing content run
`npm run content:export` and `npm run content:fingerprint`, and commit the
regenerated files with the change. `ContentParityTests` fails if the two sides
stop agreeing about what the content means.

## The two implementations must agree

`unity/Aetherlight.Core.Tests/RngParityTests.cs` pins the C# generator against
values captured from the TypeScript one. **If it fails, stop.** A diverged
generator means no seed, replay, or recorded battle transfers between the two,
and every downstream test still passes while the game silently disagrees with
itself.

Changing the generator deliberately means regenerating the fixture
(`tools/rng-fixture.ts`) and changing both sides in the same commit.

When porting a rule, port its test too. The TypeScript suite is written as a
specification rather than for coverage - same assertions, same seeds.

## Unity: what is safe to edit

**Safe** - plain C# under `unity/Aetherlight.Core/`, and any `.cs` you author.

**Do not hand-edit:**

- `*.meta` files - Unity generates these and they carry the GUIDs every asset
  reference resolves through. Editing or deleting one silently detaches
  materials, prefabs and scene references. If a `.meta` is wrong, fix it in the
  Editor, not the text.
- `*.unity` (scenes), `*.prefab`, `*.asset` - YAML with GUID cross-references.
  Hand-editing corrupts them in ways that surface much later.
- `ProjectSettings/`, `Packages/packages-lock.json` - Editor-owned.

If a change genuinely needs a scene or prefab edit, describe what to do in the
Editor rather than writing the YAML.

**Never** put `bin/` or `obj/` inside a folder Unity imports. Unity compiles
generated `*.AssemblyInfo.cs` from `obj/` and reports duplicate-attribute
errors that look like they come from your own code. `unity/Directory.Build.props`
already redirects both to `unity/build/`; leave it that way.

The core package sets `"noEngineReferences": true` in its asmdef. That is
deliberate - it makes the compiler reject a `UnityEngine` import in the rules
layer, the same boundary the DOM-free tsconfig enforces on the TypeScript side.

## Conventions

- The rules layer never renders, loads assets, or reads input. It computes and
  emits events.
- Content is data. Adding an ability or an enemy is a content-pack change, not
  a code change.
- Prefer `init`-free plain properties in the C# port: `init` needs
  `IsExternalInit`, which `netstandard2.1` does not define.
