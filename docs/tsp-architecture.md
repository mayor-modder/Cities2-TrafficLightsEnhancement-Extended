# Transit Signal Priority Architecture

This document maps the Transit Signal Priority (TSP) implementation for future maintainers and coding agents. TSP is split between a pure C# decision layer, Unity/ECS runtime integration, and UI diagnostics. The pure layer owns policy and testable decisions; the Unity layer turns game state into requests and applies those requests to traffic-light state machines.

## Quick Map

| Area | Main Files | Responsibility |
| --- | --- | --- |
| Pure logic | [`TrafficLightsEnhancement.Logic/Tsp`](../TrafficLightsEnhancement.Logic/Tsp) | Settings normalization, request DTOs, request selection, preemption policy, override policy, display formatting, and unit-tested helpers. |
| Saved ECS settings | [`TransitSignalPrioritySettings.cs`](../TrafficLightsEnhancement/Components/TransitSignalPrioritySettings.cs) | Per-intersection saved TSP configuration. Converts to pure logic settings with `ToLogicSettings()`. |
| Runtime ECS state | [`TransitSignalPriorityRequest.cs`](../TrafficLightsEnhancement/Components/TransitSignalPriorityRequest.cs) | Latched per-intersection request state used across simulation ticks. |
| Runtime diagnostics | [`TransitSignalPriorityRuntimeDebugInfo.cs`](../TrafficLightsEnhancement/Components/TransitSignalPriorityRuntimeDebugInfo.cs), [`TransitSignalPriorityDecisionTrace.cs`](../TrafficLightsEnhancement/Components/TransitSignalPriorityDecisionTrace.cs) | Transient UI-facing debug and final-decision trace data. |
| Tram indexing | [`TramApproachIndex.cs`](../TrafficLightsEnhancement/Systems/TrafficLightSystems/Simulation/TramApproachIndex.cs) | Builds a per-tick lookup from tram track lane entity to tram curve position. |
| Bus indexing | [`BusApproachIndex.cs`](../TrafficLightsEnhancement/Systems/TrafficLightSystems/Simulation/BusApproachIndex.cs) | Builds a per-tick lookup from bus/current or change-lane entity to approach sample data when diagnostics or bus priority need it. |
| Request production | [`TransitSignalPriorityRuntime.cs`](../TrafficLightsEnhancement/Systems/TrafficLightSystems/Simulation/TransitSignalPriorityRuntime.cs) | Reads ECS/game state, detects approaching trams and buses, builds or latches requests, and writes debug fields. |
| Normal signal application | [`PatchedTrafficLightSystem.cs`](../TrafficLightsEnhancement/Systems/TrafficLightSystems/Simulation/PatchedTrafficLightSystem.cs) | Resolves active TSP requests and applies them to normal traffic-light signal group selection. |
| Custom phase application | [`CustomStateMachine.cs`](../TrafficLightsEnhancement/Systems/TrafficLightSystems/Simulation/CustomStateMachine.cs) | Applies TSP hold/override policy to custom phase state machines. |
| UI and diagnostics | [`UISystem.UIBIndings.cs`](../TrafficLightsEnhancement/Systems/UI/UISystem.UIBIndings.cs), [`content.tsx`](../TrafficLightsEnhancement/UI/src/mods/components/main-panel/content.tsx) | Exposes selected-intersection TSP state, summary rows, recent events, and optional JSONL trace output. |

## Pure Logic Layer

The pure logic project lives in [`TrafficLightsEnhancement.Logic/Tsp`](../TrafficLightsEnhancement.Logic/Tsp). It targets `netstandard2.0` and has no Unity dependencies, so changes here should usually be covered by xUnit tests in [`TrafficLightsEnhancement.Tests/Tsp`](../TrafficLightsEnhancement.Tests/Tsp).

Key files:

- [`TspRequestInputs.cs`](../TrafficLightsEnhancement.Logic/Tsp/TspRequestInputs.cs) defines shared value types: `TspSource`, `PhaseScore`, `TspRequest`, and `TspDecision`.
- [`TransitSignalPrioritySettings.cs`](../TrafficLightsEnhancement.Logic/Tsp/TransitSignalPrioritySettings.cs) defines pure settings defaults and normalization. Current defaults keep the saved component disabled, allow track requests when tram priority is enabled, keep public-car/bus requests disabled until the separate bus control is enabled, use request horizon `10`, and max green extension `45`.
- [`TransitSignalPriorityRuntime.cs`](../TrafficLightsEnhancement.Logic/Tsp/TransitSignalPriorityRuntime.cs) converts normalized settings plus lane classification into a `TspRequest`. It can emit `TspSource.Track` or `TspSource.PublicCar` when the matching source flag is enabled.
- [`TspSourcePriority.cs`](../TrafficLightsEnhancement.Logic/Tsp/TspSourcePriority.cs) is the single source of truth for source ordering. Track requests outrank public-car/bus requests, equal-priority requests use strength as the tiebreaker, and a dedicated-lane bus request is preferred over a tied mixed-lane bus request so the `OnDedicatedLane` flag survives selection regardless of sublane scan order.
- [`EarlyApproachDetection.cs`](../TrafficLightsEnhancement.Logic/Tsp/EarlyApproachDetection.cs) contains pure helper policy for approach-lane resolution, tram-sample probing, early-vs-petitioner selection, and connected-edge fallback diagnostics.
- [`TspDecisionEngine.cs`](../TrafficLightsEnhancement.Logic/Tsp/TspDecisionEngine.cs) combines and scores requests. `CombineRequests()` selects the strongest eligible request using source priority first, then strength; `SelectNextPhase()` can extend a current phase that serves the request or bias selection toward a phase serving that source.
- [`TspOverrideEngine.cs`](../TrafficLightsEnhancement.Logic/Tsp/TspOverrideEngine.cs) applies a selected TSP request to a base phase or signal group choice. It owns `TspSelectionReason` and `TspOverrideSelection`.
- [`TspPreemptionPolicy.cs`](../TrafficLightsEnhancement.Logic/Tsp/TspPreemptionPolicy.cs) owns request latching, current-group hold, aggressive preemption, minimum-green override, and exclusive-pedestrian protection.
- [`TspStatusFormatter.cs`](../TrafficLightsEnhancement.Logic/Tsp/TspStatusFormatter.cs) converts request state into UI-facing status labels.

Important boundary conventions:

- Signal groups in game/ECS data are 1-based. Pure phase indexes are usually 0-based. `TspOverrideEngine.ApplySignalGroupOverride()` is the bridge between those conventions.
- `TspSource.PublicCar` and `m_AllowPublicCarRequests` power Transit Signal Priority for buses. Bus requests are off by default and have lower priority than tram requests. Buses detected on a marked (PublicOnly) bus lane carry `OnDedicatedLane = true` on both the `TspRequest`/`TspSignalRequest` value types and the transient runtime components `TransitSignalPriorityRequest` and `TransitSignalPriorityDecisionTrace`; these requests receive tram-style aggressive conflicting-group preemption (minimum green on a conflicting phase drops to 1 tick). Buses on mixed lanes carry `OnDedicatedLane = false` and keep the soft behavior (hold or select at normal transition points only).
- Request horizon value `120` is treated as a legacy default and normalized to `10`. Changing that behavior affects compatibility with previously saved TSP settings.

## Saved Settings And Runtime Components

TSP uses one saved component and several transient runtime components.

[`TrafficLightsEnhancement/Components/TransitSignalPrioritySettings.cs`](../TrafficLightsEnhancement/Components/TransitSignalPrioritySettings.cs) is the saved per-intersection component. It implements `ISerializable`, writes a versioned payload, normalizes loaded values, and converts to the pure logic settings type through `ToLogicSettings()`.

Saved fields:

- `m_Enabled`
- `m_AllowTrackRequests`
- `m_AllowPublicCarRequests`
- `m_RequestHorizonTicks`
- `m_MaxGreenExtensionTicks`

[`TransitSignalPriorityRequest.cs`](../TrafficLightsEnhancement/Components/TransitSignalPriorityRequest.cs) is runtime latch state. It stores the target signal group, source, strength, expiry timer, and whether current-phase extension is allowed. It is added, updated, or removed each simulation tick by `PatchedTrafficLightSystem.UpdateTrafficLightsJob`.

[`TransitSignalPriorityRuntimeDebugInfo.cs`](../TrafficLightsEnhancement/Components/TransitSignalPriorityRuntimeDebugInfo.cs) is transient diagnostics state. It records candidate type, probe results, selected lanes and lane owners, sibling samples, master-lane flags, tram approach index size, and connected-edge fallback details.

[`TransitSignalPriorityDecisionTrace.cs`](../TrafficLightsEnhancement/Components/TransitSignalPriorityDecisionTrace.cs) is transient final-decision trace state. It records the requested target group, base group, selected group, request source, and selection reason used by the UI diagnostics panel.

## End-To-End Runtime Flow

The normal runtime path starts in [`PatchedTrafficLightSystem.OnUpdate()`](../TrafficLightsEnhancement/Systems/TrafficLightSystems/Simulation/PatchedTrafficLightSystem.cs).

1. `OnUpdate()` checks whether any enabled, source-capable, runtime-eligible TSP setting exists through `TspPolicy.ShouldBuildApproachIndex(...)`, `TspPolicy.IsApproachIndexEligibleSetting(...)`, and the bus-specific eligibility helper.
2. If needed, [`TramApproachIndex.Build(...)`](../TrafficLightsEnhancement/Systems/TrafficLightSystems/Simulation/TramApproachIndex.cs) scans rail transit vehicles and builds a `NativeParallelHashMap<Entity, float>` from tram track lane entity to curve position. [`BusApproachIndex.Build(...)`](../TrafficLightsEnhancement/Systems/TrafficLightSystems/Simulation/BusApproachIndex.cs) scans bus road vehicles when diagnostics or bus priority require bus samples.
3. `UpdateTrafficLightsJob.Execute(...)` calls `TransitSignalPriorityRuntime.TryResolveActiveLocalRequest(...)` before selecting the next signal group.
4. The runtime reads and normalizes ECS settings, rejects unavailable or traffic-group member intersections, builds a fresh request if possible, or latches a still-valid existing request.
5. If a request is active, the job writes `TransitSignalPriorityRequest` and `TransitSignalPriorityRuntimeDebugInfo`. If no request is active, stale request/debug components are removed.
6. Normal or custom signal selection receives `hasTspRequest` plus the active `TransitSignalPriorityRequest`.

Diagnostic cost contract: diagnostics are allowed to reuse bus samples gathered
by the bus-priority runtime path, but diagnostics must not force that runtime
path when bus priority is off. With diagnostics on and bus priority off, the
panel may scan buses for display only; with diagnostics off, no panel-only bus
debug work should run.
7. If TSP changes or extends the selected group, `TransitSignalPriorityDecisionTrace` is written for diagnostics. If no TSP decision was made, stale decision traces are removed.

## Request Production

The integration runtime lives in [`TrafficLightsEnhancement/Systems/TrafficLightSystems/Simulation/TransitSignalPriorityRuntime.cs`](../TrafficLightsEnhancement/Systems/TrafficLightSystems/Simulation/TransitSignalPriorityRuntime.cs). Its main entry point is `TryResolveActiveLocalRequest(...)`.

Fresh request detection:

- `TryBuildFreshRequest(...)` scans junction sublanes with `LaneSignal`.
- For each signaled lane, it resolves the approach lane using `ExtraLaneSignal.m_SourceSubLane` when available.
- Early approach candidates are built by `TryBuildEarlyApproachRequestForTrackLane(...)`.
- Petitioner candidates are built by `TryBuildPetitionerRequestForLane(...)`.
- Bus approach candidates are built by `TryBuildBusApproachRequestForLane(...)` from indexed bus samples on the signaled lane, resolved approach lane, or connected approach lane.
- Source selection prefers tram/track requests over bus/public-car requests.

Bus no-progress suppression tracks each bus and its observed lane independently
of diagnostic visibility. Ten accumulated serving-green observation deltas
without meaningful forward lane progress suppress that bus's fresh requests and
prevent its previous request from continuing to hold priority. Time at red does
not accumulate. Forward progress, lane replacement, or absence resets the
observation; suppression does not exclude other eligible buses or trams.
The junction's `TransitSignalPriorityBusProgress` buffer persists independently
of the request component. Observed boarding or lane-changing buses retain their
blocked history even while ordinary request eligibility is false. Multiple
signaled lanes matching the same bus and approach are observed once per update;
every matched movement must have an unambiguous ongoing `Go` signal to count.
Red or yielding alternatives on a shared approach do not consume grace.
The initial threshold is `10`, matching the default request horizon as a
testable starting point, not a measured real-world timeout. This behavior still
needs fresh gameplay validation. Observation state is transient and is not saved.

Selected bus diagnostics expose `Suppressed: no progress` and accumulated ticks.
The JSONL `busApproach` object adds `noProgressTicks` and
`noProgressSuppressed`, including them in diagnostic change detection.

The tram approach index is intentionally narrow:

- It scans vehicles with `PublicTransport`, `TrainNavigation`, and `TrainCurrentLane`.
- It records front and rear track-lane samples for moving trams that are not boarding.
- It keeps the smallest curve position per lane, representing the earliest relevant tram sample on that lane.
- `Arriving` and `RequireStop` do not suppress indexing; stopped/boarding behavior is handled by movement and boarding checks.

The early approach detector checks several lane candidates:

- the signaled lane,
- the resolved approach lane,
- connected-edge fallback lanes,
- immediate upstream lanes,
- connected upstream lanes.

Current thresholds are implementation details in the runtime: approach-lane checks use `0.2f`, upstream checks use `0.9f`, and connected-edge fallback uses `0f`.

Request latching is pure policy:

- Fresh eligible track or bus requests receive the effective request horizon.
- A still-valid latched track request outranks a fresh bus request; a fresh track request can replace a latched bus request.
- Existing requests decrement while source, target, strength, and expiry remain valid.
- Expired or invalid requests are removed from ECS state.

Live playtesting has confirmed the soft bus-priority path on bus-only lanes,
mixed lanes, vanilla signals, split phasing, protected turns, tram corridors,
and exclusive pedestrian phases. Dedicated bus lanes tend to produce cleaner
matches; mixed-lane buses remain supported but rely more heavily on conservative
stop-relation and lane-change suppression.

## Applying Requests To Signals

Normal signal selection is handled in [`PatchedTrafficLightSystem.cs`](../TrafficLightsEnhancement/Systems/TrafficLightSystems/Simulation/PatchedTrafficLightSystem.cs).

- `UpdateTrafficLightState(...)` receives settings and active request state.
- `GetNextSignalGroup(...)` computes the base group through `GetNextSignalGroupWithoutTsp(...)`.
- `TspPreemptionPolicy.ShouldAggressivelyPreemptToConflictingGroup(...)` can shorten minimum green for a conflicting request. Aggressive preemption fires for `TspSource.Track` requests and for `TspSource.PublicCar` requests whose `OnDedicatedLane` flag is set; the underlying predicate is `IsAggressivePreemptionToDifferentGroup` (renamed from `IsTrackPreemptionToDifferentGroup`). Mixed-lane bus requests (`OnDedicatedLane = false`) are not eligible.
- `TspPreemptionPolicy.ShouldApplyTargetGroupSelection(...)` allows eligible tram or bus requests to select their target group at normal transition points when pedestrian protection does not block the change.
- `TspOverrideEngine.ApplySignalGroupOverride(...)` changes the selected group or reports current-group extension.
- `TryApplyTspCurrentGroupHold(...)` and `TspPreemptionPolicy.ShouldHoldCurrentGroup(...)` hold a compatible current group while the request remains valid and the max extension limit has not been reached.

Custom phase selection is handled in [`CustomStateMachine.cs`](../TrafficLightsEnhancement/Systems/TrafficLightSystems/Simulation/CustomStateMachine.cs).

- `UpdateTrafficLightState(...)` receives the same active request from the patched system.
- During an ongoing custom phase, `TransitSignalPriorityRuntime.ShouldHoldCurrentGroup(...)` can extend the current group.
- `GetNextSignalGroup(...)` computes the base fixed/dynamic custom phase.
- `ApplyTspOverride(...)` calls `TspOverrideEngine.ApplyRequestOverride(...)` to select a target custom group when appropriate.

Exclusive pedestrian protection is shared policy. `TspPreemptionPolicy.ShouldProtectActivePedestrianPhase(...)` returns true only when exclusive pedestrian mode is enabled, the current phase is ongoing, the current group is in range, and `CustomTrafficLights.m_PedestrianPhaseGroupMask` contains the current group. When protection is active, TSP can hold the current group if it already serves the request, but it should not preempt away from the active pedestrian group.

## UI And Selected-Intersection Diagnostics

The UI binding layer is [`TrafficLightsEnhancement/Systems/UI/UISystem.UIBIndings.cs`](../TrafficLightsEnhancement/Systems/UI/UISystem.UIBIndings.cs).

Selected panel flow:

1. `GetMainPanel()` reads the selected entity's `TransitSignalPrioritySettings`, defaulting to `CreateDefault()` if the component is absent.
2. It attaches `mainData.transitSignalPriority.tram` with visibility, enabled/editable state, and status label.
3. It attaches `mainData.transitSignalPriority.bus` with independent visibility, enabled/editable state, and status label.
4. It attaches optional diagnostics under `mainData.transitSignalPriority.diagnostics`.
5. TypeScript consumes the binding through `useMainPanel()` in [`UI/src/mods/components/main-panel/index.tsx`](../TrafficLightsEnhancement/UI/src/mods/components/main-panel/index.tsx).
6. [`content.tsx`](../TrafficLightsEnhancement/UI/src/mods/components/main-panel/content.tsx) renders the TSP source toggles, follower status, summary, recent events, and diagnostic rows.

The toggle path is also in `UISystem.UIBIndings.cs`:

- The UI calls `toggleTransitSignalPriorityForTrams(...)` and `toggleTransitSignalPriorityForBuses(...)`.
- The C# triggers `ToggleTransitSignalPriorityForTrams(...)` and `ToggleTransitSignalPriorityForBuses(...)` create or update `TransitSignalPrioritySettings` when enabled.
- Disabling the last enabled TSP source removes `TransitSignalPrioritySettings` from the selected entity and marks it updated.
- Intersections in TLE traffic groups cannot toggle local TSP. Runtime TSP is suspended for every group member, including the leader, so group coordination and green-wave timing remain authoritative. Saved TSP settings are preserved and can resume if the intersection leaves the group.

Diagnostics are off by default. The user-facing mod option is labeled as TLE diagnostics, while the backing setting remains `Settings.m_ShowTransitSignalPriorityDiagnostics` for compatibility. `GetMainPanel()` only builds diagnostics when that option is true. `UISystem.SimulationUpdate()` also only auto-refreshes the selected panel for diagnostics when the same option is enabled.

`GetTransitSignalPriorityDiagnostics(...)` reads:

- `TrafficLights` for current signal state,
- `TransitSignalPriorityRuntimeDebugInfo` for probe and request-candidate details,
- `TransitSignalPriorityDecisionTrace` for the final selected/base/requested groups and reason.
- selected signal configuration, traffic-group membership, and lane signal group masks for JSONL trace context.

It returns:

- `summary`: compact selected-intersection state,
- `events`: recent changed signatures for the selected entity,
- `rows`: detailed diagnostic key/value rows.

## JSONL Trace Output

When diagnostics are enabled and the selected panel asks for diagnostics, `UISystem.UIBIndings.cs` can write a trace file:

- file name: `C2VM.TrafficLightsEnhancement.TspDiagnostics.jsonl` (legacy name),
- location: `Application.persistentDataPath`,
- writer: `GetTspDiagnosticsEvents(...)` and related helpers,
- rotation threshold: 5 MB,
- rotated file retention: newest 3 rotated files,
- dedupe: `TspDiagnosticsHistory.LastSignature` suppresses repeated identical selected-entity summaries.
- event filter: trace writes follow the same meaningful-activity filter as the visible recent-event list, so non-TSP selected intersections do not log ordinary signal changes.

Trace events also include a `selectedJunction` object. It mirrors the selected-panel topology and expected UI state used for option-gating QA: connected edge summary, predefined pattern availability, extra option visibility and checked state, pedestrian duration visibility, and tram/bus TSP control editability.

The trace is a debugging aid, not gameplay state. It should remain safe to disable, delete, or rotate without affecting TSP behavior.

## Test Coverage

TSP pure logic is covered by xUnit tests in [`TrafficLightsEnhancement.Tests/Tsp`](../TrafficLightsEnhancement.Tests/Tsp):

- [`TspPolicyTests.cs`](../TrafficLightsEnhancement.Tests/Tsp/TspPolicyTests.cs) covers availability, grouped-intersection policy, default settings, persisted-value detection, normalization/clamping, approach-index policy, and pedestrian mask bounds.
- [`TspEarlyDetectionTests.cs`](../TrafficLightsEnhancement.Tests/Tsp/TspEarlyDetectionTests.cs) covers lane resolution, indexed probe precedence, movement suppression, early-over-petitioner selection, connected-edge fallback helpers, and upstream/path-node helpers.
- [`TspDecisionEngineTests.cs`](../TrafficLightsEnhancement.Tests/Tsp/TspDecisionEngineTests.cs) covers track and public-car requests, null/empty handling, extension rules, override behavior, latching/expiry, hold policy, aggressive preemption, and active pedestrian protection.
- [`TspStatusFormatterTests.cs`](../TrafficLightsEnhancement.Tests/Tsp/TspStatusFormatterTests.cs) covers status label formatting.

UI-facing behavior also has Node tests in [`TrafficLightsEnhancement/UI/tests/transit-signal-priority-panel.test.mjs`](../TrafficLightsEnhancement/UI/tests/transit-signal-priority-panel.test.mjs), including panel state, diagnostics gating, trace writing, toggling, cleanup, and serialization expectations.

## Caveats For Future Work

- Buses on marked (PublicOnly) bus lanes now receive tram-style aggressive conflicting-group preemption via the `OnDedicatedLane` flag. Mixed-lane buses keep the soft behavior (hold or select at normal transition points). Stop relation, lane changes, and queues remain conservative refinement areas for future work on both bus categories.
- Vehicle-phase fairness prevents repeated transit priority from starving the
  same normal signal group. When TSP skips the base group, that group is
  recorded as pending; when it comes due again, TSP defers once so the skipped
  group can run.
- Grouped intersections currently suspend all local runtime TSP. Group-wide TSP would need explicit leader/member semantics before implementation.
- Runtime diagnostics are transient ECS data, but UI code depends on their field meanings. Treat renames/removals as UI-impacting.
- Connected-edge fallback is topology-sensitive and diagnostics-heavy. If lane-resolution rules change, update both runtime diagnostics and this document.
- The selected-intersection JSONL trace is synchronous UI-triggered file I/O. Keep it opt-in unless it is redesigned.
