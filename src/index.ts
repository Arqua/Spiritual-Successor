/**
 * Aetherlight Core - engine-agnostic rules for a classic-style elemental JRPG.
 *
 * Nothing in this package renders, loads assets, or reads input. It computes
 * rules and emits events. Bind it to whatever engine you like; see
 * docs/INTEGRATION.md.
 */

// Core
export * from './core/rng.js';
export * from './core/events.js';
export * from './core/registry.js';

// Domain
export * from './domain/elements.js';
export * from './domain/stats.js';
export * from './domain/status.js';
export * from './domain/motes.js';
export * from './domain/classes.js';
export * from './domain/arts.js';
export * from './domain/equipment.js';
export * from './domain/summons.js';
export * from './domain/actor.js';
export * from './domain/enemy.js';

// Battle
export * from './battle/state.js';
export * from './battle/damage.js';
export * from './battle/order.js';
export * from './battle/resolve.js';
export * from './battle/setup.js';
export * from './battle/ai.js';

// Field
export * from './field/grid.js';
export * from './field/interactables.js';
export * from './field/abilities.js';

// Save
export * from './save/serialize.js';
