# The 2.5D presentation contract

The rules layer has no idea what a sprite is. It computes state and emits
events; your renderer turns those into a scene. This document defines the
contract between the two, and the specific hooks that a 2.5D / HD-2D
presentation (pixel-art sprites composited into a perspective 3D world) needs.

Nothing here is required to *run* the core. It is required to make the core
look like the game you want.

## The one rule

**The core never blocks on animation, and the renderer never mutates state.**

`resolveRound()` computes an entire round instantly and hands you an ordered
event list. The fight is already over by the time you draw the first hit. Your
renderer walks that list at whatever pace looks good, and the player sees a
paced battle. If you let the renderer write back into battle state, you lose
determinism and replay, and desync becomes possible in netplay.

```
resolveRound()  ->  [event, event, event, ...]  ->  animation queue  ->  frames
   (instant)              (ordered)                   (your pace)
```

## Consuming the event stream

```ts
const { events } = resolveRound(state, commands, registry);
await playback.enqueue(events);   // your code
```

A minimal playback loop maps each event type to a coroutine and awaits it:

| Event | Typical presentation |
| --- | --- |
| `turn-start` | Move the camera to the actor, raise their sprite slightly |
| `action-declared` | Play the wind-up; show the art name banner |
| `damage` | Impact flash, hit-stop, damage numeral, HP bar drain |
| `heal` | Green numeral, restorative particle burst |
| `miss` | "Miss" numeral, dodge-step animation |
| `status-applied` | Attach the status icon and its looping VFX |
| `mote-unleashed` | The mote's spirit leaves the character, elemental burst |
| `class-changed` | Costume/palette swap, class-name flourish |
| `summon-called` | Full-screen cutaway; this is the set-piece moment |
| `downed` | Collapse animation, desaturate the sprite |
| `battle-ended` | Victory pose or game-over transition |

`damage` carries `effective`, the elemental multiplier. Use it to pick the
feedback: above ~1.2 is a "strong" hit (bigger numeral, brighter flash), below
~0.8 is "resisted" (dull thud, grey numeral). This is why the field exists.

## Elevation is the whole trick

`Tile.elevation` is the single number that makes a flat rules grid render as a
layered 3D scene. The core uses it for movement legality; the renderer uses it
for placement.

```
world_position = (tile.x * TILE, tile.elevation * HEIGHT_UNIT, tile.y * TILE)
```

Pick `HEIGHT_UNIT` as a fraction of `TILE` — commonly 1/2 — so a one-step rise
reads clearly without dwarfing the sprites. `Grid.canStep()` already encodes
the ledge rule: you may drop further than you may climb. That asymmetry is what
makes elevated maps navigable rather than annoying.

Field abilities that change `elevation` (`raise`, `lower`, `freeze`, `grow`)
return `FieldChange` records with `from` and `to` values. Tween between them —
that is the terrain-morph animation, and you get it for free.

## Sprite billboarding

Pixel-art characters in a perspective camera must face the viewer or they
shear. Three options, in increasing fidelity:

1. **Full billboard** — the sprite always faces the camera exactly. Simplest;
   characters appear to swivel when the camera orbits.
2. **Y-axis billboard** (recommended) — rotate only around the vertical axis,
   so sprites stay upright and grounded. Shadows and contact points stay put.
3. **Locked-angle** — do not billboard; author 8-directional sprites and pick
   the one nearest the camera-relative facing. Most authentic, most art.

For option 3, derive the sprite direction from `Facing` plus the camera yaw:

```ts
const relative = (facingAngle(facing) - cameraYaw + 360) % 360;
const spriteIndex = Math.round(relative / 45) % 8;
```

## Keeping pixels crisp

The classic HD-2D failure is shimmering sprites. Three rules:

- Render sprites to an internal buffer at an integer multiple of your art's
  native resolution, then upscale once with nearest-neighbour.
- Snap sprite world positions to a virtual pixel grid before drawing. Sub-pixel
  camera motion is what causes crawl.
- Use point filtering with mipmaps disabled on all sprite atlases. Let depth of
  field and bloom do the softening instead — that contrast between crisp
  sprites and soft environment is the entire HD-2D look.

## Depth, lighting, and the diorama look

- Give sprites a real depth value so they occlude correctly against geometry,
  but render them unlit or half-lit — pixel art carries its own shading, and
  full scene lighting flattens it. A common compromise is to tint sprites by
  ambient colour only.
- Tilt-shift depth of field on the far and near planes is what sells the
  "miniature diorama" reading of the scene.
- Keep the camera at a fixed pitch (35-50 degrees works) and allow only yaw
  snapping in 90-degree steps, so authored 8-directional art always lines up.

## Where the renderer gets its data

The core deliberately carries only opaque string ids: `ActorDef.spriteId`,
`ActorDef.portraitId`, `EnemyDef.spriteId`, `FieldEntity.spriteId`,
`EncounterDef.backgroundId`, `EncounterDef.musicId`. The rules layer never
reads them. Keep the id-to-asset mapping in your engine's own resource table;
that way art can be reorganised without touching content packs, and the same
content runs in a text client with no assets at all.
