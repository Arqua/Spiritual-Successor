# Mechanics specification

This is the design document for the rules the library implements. It is
written so that someone could reimplement the system in another language from
this text plus the test suite, without reading the TypeScript.

## Elements

Four affinities: **terra**, **pyre**, **aeris**, **rime**. They pair into
opposites — terra/aeris and pyre/rime.

There is no type-effectiveness chart. Every actor carries a `power` and a
`resist` value per element, and effectiveness is the gap between them:

```
factor = clamp(1 + (attackerPower + opposedBonus - defenderResist) / 200, 0.25, 2.0)
```

`opposedBonus` (default 25) applies when the strike's element opposes the
defender's own alignment. This means an actor aligned to one element is
naturally soft against its opposite without any pairwise table, and gear that
grants resistance is meaningful against every source of that element.

## Stats

Six scalars — `maxHp`, `maxAether`, `attack`, `defense`, `agility`, `luck` —
plus a `power` and `resist` value for each of the four elements.

**Aether** is the resource arts are cast from.

Growth is a formula, not a table:

```
value(level) = base + gain * (level - 1) ^ curve
```

`curve` below 1 front-loads growth; above 1 back-loads it. Four numbers per
stat instead of ninety-nine rows, and it stays readable in a JSON pack.

Derived stats are always recomputed, never stored, in a fixed order:

1. level growth curve
2. class multipliers
3. bonuses from bound motes
4. equipment
5. status effects
6. temporary battle buffs

Fixing the order matters: multiplication before addition means a class
multiplier scales base growth but not the flat bonus from a ring, which keeps
late-game gear from being multiplied into absurdity.

## Motes

Motes are bindable elemental spirits — the system the whole build is organised
around. Each sits in one of three states:

- **set** — bound to an actor. Contributes its stat bonus, and counts toward
  the actor's class.
- **standby** — unleashed during this battle. Bonus and class contribution are
  gone, but it can now be spent on a summon.
- **recovering** — spent on a summon. Returns to `set` after a few rounds.

The loop is the interesting part. Unleashing a mote is an immediate tactical
gain that costs you stats and *can knock you out of your class mid-fight*.
Summoning cashes those standby motes in for a large hit at the price of a
longer downtime. Every choice is a real trade, and none of it is permanent.

Between battles, all motes return to `set`.

## Classes

**A class is derived, never assigned.** It is a pure function of the actor's
innate element and the count of set motes per element.

Resolution: gather every class whose requirements are met, take the highest
`priority`; ties break toward the more specific requirement, then by id so the
result is deterministic.

Requirements are expressed as minimum and maximum set-mote counts per element,
plus an optional minimum total. A "pure" class sets maxima of 0 on the other
three elements; a hybrid sets minima on two; a capstone class requires at least
one of all four.

Because nothing stores the class, it cannot fall out of sync with the motes,
and mid-battle class changes fall out of the system for free rather than
needing special handling.

Classes contribute stat multipliers and an art list gated on level.

## Arts

Arts are the active abilities, paid for with aether. One definition serves both
battle and the overworld: an art may carry a battle effect, a `fieldEffect`, or
both — which is what makes the exploration kit and the combat kit feel like one
toolkit rather than two disjoint lists.

Targeting covers single and whole-side targets plus `spread-foes`, which hits a
primary target fully and neighbours at a falloff based on formation slot
distance.

## Damage

Two families, both mitigated **asymptotically** rather than subtractively:

```
mitigation(d) = softening / (softening + d)      // softening default 100

physical:  attack * physicalScale * power * mitigation(defense)
aetheric:  power * mitigation(defense * aethericDefenseWeight)
```

Then, in order: elemental factor, spread falloff, critical multiplier, the
defending multiplier, and a random variance of ±8%. Minimum 1 damage.

The asymptotic form is a deliberate correction of the obvious subtractive
formula (`attack - defense`). Subtraction has a cliff: once defense exceeds
attack, damage collapses to the minimum and armour becomes flat immunity. In
testing, a level-3 party in starting gear was taking exactly 1 damage per hit
from a boss — unkillable, by accident. With asymptotic mitigation, defense has
diminishing returns but never becomes a wall, and every point of attack and
defense keeps mattering at any scale.

Defense applies fully to physical damage but only fractionally (35% by default)
to aetheric, so heavy armour is not an answer to a well-aimed spell.

Critical hits scale with luck. Evasion scales with the agility gap and is
capped (25% by default) so agility stacking cannot make a character
untouchable.

## Turn order

Commands for the whole round are chosen first, then everyone acts in one
ordered pass.

Order is agility with ±15% jitter, with action priority tiers layered on top —
defending and fleeing resolve first, then items, then mote unleashes, summons,
and finally arts by their own priority. A fast healer is usually first but
never reliably so, and a priority move beats raw speed outright.

Order is recomputed each round rather than kept as a persistent queue, so a
speed buff applied this round is felt next round — easy to reason about, easy
to display.

## Summons

A summon is bought with motes already on standby, so its cost was paid a turn
or more earlier by unleashing them. Cost is specified per element, so a summon
can demand a particular mix and the party must plan which motes to spend.

```
damage = basePower + min(hpFractionCap, hpFractionPerMote * motesSpent) * targetMaxHp
```

Scaling off the target's max HP keeps summons relevant against high-HP bosses;
capping that portion stops them from deleting trash. Some summons leave an
afterglow — temporary elemental power for the summoner.

## Status effects

Statuses carry a duration (a round count, the battle, or persistent until
cured), optional stat modifiers, per-round degeneration or regeneration, and
flags for preventing actions or sealing arts. Exclusive groups let a new
control effect replace an old one rather than stacking.

Landing chance is tempered by the target's luck — a soft counter, never a hard
immunity:

```
chance = base * (1 - statusResist) * (1 - min(0.75, luck / 400))
```

## The field

The overworld grid is 2D, with each tile carrying an `elevation`. That one
extra number lets a flat rules grid render as a layered 2.5D scene, and encodes
the ledge rule: you may climb at most `stepHeight`, but you may drop further.

Field abilities reach their targets through a shape — the tile you face, a
radius, a line — and then apply their effect to whichever interactables in that
shape declared they listen for that ability.

**The inversion matters.** An interactable declares which abilities it responds
to; an ability does not know what it can affect. So adding a new liftable rock
is content, not code, and a new ability automatically works on everything that
already listens for it.

Effects cover lifting, pushing, toggling, raising and lowering terrain,
revealing, burning, freezing and growing. Each returns a `FieldChange` record
describing what changed, which the renderer replays as animation.

## Determinism

Every random decision flows through a seeded, serializable generator stored
inside the battle state. Two clients from the same state with the same commands
produce identical results. This makes replays cheap, netplay possible without
sending outcomes, and balance failures reproducible from a seed.
