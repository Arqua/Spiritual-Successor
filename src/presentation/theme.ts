/**
 * Palette and visual tokens.
 *
 * Kept as data so a project can restyle the whole presentation without
 * touching renderer code, and so the same tokens can be read by a non-canvas
 * backend.
 */

import type { Element } from '../domain/elements.js';
import type { NumeralStyle } from './timeline.js';

export interface Theme {
  background: [string, string];
  floor: string;
  floorEdge: string;
  floorHighlight: string;
  gridLine: string;
  shadow: string;
  text: string;
  textDim: string;
  panel: string;
  panelEdge: string;
  hpBar: string;
  hpBarLow: string;
  aetherBar: string;
  barTrack: string;
  elements: Record<Element, string>;
  numerals: Record<NumeralStyle, string>;
  banner: Record<string, string>;
}

export const DEFAULT_THEME: Theme = {
  background: ['#141326', '#2a1f38'],
  floor: '#3c3357',
  floorEdge: '#544a73',
  floorHighlight: '#4a4068',
  gridLine: '#2b2440',
  shadow: 'rgba(8, 6, 18, 0.45)',
  text: '#f2ecff',
  textDim: '#a99fc4',
  panel: 'rgba(20, 17, 38, 0.82)',
  panelEdge: '#5b5080',
  hpBar: '#5fd97a',
  hpBarLow: '#e2565c',
  aetherBar: '#5aa9f0',
  barTrack: '#241f3a',
  elements: {
    terra: '#8f6b3f',
    pyre: '#e0603a',
    aeris: '#63c9a8',
    rime: '#5c9fe0',
  },
  numerals: {
    damage: '#ffffff',
    critical: '#ffd447',
    strong: '#ff9d4a',
    weak: '#9d95b8',
    heal: '#6fe08c',
    miss: '#b9b1d0',
    status: '#c78ce8',
    aether: '#63b6f5',
  },
  banner: {
    neutral: '#cfc6e8',
    good: '#7fe39a',
    bad: '#f0757a',
    grand: '#ffd447',
  },
};

/** Colour for an element, falling back to plain white for typeless. */
export function elementColor(theme: Theme, element: Element | null | string): string {
  if (!element) return '#ffffff';
  return theme.elements[element as Element] ?? '#ffffff';
}
