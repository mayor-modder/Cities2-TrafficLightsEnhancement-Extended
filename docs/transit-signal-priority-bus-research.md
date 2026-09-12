# Transit Signal Priority for buses Research

This document records research and follow-up notes for extending Tram Signal
Priority (TSP) toward transit signal priority for buses.

## Current State

Transit Signal Priority for buses now exists as a separate off-by-default
player control. Earlier soft-priority behavior has gameplay evidence; the new
no-progress suppression still needs a fresh gameplay pass.

- `TspSource.PublicCar` is the internal source used for bus priority.
- `m_AllowPublicCarRequests` exists in settings and serialization.
- Runtime normalization and UI toggling keep bus requests disabled unless the
  separate Transit Signal Priority for buses control is enabled.
- Pure decision tests cover bus request ordering and keep tram requests ahead of
  bus requests.
- Live playtesting has verified useful bus priority behavior on bus-only lanes,
  mixed lanes, vanilla signals, split phasing, protected turns, tram corridors,
  and exclusive pedestrian phases.

The tram path builds `TramApproachIndex` from rail public transport vehicles
using `PublicTransport`, `TrainNavigation`, and `TrainCurrentLane`. Fresh
request production scans signaled sublanes, resolves source lanes, and only
builds requests when the resolved approach lane is a tram track.

## Reusable Pieces

Bus priority can reuse much of the TSP pipeline:

- saved settings component shape
- latched request component
- request expiry policy
- selected-intersection diagnostics and trace structure
- signal group hold/override application
- exclusive pedestrian phase protection
- custom phase integration

The lane and signal group mapping also has useful inherited support. Bus-only
lanes are represented through `CarLaneFlags.PublicOnly`, and custom phase masks
track public-car lane groups separately from general car lanes.

## Runtime Detection

Bus detection uses a road-vehicle approach index, not just public-only lane
detection. A bus in a mixed car lane can request that lane's signal group,
while a bus-only lane can use the public-car lane mask where custom phases
split it.

Runtime detection and diagnostics use or may refine these ECS data sources:

- `PublicTransport`
- `PublicTransport.m_State` with `Boarding`, `Arriving`, and `RequireStop`
- `PassengerTransport`
- `CarCurrentLane`
- `CarNavigation`
- `CarNavigationLane`
- `Moving`
- `PrefabRef`
- `PublicTransportVehicleData.m_TransportType == Bus`
- `CurrentRoute`
- route stop entities with `BusStop` and `TransportStop`
- route/vehicle buffers such as `RouteWaypoint`, `RouteVehicle`,
  `RouteLane`, and `VehicleTiming`

`ExtraTypeHandle` exposes the road-vehicle state needed for bus detection and
diagnostics: `PassengerTransport`, `CarCurrentLane`,
`CarNavigation`, `CarNavigationLane`, `Moving`, and
`PublicTransportVehicleData`.

Pure policy is source-generalized for bus priority. Request construction,
request combination, phase scoring, latching, current-group hold, and overrides
account for bus requests. Buses on marked (PublicOnly) bus lanes now also use
tram-style aggressive minimum-green preemption via an `OnDedicatedLane` flag on
the request; mixed-lane buses remain soft (hold or select at normal transition
points only).

## Stop-Aware Suppression Policy

Bus priority should be stricter than tram priority around stops. A tram with
`Arriving` or `RequireStop` can still be worth detecting because tram stops are
often integrated with the track approach. A bus approaching a near-side stop may
board passengers before the signal, so requesting green before boarding would
hold cross traffic for no benefit.

Available ECS data from `Game.dll` reflection:

- `Game.Vehicles.PublicTransport` has `m_State`, `m_TargetRequest`,
  `m_DepartureFrame`, `m_PathElementTime`, `m_MaxBoardingDistance`, and
  `m_MinWaitingDistance`.
- `Game.Vehicles.PublicTransportFlags` includes `Boarding`, `Arriving`, and
  `RequireStop`.
- `Game.Prefabs.PublicTransportVehicleData.m_TransportType` identifies buses
  with `TransportType.Bus`.
- `Game.Vehicles.CarCurrentLane` exposes current lane, change lane, curve
  position, lane flags, lane position, distance, and change progress.
- `Game.Routes.TransportStop` carries stop flags/loading data, and bus stops can
  be identified by the marker component `Game.Routes.BusStop`.
- Route context is available through route components/buffers such as
  `CurrentRoute`, `RouteWaypoint`, `RouteVehicle`, `RouteLane`, and
  `VehicleTiming`.

Pure stop suppression is now captured by
`BusPrioritySuppressionPolicy.EvaluateStopSuppression(...)`:

Today the runtime always passes `BusStopRelation.Unknown`; near-side and
far-side stop classification is tracked in #35, and lane-change semantics are
tracked in #36. The known-stop cases below describe the intended policy once
that classifier exists, not behavior the current runtime can already observe.

- `Boarding` always suppresses bus priority.
- `Arriving` or `RequireStop` suppresses priority for a known near-side stop
  before the signal.
- `Arriving` or `RequireStop` does not suppress priority for a known far-side
  stop after the signal; helping the bus cross the junction can still be useful.
- `Arriving` with unknown stop relation suppresses conservatively until
  diagnostics can classify the stop.
- `RequireStop` alone with unknown stop relation does not suppress a bus on a
  dedicated bus-only approach or a moving bus in a mixed lane. Live diagnostics
  showed this flag on buses at a junction that is
  not near any stop, so treating it as a near-side-stop signal blocked the
  easiest useful bus-priority case.
- `RequireStop` alone with unknown stop relation still suppresses stopped
  mixed-lane samples until diagnostics can classify the stop.
- A queued bus with no stop flags is not stop-suppressed by this policy. Runtime
  detection may still require movement/position thresholds before creating a
  request, but queueing is not the same as boarding.

Runtime implementation should continue refining stop relation separately from
the no-progress guard:

- **Near-side stop:** suppress while `Arriving`, `RequireStop`, or `Boarding`.
- **Far-side stop:** allow approach priority unless the bus is actually
  `Boarding`.
- **Stopped behind queue:** do not suppress solely because the bus is stopped;
  use distance/curve thresholds and request expiry to decide whether it is close
  enough to benefit.
- **Unknown stop relation:** allow `RequireStop`-only buses on dedicated
  bus-only approaches or while moving; suppress `Arriving` and stopped
  mixed-lane `RequireStop` samples, then report the unknown relation in diagnostics.

### No-progress suppression

The no-progress guard addresses repeated priority requests from a bus that is
not benefiting from its serving green. It tracks forward lane progress per bus,
accumulating serving-green observation deltas only. After ten ticks without
meaningful forward progress, the bus cannot keep refreshing a request or keep
its existing request latched. Real queues waiting at red do not accumulate
no-progress ticks. Forward progress, lane replacement, or absence rearms the
bus; other eligible buses and trams remain eligible throughout.

The initial ten-tick threshold matches the default request horizon as a bounded,
testable starting point. It is not a ten-second timeout or a validated tuning
result. All observation and suppression state is transient. Fresh gameplay
evidence must cover a blocked bus while green is served, a red-light queue,
movement resuming, lane replacement, and another eligible bus or tram.

Use a copied save and record the installed assembly's informational commit plus
the game version. With diagnostics enabled on the test junction, retain the
`busApproach` trace alongside these observations:

| Case | Expected result |
| --- | --- |
| Same bus remains blocked through an unambiguous green | `noProgressTicks` reaches 10, `noProgressSuppressed` becomes true, and that bus stops extending or preempting. Ordinary phase timing still applies. |
| Bus waits at red or its shared approach also has a red/yielding movement | No no-progress time accumulates. |
| Bus briefly pauses, then advances | The counter resets after meaningful cumulative lane progress. |
| Suppressed bus remains stationary through red or a boarding/ambiguous-lane interval | Suppression persists while the same bus and lane remain observed. |
| Bus advances, changes lane, or leaves the observed approach | Its old observation no longer blocks a later eligible request. |
| Another eligible bus or tram approaches | The blocked bus does not exclude that request. |

## Diagnostics

When the off-by-default TLE diagnostics option is enabled, `BusApproachIndex`
scans public-transport road vehicles with
`PublicTransportVehicleData.m_TransportType == Bus` and records
current/change-lane samples. This scan is intentionally independent of tram TSP
approach-index eligibility, so a selected bus-only candidate intersection can
still produce bus diagnostics even when no tram priority request is possible.
The selected junction diagnostics can report:

- indexed bus lane count
- whether a hit came from the signaled lane, resolved approach lane, or
  connected approach fallback
- bus-only versus mixed lane structure via `CarLaneFlags.PublicOnly`
- lane-change progress, speed, public-transport state, and vehicle lane flags
- accumulated no-progress ticks and `Suppressed: no progress`; the JSONL
  `busApproach` object includes `noProgressTicks` and `noProgressSuppressed`
- **"Bus priority mode"**: "Aggressive (bus lane)" when the active request is on
  a marked (PublicOnly) bus lane; "Soft" when the bus is in a mixed lane

The Transit Signal Priority for buses implementation creates
`TransitSignalPriorityRequest` values when its separate player control is
enabled. Buses on marked (PublicOnly) bus lanes receive tram-style aggressive
minimum-green preemption: a conflicting phase's minimum green drops to 1 tick
to bring up the bus's group. This is carried by an `OnDedicatedLane` flag
derived each tick from `BusApproachSample.IsBusOnlyLane`. Buses in mixed lanes
remain soft: they may hold an already-serving green or select their target group
at normal transition points only. Trams still outrank buses.

The JSONL trace `decision` object now includes a boolean field `onDedicatedLane`
(true when the active request is a bus on a marked bus lane receiving aggressive
priority).

Playtesting showed a useful split between lane types. Dedicated bus lanes
usually produce cleaner matches and fewer suppression reasons, and now also
trigger the stronger priority mode. Mixed-lane buses are supported and useful,
but they are noisier: stop relation and lane-change uncertainty can suppress
requests when the runtime cannot safely prove the bus will benefit from
priority.

## Edge Cases

Near-side stops are the biggest policy risk. A bus approaching a stop before the
signal should not request or hold green too early if it is about to board
passengers. The tram index suppresses boarding samples but allows
`Arriving`/`RequireStop`; buses may need stricter stop-aware handling.

Mixed lanes require vehicle-level detection. A lane marked for regular cars can
still carry a bus, and bus priority should follow the actual bus lane/current
route, not only the lane type.

Lane changes matter. `CarCurrentLane` can include both current and change-lane
state, and choosing the wrong lane near an intersection could select the wrong
signal group.

Congestion also matters. A stopped bus far behind a queue may not deserve
priority until it is close enough, latched, or otherwise confirmed to benefit
from priority. Playtesting found intentionally pathological layouts where TSP
can keep serving a legal-looking transit phase while the physical road geometry
is gridlocked; that is a layout edge case, not a reason to make the soft MVP
more aggressive or more restrictive by default.

## Implementation Status

The bus priority implementation as shipped:

- Bus requests are behind an explicit, off-by-default setting (per intersection).
- Tram requests still outrank bus requests.
- Buses on marked (PublicOnly) bus lanes use tram-style aggressive minimum-green
  preemption (implemented via `OnDedicatedLane`).
- Buses in mixed lanes may hold an already-serving green or select the target
  group at normal transition points (soft behavior unchanged).
- No save-format change, no migration, no new UI toggle beyond the existing bus
  TSP toggle.

Remaining open work: fresh gameplay validation and tuning of no-progress
suppression, stop-relation refinement, and further mixed-lane aggressiveness
improvements once stop and lane-change classification matures.

## Naming Decision

Keep **Transit Signal Priority** as the player-facing feature name, with
separate source controls for trams and buses.

The code can keep internal `TransitSignalPriority*` names because the saved
component shape and pure policy layer are intended to support more than one
transit source over time. Separate controls make the behavior easier to
explain, keep existing tram settings stable, and let buses stay disabled by
default unless a user explicitly enables them at an intersection.

Localization impact: keep new base strings in `Locale.json` first. Do not
rewrite non-English locale files by hand for this rename/split; let the normal
translation workflow handle new strings after the English UI is stable.

## Staged Plan Status

1. Add pure policy tests for `PublicCar` eligibility and source ordering.
   (Done.)
2. Prototype bus approach diagnostics that report bus lane hits. (Done.)
3. Integrate soft bus fresh request production from car-lane bus samples. (Done
   for MVP.)
4. Add a separate bus settings/control surface while keeping the existing tram
   labels and saved settings stable. (Done.)
5. Playtest bus-only lanes, mixed lanes, tram corridors, exclusive pedestrian
   phases, and combined bus/tram priority. (Done for release readiness.)
6. Add aggressive preemption for buses on marked bus lanes via `OnDedicatedLane`.
   (Done.)
7. Add per-bus serving-green no-progress suppression. (Implemented; fresh
   gameplay validation pending.)
8. Refine stop classification, lane-change handling, queue heuristics, and
   grouped-intersection semantics as follow-up.

## Follow-Up Work

Suggested follow-up issues:

- Refine bus request production around stop relation, lane changes, and queue
  distance.
- Refine stop-aware bus suppression rules with real-save examples.
- Validate and tune the initial no-progress threshold with recorded serving-green
  observations and resumed movement; preserve red-light queue eligibility.
- Improve lane-change and queue heuristics with real-save examples.
- Design explicit group-wide TSP semantics before allowing TSP to run on
  traffic-group members.
