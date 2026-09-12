# TLE Extended Roadmap

TLE Extended is a compatible extended fork of Traffic Lights Enhancement for
Cities: Skylines II. The project starts from the rewritten TLE codebase plus
Transit Signal Priority, and grows from there with a focus on compatibility,
maintainability, diagnostics, and broader transit-priority features.

This roadmap is maintainer-facing. The repository is public, but these notes are
not release promises.

## Guiding Principles

- Preserve drop-in compatibility with existing TLE saves and configured
  intersections wherever possible.
- Prefer opt-in behavior for changes that alter existing junction behavior.
- Version, document, and test save-format changes before users accumulate
  incompatible data.
- Keep complex logic isolated and testable outside Unity/ECS when practical.
- Make diagnostics useful enough to explain behavior in real cities, but keep
  expensive diagnostics off by default.
- Treat uncertain work as research until the implementation path is understood.
- Track concrete work as GitHub issues so future agents can continue without
  relying on chat history.

## Current Foundation

These are no longer roadmap guesses. They are the foundation future work should
preserve and extend:

- Transit Signal Priority is implemented as an opt-in, per-junction feature
  with separate tram and bus source controls.
- TSP has pure policy tests, UI source tests, serialization coverage, and a
  custom state-machine regression harness for TSP-off behavior.
- Dynamic mode now documents and tests restored narrow linked-phase behavior,
  including how it interacts with TSP-selected phases.
- Bicycle phase weight is exposed in the custom phase vehicle-weight UI.
- The bus source has a separate off-by-default player control. Buses detected on
  marked (PublicOnly) bus lanes now receive the same aggressive minimum-green
  preemption as trams via an `OnDedicatedLane` flag on the request. Buses in
  mixed lanes remain soft: they may hold an already-serving green or select
  their group at normal transition points. Trams still outrank buses.
- Bus diagnostics can identify mixed and bus-only approaches, including current
  and change-lane samples, and show a "Bus priority mode" row ("Aggressive (bus
  lane)" or "Soft") for the active request. Earlier bus-priority behavior has
  gameplay evidence; the new no-progress suppression still needs a fresh
  gameplay pass. Stop relation, lane-change semantics, and queue heuristics
  remain refinement areas.
- Maintainer docs now cover TSP architecture, diagnostics, dynamic mode,
  save-format compatibility, localization workflow, and serialization/migration
  audit notes.
- The fork is named and documented as TLE Extended while retaining the inherited
  TLE compatibility posture.

## Near-Term Decision Queue

These are the next bounded choices to resolve before larger feature expansion:

- Keep collecting bus-priority examples from real saves, especially edge cases
  around mixed lanes, lane changes, queues, and stop behavior.
- Validate bus no-progress suppression against a blocked bus, a real red-light
  queue, resumed movement, and competing bus/tram requests. Refine stop-relation
  classification and lane-change request semantics separately.
- Extract custom phase selection into pure logic only when a behavior change or
  larger refactor needs it; the current extraction audit does not require an
  immediate rewrite.
- Remove or retire unused inherited localization paths only after supported game
  version checks confirm they are safe to remove.
- Keep the save-format contract and localization workflow current whenever
  those surfaces change.

## Bus Priority Path

Bus priority builds on the TSP architecture with an opt-in implementation that
now includes aggressive priority for buses on marked bus lanes:

- Pure bus-priority policy tests are in place.
- Bus approach diagnostics can identify mixed-lane and bus-only approaches
  behind the existing off-by-default diagnostics option.
- Pure stop-aware suppression rules are in place for boarding, near-side stops,
  far-side stops, unknown stop relation, and queued buses.
- A separate bus source control exists and is off by default.
- **Buses on marked (PublicOnly) bus lanes now use tram-style aggressive
  minimum-green preemption**, carried by an `OnDedicatedLane` flag on the
  request. A conflicting phase's minimum green drops to 1 tick to bring up the
  bus's group. Tram requests still outrank bus requests.
- **Buses in mixed lanes remain soft**: they may hold an already-serving green
  or select their group at normal transition points only.
- A per-bus no-progress guard suppresses requests after ten serving-green
  observation ticks without meaningful forward lane progress. Red-light waiting
  is uncounted; progress, lane replacement, or absence rearms the bus. Other
  buses and trams remain eligible. The state is transient and the initial
  threshold still needs gameplay validation.
- Remaining future work: stop-relation classification, lane-change semantics,
  and mixed-lane aggressiveness improvements.

## Longer-Term Direction

- Support broader transit priority policies across trams and buses.
- Improve pedestrian-phase and conflict-policy behavior while keeping outcomes
  predictable.
- Build better compatibility and migration tooling for users moving from TLE.
- Improve inherited TLE documentation for migrations, custom phases, traffic
  groups, and maintenance workflows.
- Prepare public release documentation only when the project is ready to be
  publicized.

## Current Non-Goals

- No Paradox Mods publication push yet.
- No release dates.
- No broad marketing page.
- No large unrelated cleanup without an issue and a focused commit.
- No aggressive bus preemption for mixed-lane buses until stop-relation
  classification and lane-change semantics are understood in real saves.
  (Aggressive preemption for buses on marked bus lanes is now implemented.)
