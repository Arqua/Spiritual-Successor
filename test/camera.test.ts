import { describe, expect, it } from 'vitest';
import { defaultCamera, followCamera, isVisible, project, snapToPixel, unproject } from '../src/presentation/camera.js';

const camera = { ...defaultCamera(640, 360), focus: { x: 0, y: 0, z: 0 } };

describe('2.5D projection', () => {
  it('puts the focus point at the viewport centre', () => {
    const point = project({ x: 0, y: 0, z: 0 }, camera);
    expect(point.x).toBe(320);
    expect(point.y).toBe(180);
  });

  it('separates the two ground axes horizontally in opposite directions', () => {
    const east = project({ x: 1, y: 0, z: 0 }, camera);
    const south = project({ x: 0, y: 1, z: 0 }, camera);
    expect(east.x).toBeGreaterThan(320);
    expect(south.x).toBeLessThan(320);
    // Both move down the screen by the same amount.
    expect(east.y).toBe(south.y);
  });

  it('raises elevated points up the screen', () => {
    const ground = project({ x: 0, y: 0, z: 0 }, camera);
    const raised = project({ x: 0, y: 0, z: 2 }, camera);
    expect(raised.y).toBeLessThan(ground.y);
  });

  it('sorts nearer tiles in front', () => {
    const near = project({ x: 3, y: 3, z: 0 }, camera);
    const far = project({ x: 0, y: 0, z: 0 }, camera);
    expect(near.depth).toBeGreaterThan(far.depth);
  });

  it('breaks depth ties by elevation so higher objects draw in front', () => {
    const low = project({ x: 1, y: 1, z: 0 }, camera);
    const high = project({ x: 1, y: 1, z: 3 }, camera);
    expect(high.depth).toBeGreaterThan(low.depth);
  });

  it('round-trips through unproject on the ground plane', () => {
    const world = { x: 4, y: -2, z: 0 };
    const screen = project(world, camera);
    const back = unproject(screen.x, screen.y, camera, 0);
    expect(back.x).toBeCloseTo(world.x, 6);
    expect(back.y).toBeCloseTo(world.y, 6);
  });

  it('round-trips on an elevated plane', () => {
    const world = { x: 2, y: 5, z: 3 };
    const screen = project(world, camera);
    const back = unproject(screen.x, screen.y, camera, 3);
    expect(back.x).toBeCloseTo(world.x, 6);
    expect(back.y).toBeCloseTo(world.y, 6);
  });

  it('snaps to whole pixels so sprites do not shimmer', () => {
    const snapped = snapToPixel({ x: 10.4, y: 20.6, depth: 5 });
    expect(snapped.x).toBe(10);
    expect(snapped.y).toBe(21);
    expect(Number.isInteger(snapped.x) && Number.isInteger(snapped.y)).toBe(true);
  });

  it('follows a target without overshooting it', () => {
    let cam = camera;
    for (let i = 0; i < 200; i++) cam = followCamera(cam, { x: 10, y: 10, z: 0 }, 0.1, 16.667);
    expect(cam.focus.x).toBeCloseTo(10, 1);
    expect(cam.focus.y).toBeCloseTo(10, 1);
  });

  it('follows at the same rate regardless of framerate', () => {
    let slow = camera;
    let fast = camera;
    // One 100ms step versus six ~16.7ms steps covering the same wall time.
    slow = followCamera(slow, { x: 10, y: 0, z: 0 }, 0.1, 100);
    for (let i = 0; i < 6; i++) fast = followCamera(fast, { x: 10, y: 0, z: 0 }, 0.1, 16.667);
    expect(slow.focus.x).toBeCloseTo(fast.focus.x, 1);
  });

  it('culls points outside the viewport', () => {
    expect(isVisible(project({ x: 0, y: 0, z: 0 }, camera), camera)).toBe(true);
    expect(isVisible(project({ x: 500, y: 500, z: 0 }, camera), camera)).toBe(false);
  });
});

describe('framing offset', () => {
  it('shifts the projected world without moving the focus', () => {
    const shifted = { ...camera, offsetY: 40 };
    const base = project({ x: 0, y: 0, z: 0 }, camera);
    const moved = project({ x: 0, y: 0, z: 0 }, shifted);
    expect(moved.y - base.y).toBe(40);
    expect(moved.x).toBe(base.x);
  });

  it('still round-trips through unproject with an offset applied', () => {
    const shifted = { ...camera, offsetY: 40 };
    const world = { x: 3, y: -1, z: 0 };
    const screen = project(world, shifted);
    const back = unproject(screen.x, screen.y, shifted, 0);
    expect(back.x).toBeCloseTo(world.x, 6);
    expect(back.y).toBeCloseTo(world.y, 6);
  });

  it('treats a missing offset as zero', () => {
    const without = { ...camera };
    delete (without as { offsetY?: number }).offsetY;
    expect(project({ x: 0, y: 0, z: 0 }, without).y).toBe(project({ x: 0, y: 0, z: 0 }, { ...camera, offsetY: 0 }).y);
  });
});
