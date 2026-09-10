import { describe, expect, it } from 'vitest';
import type { GameEvent } from '../src/core/events.js';
import { buildTimeline, damageStyle, GLOBAL_TRACK } from '../src/presentation/timeline.js';

const vitals = {
  'party:rell': { hp: 100, maxHp: 100, aether: 30, maxAether: 30 },
  'foe:slime:0': { hp: 80, maxHp: 80, aether: 0, maxAether: 0 },
};

function stepsOf(timeline: ReturnType<typeof buildTimeline>, kind: string) {
  return timeline.steps.filter((s) => s.action.kind === kind);
}

describe('timeline construction', () => {
  it('produces nothing for an empty event list', () => {
    const timeline = buildTimeline([]);
    expect(timeline.steps).toEqual([]);
    expect(timeline.durationMs).toBe(0);
  });

  it('schedules steps in non-decreasing start order for sequential events', () => {
    const events: GameEvent[] = [
      { type: 'turn-start', combatantId: 'party:rell' },
      { type: 'action-declared', combatantId: 'party:rell', action: 'attack', targetIds: ['foe:slime:0'] },
      { type: 'damage', sourceId: 'party:rell', targetId: 'foe:slime:0', amount: 12, element: null, critical: false, effective: 1 },
    ];
    const timeline = buildTimeline(events, { vitals });
    const starts = timeline.steps.map((s) => s.startMs);
    // A damage beat emits several concurrent steps, so equality is expected;
    // what matters is that time never runs backwards.
    for (let i = 1; i < starts.length; i++) {
      expect(starts[i]!).toBeGreaterThanOrEqual(starts[i - 1]!);
    }
  });

  it('emits an impact cluster on the same beat', () => {
    const events: GameEvent[] = [
      { type: 'damage', sourceId: 'a', targetId: 'foe:slime:0', amount: 12, element: 'pyre', critical: false, effective: 1 },
    ];
    const timeline = buildTimeline(events, { vitals });
    const impactKinds = timeline.steps.filter((s) => s.startMs === 0).map((s) => s.action.kind);
    expect(impactKinds).toContain('flash');
    expect(impactKinds).toContain('shake');
    expect(impactKinds).toContain('numeral');
    expect(impactKinds).toContain('hitstop');
    expect(impactKinds).toContain('bar');
  });

  it('animates the HP bar from the pre-hit value', () => {
    const events: GameEvent[] = [
      { type: 'damage', sourceId: 'a', targetId: 'foe:slime:0', amount: 30, element: null, critical: false, effective: 1 },
    ];
    const bar = stepsOf(buildTimeline(events, { vitals }), 'bar')[0];
    expect(bar?.action).toMatchObject({ stat: 'hp', from: 80, to: 50, max: 80 });
  });

  it('chains successive hits from the running HP value', () => {
    const events: GameEvent[] = [
      { type: 'damage', sourceId: 'a', targetId: 'foe:slime:0', amount: 30, element: null, critical: false, effective: 1 },
      { type: 'damage', sourceId: 'a', targetId: 'foe:slime:0', amount: 20, element: null, critical: false, effective: 1 },
    ];
    const bars = stepsOf(buildTimeline(events, { vitals }), 'bar');
    expect(bars[0]?.action).toMatchObject({ from: 80, to: 50 });
    expect(bars[1]?.action).toMatchObject({ from: 50, to: 30 });
  });

  it('never drives a bar below zero or above max', () => {
    const events: GameEvent[] = [
      { type: 'damage', sourceId: 'a', targetId: 'foe:slime:0', amount: 9999, element: null, critical: false, effective: 1 },
      { type: 'heal', sourceId: 'a', targetId: 'foe:slime:0', amount: 9999 },
    ];
    const bars = stepsOf(buildTimeline(events, { vitals }), 'bar');
    expect(bars[0]?.action).toMatchObject({ to: 0 });
    expect(bars[1]?.action).toMatchObject({ to: 80 });
  });

  it('does not emit bar steps for unknown combatants', () => {
    const events: GameEvent[] = [
      { type: 'damage', sourceId: 'a', targetId: 'nobody', amount: 5, element: null, critical: false, effective: 1 },
    ];
    expect(stepsOf(buildTimeline(events, { vitals }), 'bar')).toHaveLength(0);
  });

  it('gives a summon a long cutaway and a screen flash', () => {
    const events: GameEvent[] = [
      { type: 'summon-called', combatantId: 'party:rell', summonId: 'stonewarden', motesSpent: 2 },
    ];
    const timeline = buildTimeline(events, { vitals, labels: { stonewarden: 'Stonewarden' } });
    expect(stepsOf(timeline, 'screen-flash')).toHaveLength(1);
    const banner = stepsOf(timeline, 'banner')[0];
    expect(banner?.action).toMatchObject({ text: 'Stonewarden', tone: 'grand' });
    expect(timeline.durationMs).toBeGreaterThan(1000);
  });

  it('routes scene-wide steps onto the global track', () => {
    const events: GameEvent[] = [{ type: 'battle-ended', outcome: 'victory' }];
    const timeline = buildTimeline(events);
    expect(timeline.steps.every((s) => s.track === GLOBAL_TRACK)).toBe(true);
  });

  it('reports a duration covering the last step', () => {
    const events: GameEvent[] = [
      { type: 'damage', sourceId: 'a', targetId: 'foe:slime:0', amount: 5, element: null, critical: false, effective: 1 },
    ];
    const timeline = buildTimeline(events, { vitals });
    const last = Math.max(...timeline.steps.map((s) => s.startMs + s.durationMs));
    expect(timeline.durationMs).toBe(last);
  });

  it('is deterministic', () => {
    const events: GameEvent[] = [
      { type: 'turn-start', combatantId: 'party:rell' },
      { type: 'damage', sourceId: 'party:rell', targetId: 'foe:slime:0', amount: 12, element: 'pyre', critical: true, effective: 1.4 },
      { type: 'downed', combatantId: 'foe:slime:0' },
    ];
    expect(buildTimeline(events, { vitals })).toEqual(buildTimeline(events, { vitals }));
  });

  it('honours custom timing', () => {
    const events: GameEvent[] = [
      { type: 'damage', sourceId: 'a', targetId: 'foe:slime:0', amount: 5, element: null, critical: false, effective: 1 },
    ];
    const fast = buildTimeline(events, { vitals, timing: { impact: 50, numeralFloat: 60 } });
    const slow = buildTimeline(events, { vitals });
    expect(fast.durationMs).toBeLessThan(slow.durationMs);
  });
});

describe('numeral styling', () => {
  it('reads effectiveness off the event rather than recomputing it', () => {
    expect(damageStyle(true, 1)).toBe('critical');
    expect(damageStyle(false, 1.5)).toBe('strong');
    expect(damageStyle(false, 0.5)).toBe('weak');
    expect(damageStyle(false, 1)).toBe('damage');
  });

  it('prefers critical over effectiveness', () => {
    expect(damageStyle(true, 0.4)).toBe('critical');
  });
});
