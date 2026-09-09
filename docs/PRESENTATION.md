# The presentation layer

An implementation of the contract described in `HD2D.md`. It turns the rules
core's event stream into a paced, animated scene.

The layer is split so that only one file knows what a browser is:

```
src/presentation/
  easing.ts       pure    easing curves
  camera.ts       pure    2.5D projection, framing, follow
  sprites.ts      pure    8-way direction, billboarding, clip timing
  timeline.ts     pure    events -> a timed schedule
  playback.ts     pure    the clock, hitstop, folding concurrent steps
  theme.ts        data    palette and visual tokens
  renderer/
    canvas2d.ts   web     the only DOM-dependent module
```

Everything marked `pure` has no engine or DOM dependency and ports unchanged.
The split is enforced by the build, not by convention: `tsconfig.json` compiles
the core and pure layers **without** the DOM lib, so importing a browser API
into them fails the typecheck. `tsconfig.browser.json` adds the DOM and covers
the renderer and demo.

## The pipeline

```
resolveRound()  ->  events  ->  buildTimeline()  ->  Playback  ->  renderer
   instant                       schedule            paces it      draws it
```

1. **`buildTimeline(events, options)`** walks the event list and emits
   `AnimationStep`s, each with a `track`, a `startMs` and a `durationMs`.
   Concurrency is expressed as overlapping time ranges: an all-foes spell hits
   every target on the same beat because the cursor only advances between
   beats. It is a pure function, so a schedule can be asserted in a test.

2. **`Playback`** owns the clock. Call `advance(dt)` each frame and it returns
   the steps active now, each with its normalized progress `t`.

3. **`foldRenderState(active)`** collapses the several steps that can target
   one entity at once - a flash *and* a shake *and* a pose - into the single
   set of values a sprite needs. `foldSceneState` does the same for the global
   track.

4. The renderer draws that state. It makes no decisions about timing.

## Details worth knowing

**Bars animate from the pre-hit value.** `buildTimeline` takes a `vitals`
snapshot and tracks HP as it walks, so each bar step carries a real `from` and
`to`. Successive hits chain correctly, and values are clamped to `[0, max]`.

**Effectiveness drives feedback, and is not recomputed.** The `damage` event
already carries `effective`, the elemental multiplier the rules layer worked
out. `damageStyle()` reads it to pick a numeral style. The presentation layer
never redoes affinity maths.

**Hitstop freezes the whole scene.** It is handled in `Playback`, not per
entity - it eats frame time before the clock sees it. If each sprite decided
independently, a hit would smear instead of landing.

**One-shot cues fire exactly once.** `justStarted()` reports steps whose start
falls in `(previous clock, current clock]`, measured from the actual last
advance rather than a caller-supplied window. A supplied window either drops
cues when it is shorter than the frame delta or repeats them when it is longer,
and an exclusive lower bound at zero silently swallows the opening cue of every
round.

**Elevation is the whole trick.** `project()` maps `(x, y, z)` to screen space
and returns a `depth` key for painter's-algorithm sorting. Depth ties break by
elevation so a raised object draws in front of a lower one on the same tile
line. `offsetY` frames the scene: sprites draw upward from their ground point,
so a view centred on the ground plane sits high with dead space beneath it.

**Pixel snapping is not optional.** `snapToPixel()` rounds projected positions
to whole pixels. Sub-pixel sprite placement under a moving camera is what makes
pixel art shimmer.

## Running the demo

```bash
npm run demo          # bundles demo/main.ts -> demo/bundle.js
npx http-server -p 8099 .
# then open http://127.0.0.1:8099/demo/index.html
```

It must be served over HTTP. ES modules do not load from `file://`.

The demo drives the real core - no mocks, no scripted animation. It resolves
actual rounds, builds real timelines, and plays them back. Character art is
procedural: the library ships no assets. Replace `drawActor` with a sprite
sheet blit, using `spriteDirection()` for the row and `frameAt()` for the
column, and nothing else in the pipeline changes.

## Porting to another engine

Reimplement or transliterate the pure modules, then write one backend in place
of `renderer/canvas2d.ts`. The order that minimises churn:

1. `easing`, `camera`, `sprites` - small, pure, no dependencies.
2. `timeline` - the schedule. Port `test/timeline.test.ts` alongside it.
3. `playback` - the clock and the folds. Port `test/playback.test.ts`; the
   hitstop and cue-firing assertions are the ones that catch real mistakes.
4. Your renderer.

The 47 tests covering these modules are written as a specification. If your
port passes the same assertions, it behaves identically.
