# Unity integration

Two things live here: a C# port of the rules core that Unity consumes as a
local package, and the repository conventions that make the project workable.

## Layout

```
unity/
  Directory.Build.props        redirects bin/obj out of Unity's reach
  Aetherlight.Core/            the port. A UPM package AND a dotnet class library
    package.json               so Unity can reference it as a local package
    Aetherlight.Core.asmdef    noEngineReferences: true
    Aetherlight.Core.csproj    netstandard2.1, so dotnet can build/test it
  Aetherlight.Core.Tests/      xUnit, runs headless in seconds
  build/                       all bin/obj output (gitignored)
```

The core targets **netstandard2.1**, which is what Unity 2021.2+ consumes. The
same sources therefore compile in both places: `dotnet test` for a two-second
feedback loop while porting, and Unity for the real thing.

## Wiring it into a Unity project

1. Create the project in the Unity Hub (2021.3 LTS or newer) at
   `unity/UnityProject`.
2. Add the core as a local package in `unity/UnityProject/Packages/manifest.json`:

   ```json
   {
     "dependencies": {
       "com.aetherlight.core": "file:../../Aetherlight.Core"
     }
   }
   ```

   A local `file:` package keeps one copy of the sources. Copying them into
   `Assets/` instead means two copies that drift.

3. Reference `Aetherlight.Core` from your own asmdef and you are done.

### Two traps worth knowing

**`obj/` inside an imported folder breaks the build.** A default `dotnet build`
writes generated `*.AssemblyInfo.cs` into `obj/` beside the sources, and Unity
compiles it, producing duplicate-attribute errors that appear to come from your
own code. `unity/Directory.Build.props` redirects `bin`/`obj` to `unity/build/`.
Leave it alone.

**`init` accessors do not compile on netstandard2.1.** They need
`System.Runtime.CompilerServices.IsExternalInit`, which that target does not
define. Declaring the shim yourself risks colliding with another assembly's
copy depending on the Unity version, so the port uses plain settable
properties.

## Running the tests

Headless, fast, no Editor required - this is the loop to use while porting:

```bash
cd unity/Aetherlight.Core.Tests && dotnet test
```

For Unity-side tests (anything touching UnityEngine), the Editor runs them from
the command line:

```bash
Unity -runTests -batchmode -projectPath unity/UnityProject \
      -testPlatform EditMode -testResults results.xml
```

That needs a licensed Editor installed, so it is a local or CI step rather than
something to reach for on every change. Keep logic in the engine-free core
where `dotnet test` can reach it, and the Editor run stays a formality.

## Cross-language agreement

`RngParityTests` pins the C# generator against values captured from the
TypeScript one. This is the test that matters most: if the two generators
disagree, no seed, replay, or recorded battle transfers between them, and every
other test still passes while the two implementations quietly diverge.

Regenerate the fixture with `npx tsx tools/rng-fixture.ts` if you ever change
the generator deliberately, and change both sides in the same commit.

## What is ported, and what is not

Ported and tested (30 tests):

- `Core/Rng.cs` - bit-exact with the TypeScript generator
- `Domain/Elements.cs` - affinity maths, opposed pairs, element tables
- `Domain/Stats.cs` - stat blocks, modifiers, growth curves, XP
- `Domain/Motes.cs` - the set/standby/recovering cycle
- `Domain/Classes.cs` - class derivation and its tie-breaking

Not yet ported:

- `Battle/` - damage, turn order, the round resolver, enemy AI
- `Field/` - grid, interactables, field abilities
- `Presentation/` - timeline and playback (the canvas renderer is web-only and
  should be replaced by a Unity renderer, not ported)
- `Core/Registry.cs`, save serialization, and content-pack loading

Port in dependency order, and port each module's TypeScript test alongside it.
The suites in `test/` are written as specifications rather than for coverage:
same assertions, same seeds. If the C# version passes them, the port is
faithful.

Content packs stay JSON so both implementations read identical data - port the
*rules*, never the content.
