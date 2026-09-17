# Aetherlight Unity Renderer

The Unity presentation layer. It consumes the rules core's event timeline and
draws it; it never decides anything about the fight.

```
ResolveRound()  ->  events  ->  TimelineBuilder  ->  Playback  ->  these components
   instant                       schedule           paces it      draw it
```

`BattleDirector` owns that loop. Everything else applies one frame of state.

## Why this package does not use Presentation.Camera

The core ships a 2.5D projection (`Aetherlight.Presentation.Camera`) for
backends that must map world coordinates to screen space themselves - a 2D
canvas, for instance. Unity already has a camera, so this package places sprites
in world space instead and lets the engine project them:

```
world = (tileX * tileSize, elevation * heightUnit, tileY * tileSize)
```

Elevation is simply height, and depth sorting falls out of the camera. That is
`IsoPlacement.World`, and it matches the mapping in `docs/HD2D.md`.

## Installing

Unity Hub has to create the project itself - it writes ProjectSettings tied to
your Editor version - so create it at `unity/UnityProject` (2D template, Unity
2021.3 LTS or newer), quit the Editor, then run:

```bash
node tools/unity-setup.mjs
```

That adds both packages to the manifest, copies the content pack into
`Assets/Resources`, and drops in a smoke-test script. It parses and
re-serializes the manifest rather than splicing text, so the missing-comma
failure that leaves a project Unity will not open cannot happen. Running it
twice changes nothing.

The rest of this section is what the script does, for when you would rather do
it by hand.

Add both packages to `Packages/manifest.json`:

```json
"com.aetherlight.core": "file:../../Aetherlight.Core",
"com.aetherlight.unity": "file:../../Aetherlight.Unity"
```

Copy the content pack somewhere Unity can read - it cannot reach outside
`Assets/`:

```bash
mkdir -p unity/UnityProject/Assets/Resources
cp content/starter.json unity/UnityProject/Assets/Resources/starter.json
```

## Scene setup

No scene or prefabs ship with the package. Scenes and prefabs are Editor-owned
YAML full of GUID cross-references; hand-writing them produces files that look
right and break subtly. Build them once in the Editor:

**1. Camera.** Position it looking down at the arena - a pitch of 35-50 degrees
reads as the diorama look. Orthographic suits pixel art; perspective with a
narrow FOV gives more depth. Point `BattleDirector.cameraTransform` at it.

**2. Numeral prefab.** Empty GameObject → add `TextMesh` → add `FloatingNumeral`.
Set the TextMesh anchor to Middle Center. Save as a prefab.

**3. Combatant prefab.** This is the one with structure worth getting right:

```
Combatant              <- CombatantView
├── SpritePivot        <- assign to `spritePivot`; billboards around Y
│   └── Sprite         <- SpriteRenderer, assign to `sprite`
├── NumeralAnchor      <- empty transform above the head
├── HpBarFill          <- a unit-width quad; scaled on X, so its pivot must be on its LEFT edge
├── AetherBarFill      <- same
└── NameLabel          <- TextMesh
```

The bar pivots matter. `CombatantView` scales those transforms on X to show a
fraction, so a centre-pivoted quad shrinks toward its middle instead of draining
from one end.

**4. HUD.** A GameObject with `BattleHud`. Assign a `TextMesh` for the banner,
and for the screen flash a quad parented to the camera and sized to cover the
view, with a `SpriteRenderer`.

**5. Director.** A GameObject with `BattleDirector`. Assign the content pack
TextAsset, the camera transform, the HUD, and optionally a `CueDispatcher`.

## Wiring combatants to the battle

Every `CombatantView` needs its `CombatantId` to match the id the battle
generates, and must register itself with the director. The ids are:

| Side | Format | Example |
| --- | --- | --- |
| Party | `party:{actorId}` | `party:rell` |
| Foe | `foe:{enemyId}:{index}` | `foe:thicket-crawler:0` |

The foe index is the position in the encounter's member list, not the slot.

A bootstrap script ties it together:

```csharp
using System.Collections.Generic;
using UnityEngine;
using Aetherlight.Domain;
using Aetherlight.Battle;
using Aetherlight.Unity;

public class BattleBootstrap : MonoBehaviour
{
    [SerializeField] private BattleDirector director;
    [SerializeField] private CombatantView[] views;

    void Start()
    {
        foreach (var view in views) director.RegisterView(view);

        var registry = director.Registry;
        var ctx = registry;

        var party = new List<ActorState>();
        foreach (var id in new[] { "rell", "maren" })
        {
            var actor = Actors.Create(registry.ActorDef(id), 12, ctx);
            actor.Motes.Add(new MoteInstance("boulder"));
            Actors.Restore(actor, ctx);   // motes raise max HP; top up before the fight
            party.Add(actor);
        }

        var encounter = new EncounterDef
        {
            Id = "road",
            Members =
            {
                new EncounterMember { EnemyId = "thicket-crawler", Slot = 0 },
                new EncounterMember { EnemyId = "ash-wisp", Slot = 1 },
            },
        };

        director.Begin(party, encounter, seed: "demo");
    }
}
```

Add `ScriptedPartyBrain` next to the director and the party will fight on its
own - useful for seeing the whole pipeline before any UI exists. Replace it by
subscribing to `BattleDirector.OnCommandsNeeded` yourself.

## Components

| Component | Responsibility |
| --- | --- |
| `BattleDirector` | Resolves rounds, schedules events, paces playback, applies frames |
| `CombatantView` | One combatant: pose, flash, shake, opacity, lunge, bars, numerals |
| `FloatingNumeral` | A damage or heal numeral, driven by timeline progress |
| `BattleHud` | Banner and screen flash |
| `CueDispatcher` | Maps named sound cues onto clips |
| `IsoPlacement` | Tile coordinates to world space; pixel snapping |
| `ScriptedPartyBrain` | Example command source, not a real AI |

## Rules that keep it honest

**Nothing here writes back into battle state.** The director resolves a round
and then only reads. Writing back breaks determinism, and with it replays and
netplay.

**No timing lives in the views.** A view applies the state it is handed. If
something needs to take longer, change the timeline's beat lengths
(`TimelineTiming`), not a view.

**Hitstop is central.** The director reads it from steps that just started and
freezes the clock. If each view decided independently, a hit would smear across
several frames instead of landing on one.

**Sound cues fire exactly once.** `Playback.JustStarted()` reports the window
since the last advance, so a cue cannot be dropped by a long frame or repeated
by a short one.

## Replacing the placeholder look

`CombatantView` draws a single `SpriteRenderer`. For 8-directional art, use
`Sprites.SpriteDirection(facing, cameraYaw)` to choose the sheet row and
`Sprites.FrameAt(clip, elapsed)` for the column; the rest of the pipeline is
unchanged. `docs/HD2D.md` covers billboarding modes, pixel snapping, and why the
crisp-sprite/soft-environment contrast is what sells the look.
