# UnityEngine compile stub

**This is not Unity, and it must never enter a Unity project.**

`UnityEngine.cs` declares a fake `UnityEngine` namespace. If it were ever
imported into an actual Unity project it would collide with the real engine
assemblies and break the compile in confusing ways. It lives outside both
packages precisely so Unity cannot see it: Unity imports `Assets/` and the
packages named in `manifest.json`, and this is in neither.

## What it is for

The renderer in `unity/Aetherlight.Unity/` cannot be compiled without the real
engine assemblies, which need an Editor install. This stub declares just enough
of the UnityEngine surface that the renderer uses to let the compiler check it:

```bash
dotnet build unity/tools/unity-stub/Aetherlight.Unity.Compile.csproj
```

## What it proves, and what it does not

**It proves** the renderer's own code is internally consistent - no typos, no
type errors, no missing usings, no misuse of the Aetherlight core API, and no
accidental `UnityEngine.Camera` / `Aetherlight.Presentation.Camera` ambiguity.

**It does not prove** the renderer matches the real Unity API. The stub was
written from knowledge of Unity's signatures, so if a signature here is wrong,
the renderer is checked against the same wrong assumption. Only opening the
project in an Editor closes that gap.

Treat a clean build here as "worth trying in the Editor", not "verified".

## Keeping it useful

When the renderer starts using a UnityEngine member the stub does not declare,
the build fails with a missing-member error. Add the member with the signature
the real engine uses - checking the Unity scripting reference rather than
guessing, since a wrong signature here silently weakens every future check.
