import { describe, expect, it } from 'vitest';
import { Playback, foldRenderState, foldSceneState } from '../src/presentation/playback.js';
import { GLOBAL_TRACK, type AnimationStep, type Timeline } from '../src/presentation/timeline.js';

function timeline(steps: AnimationStep[]): Timeline {
  return { steps, durationMs: steps.reduce((m, s) => Math.max(m, s.startMs + s.durationMs), 0) };
}

describe('playback clock', () => {
  it('starts finished when there is nothing to play', () => {
    const playback = new Playback();
    playback.load(timeline([]));
    expect(playback.isFinished).toBe(true);
  });

  it('reports steps active at the current time and not before', () => {
    const playback = new Playback();
    playback.load(timeline([
      { track: 'a', startMs: 100, durationMs: 100, action: { kind: 'pose', pose: 'attack' } },
    ]));
    expect(playback.advance(50)).toHaveLength(0);
    expect(playback.advance(100)).toHaveLength(1);
  });

  it('drops a step once it has elapsed', () => {
    const playback = new Playback();
    playback.load(timeline([
      { track: 'a', startMs: 0, durationMs: 100, action: { kind: 'pose', pose: 'attack' } },
    ]));
    playback.advance(50);
    expect(playback.activeAt(150)).toHaveLength(0);
  });

  it('reports normalized progress through a step', () => {
    const playback = new Playback();
    playback.load(timeline([
      { track: 'a', startMs: 0, durationMs: 200, action: { kind: 'pose', pose: 'attack' } },
    ]));
    const active = playback.advance(100);
    expect(active[0]?.t).toBeCloseTo(0.5, 5);
  });

  it('clamps to the end and finishes', () => {
    const playback = new Playback();
    playback.load(timeline([
      { track: 'a', startMs: 0, durationMs: 100, action: { kind: 'pose', pose: 'attack' } },
    ]));
    playback.advance(10_000);
    expect(playback.isFinished).toBe(true);
    expect(playback.now).toBe(100);
    expect(playback.progress).toBe(1);
  });

  it('freezes the clock during hitstop, then resumes', () => {
    const playback = new Playback();
    playback.load(timeline([
      { track: 'a', startMs: 0, durationMs: 1000, action: { kind: 'pose', pose: 'attack' } },
    ]));
    playback.requestHitstop(100);
    playback.advance(60);
    // The whole frame was eaten by the freeze.
    expect(playback.now).toBe(0);
    playback.advance(60);
    // 40ms of freeze left, so 20ms reaches the clock.
    expect(playback.now).toBeCloseTo(20, 5);
  });

  it('does not stack hitstop requests beyond the longest', () => {
    const playback = new Playback();
    playback.load(timeline([
      { track: 'a', startMs: 0, durationMs: 1000, action: { kind: 'pose', pose: 'attack' } },
    ]));
    playback.requestHitstop(50);
    playback.requestHitstop(80);
    playback.advance(80);
    expect(playback.now).toBe(0);
    playback.advance(10);
    expect(playback.now).toBeCloseTo(10, 5);
  });

  it('honours the speed multiplier', () => {
    const playback = new Playback();
    playback.load(timeline([
      { track: 'a', startMs: 0, durationMs: 1000, action: { kind: 'pose', pose: 'attack' } },
    ]));
    playback.speed = 2;
    playback.advance(100);
    expect(playback.now).toBe(200);
  });

  it('skips straight to the end', () => {
    const playback = new Playback();
    playback.load(timeline([
      { track: 'a', startMs: 0, durationMs: 5000, action: { kind: 'pose', pose: 'attack' } },
    ]));
    playback.skip();
    expect(playback.isFinished).toBe(true);
    expect(playback.now).toBe(5000);
  });

  it('filters steps by track', () => {
    const playback = new Playback();
    playback.load(timeline([
      { track: 'a', startMs: 0, durationMs: 100, action: { kind: 'pose', pose: 'attack' } },
      { track: 'b', startMs: 0, durationMs: 100, action: { kind: 'pose', pose: 'hurt' } },
    ]));
    playback.advance(50);
    expect(playback.activeForTrack('a')).toHaveLength(1);
  });

  it('fires a cue scheduled at time zero on the first frame', () => {
    // An exclusive lower bound here would silently swallow the opening cue of
    // every round, since timelines almost always start a step at t=0.
    const playback = new Playback();
    playback.load(timeline([
      { track: GLOBAL_TRACK, startMs: 0, durationMs: 1, action: { kind: 'sfx', cue: 'hit' } },
      { track: GLOBAL_TRACK, startMs: 500, durationMs: 1, action: { kind: 'sfx', cue: 'down' } },
    ]));
    playback.advance(20);
    expect(playback.justStarted().map((s) => (s.action as { cue: string }).cue)).toEqual(['hit']);
  });

  it('fires each cue exactly once across many frames', () => {
    const playback = new Playback();
    playback.load(timeline([
      { track: GLOBAL_TRACK, startMs: 0, durationMs: 1, action: { kind: 'sfx', cue: 'a' } },
      { track: GLOBAL_TRACK, startMs: 30, durationMs: 1, action: { kind: 'sfx', cue: 'b' } },
      { track: GLOBAL_TRACK, startMs: 200, durationMs: 1, action: { kind: 'sfx', cue: 'c' } },
    ]));
    const fired: string[] = [];
    for (let i = 0; i < 40; i++) {
      playback.advance(16.667);
      for (const step of playback.justStarted()) fired.push((step.action as { cue: string }).cue);
    }
    expect(fired).toEqual(['a', 'b', 'c']);
  });
});

describe('folding concurrent steps', () => {
  it('combines a flash, a shake and a pose into one state', () => {
    const state = foldRenderState([
      { step: { track: 'a', startMs: 0, durationMs: 100, action: { kind: 'flash', color: '#fff', intensity: 1 } }, t: 0, elapsedMs: 0 },
      { step: { track: 'a', startMs: 0, durationMs: 100, action: { kind: 'shake', intensity: 5 } }, t: 0.25, elapsedMs: 25 },
      { step: { track: 'a', startMs: 0, durationMs: 100, action: { kind: 'pose', pose: 'hurt' } }, t: 0, elapsedMs: 0 },
    ]);
    expect(state.pose).toBe('hurt');
    expect(state.flashIntensity).toBeGreaterThan(0);
    expect(state.shakeOffset).not.toBe(0);
  });

  it('decays a flash to nothing by the end of its step', () => {
    const at = (t: number) => foldRenderState([
      { step: { track: 'a', startMs: 0, durationMs: 100, action: { kind: 'flash', color: '#fff', intensity: 1 } }, t, elapsedMs: t * 100 },
    ]).flashIntensity;
    expect(at(0)).toBeCloseTo(1, 5);
    expect(at(1)).toBeCloseTo(0, 5);
  });

  it('settles a shake as it completes', () => {
    const at = (t: number) => Math.abs(foldRenderState([
      { step: { track: 'a', startMs: 0, durationMs: 100, action: { kind: 'shake', intensity: 8 } }, t, elapsedMs: t * 100 },
    ]).shakeOffset);
    expect(at(1)).toBeCloseTo(0, 5);
    expect(at(0.99)).toBeLessThan(1);
  });

  it('interpolates a bar between its endpoints', () => {
    const at = (t: number) => foldRenderState([
      { step: { track: 'a', startMs: 0, durationMs: 100, action: { kind: 'bar', stat: 'hp', from: 100, to: 50, max: 100 } }, t, elapsedMs: t * 100 },
    ]).bars[0]?.value;
    expect(at(0)).toBe(100);
    expect(at(1)).toBe(50);
    expect(at(0.5)!).toBeLessThan(100);
    expect(at(0.5)!).toBeGreaterThan(50);
  });

  it('fades opacity while collapsing', () => {
    const state = foldRenderState([
      { step: { track: 'a', startMs: 0, durationMs: 100, action: { kind: 'collapse' } }, t: 1, elapsedMs: 100 },
    ]);
    expect(state.opacity).toBeLessThan(1);
    expect(state.pose).toBe('down');
  });

  it('returns a neutral state when nothing is active', () => {
    const state = foldRenderState([]);
    expect(state.pose).toBe('idle');
    expect(state.opacity).toBe(1);
    expect(state.numerals).toEqual([]);
  });

  it('folds the global track into scene state', () => {
    const scene = foldSceneState([
      { step: { track: GLOBAL_TRACK, startMs: 0, durationMs: 100, action: { kind: 'banner', text: 'Victory', tone: 'good' } }, t: 0.5, elapsedMs: 50 },
      { step: { track: GLOBAL_TRACK, startMs: 0, durationMs: 100, action: { kind: 'hitstop' } }, t: 0, elapsedMs: 0 },
      { step: { track: 'a', startMs: 0, durationMs: 100, action: { kind: 'pose', pose: 'idle' } }, t: 0, elapsedMs: 0 },
    ]);
    expect(scene.banner).toMatchObject({ text: 'Victory', tone: 'good' });
    expect(scene.hitstopActive).toBe(true);
  });
});
