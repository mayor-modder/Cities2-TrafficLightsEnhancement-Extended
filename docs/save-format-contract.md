# Save Format Contract

This document is the maintainer-facing compatibility contract for TLE Extended
save data. It lists the current serialized surface, the payload versions that
must remain readable, and the runtime fields that are intentionally not saved.

TLE Extended is intended to load saves created with bruceyboy's Traffic Lights
Enhancement while this fork remains in drop-in compatibility mode. Saves written
by TLE Extended may include additive TSP data and should not be treated as
downgrade-safe for older TLE builds once users enable new Extended features.

## Versioning Rules

`TLEDataMigrationSystem` writes the global data version
`TLEDataVersion.Current`, currently `V5`. On load, that global version drives
the migration plan documented in
[`serialization-and-migration-audit.md`](serialization-and-migration-audit.md).

Component payload versions are not always the same as the global migration
version:

- `CustomTrafficLights` writes the current global version, currently `V5`.
- Several inherited components write `V1` or `V2` payloads.
- `TransitSignalPrioritySettings` writes its own integer payload version, `2`.
- `TrafficGroupName` has no explicit payload version.
- `CustomLaneDirection` in the shared lane-system library uses a sentinel plus
  schema version instead of `TLEDataVersion`.

Future save changes should document whether a version bump is a global
migration step, a component payload change, or both. Readers currently do not
have generic skip logic for unknown future payload fields, so forward
compatibility must be designed explicitly.

## Saved Payloads

| Type | Kind | Current payload | Serialized fields |
| --- | --- | --- | --- |
| `TLEDataMigrationSystem` | global system | `TLEDataVersion.Current` (`V5`) | Global loaded data version only. |
| `CustomTrafficLights` | component on junction node | `V5` | `m_Pattern`, `m_PedestrianPhaseDurationMultiplier`, `m_PedestrianPhaseGroupMask`, `m_Timer`, `m_ManualSignalGroup`, `m_Mode`, `m_Options`. |
| `CustomPhaseData` | dynamic buffer on junction node | `V2` | V1 timing, demand, priority, options, duration, and multiplier fields; V2 bicycle occupancy, phase-change metric, wait/flow balance, open/close delays, vehicle weights, smoothing, next-step reference, and current flow/wait. |
| `EdgeGroupMask` | dynamic buffer on junction node | `V2` | Edge, position, options, car/public-car/track turn masks, stop-line and non-stop pedestrian masks, unified pedestrian mask, bicycle mask, open/close delay. |
| `SubLaneGroupMask` | dynamic buffer on junction node | `V1` | Sublane, position, options, car turn mask, track turn mask, pedestrian signal mask. |
| `GroupMask.Signal` | nested serializable value | `V2` | Go group mask, yield group mask, open delay, close delay. |
| `GroupMask.Turn` | nested serializable value | `V1` | Left, straight, right, and U-turn `GroupMask.Signal` values. |
| `SignalDelayData` | dynamic buffer on junction node | `V1` | Edge, open delay, close delay, enabled flag. |
| `ExtraLaneSignal` | component on sublane | `V1` | Yield group mask, ignore-priority group mask, source sublane. |
| `LaneFlowHistory` | component on sublane | `V1` | Duration vector, distance vector, frame. |
| `TrafficGroup` | component on group entity | `V1` | Coordinated flag, green-wave enabled flag, green-wave speed, green-wave offset, max coordination distance, creation time, cycle length. |
| `TrafficGroupMember` | component on junction node | `V2` | Group entity, leader entity, group index, distance to group center, distance to leader, phase offset, signal delay, member cycle timer, leader flag. |
| `TrafficGroupName` | component on group entity | unversioned | One string, packed internally into eight `ulong` fields. |
| `TransitSignalPrioritySettings` | component on junction node | `2` | Enabled flag, allow-track flag, allow-public-car flag, request horizon ticks, max green extension ticks. |
| `CustomLaneDirection` | dynamic buffer from `CommonLibraries/LaneSystem` | schema `3` | Sentinel `float.MaxValue`, schema version, position, tangent, group index, lane index, four restriction booleans, owner entity. |

`CustomPhaseData.Options.PrioritiseBicycle` is part of the V2 serialized surface
and has round-trip coverage, but no current runtime or UI consumer has been
confirmed. Treat it as reserved compatibility state unless a future change
explicitly defines bicycle priority semantics and migration behavior.

`CustomPhaseData` and `EdgeGroupMask.m_Bicycle` also persist bicycle delay
fields. Current custom-phase runtime uses edge-wide open/close delay for group
timing; movement-specific bicycle delay application has not been confirmed.

## Legacy Reads

`CustomTrafficLights` supports several inherited payload layouts:

- `V1` and earlier consumed fifteen legacy pattern entries and now reset the
  pattern to `Vanilla`.
- `V2` reads only the pattern.
- `V3` adds pedestrian phase duration and pedestrian phase group mask.
- `V4` adds timer and manual signal group.
- `V5` adds mode and options.

`CustomPhaseData` reads `V1` payloads and initializes all `V2` additions to the
same defaults used by a newly-created phase.

`EdgeGroupMask` reads `V1` payloads by deriving the unified pedestrian mask from
the stop-line and non-stop pedestrian masks. Bicycle and open/close delay fields
default to zero.

`GroupMask.Signal` reads `V1` payloads by defaulting open/close delays to zero.

`TrafficGroupMember` reads `V1` payloads by defaulting
`m_MemberCycleTimer` to `0`. `m_PhaseOffset` is a signed zero-based phase
offset; runtime consumers must wrap it before converting it to a one-based
`TrafficLights` signal group.

`TrafficGroup` currently writes `V1`, but reads and discards one extra bool for
payloads marked `V2` or newer. Older TSP builds serialized group propagation in
that slot. TLE Extended intentionally keeps propagation inactive, so the field is
discarded instead of reintroduced.

`TransitSignalPrioritySettings` supports TSP payload `1` and `2`. Payload `1`
contains an extra bool between `m_AllowPublicCarRequests` and
`m_RequestHorizonTicks`; the current reader consumes and discards it. Versions
below `1` remain at default settings.

`CustomLaneDirection` supports:

- schema `1`: raw position followed by four restriction booleans,
- schema `2`: sentinel, schema, position, tangent, group index, lane index, and
  restrictions, with owner defaulted to `Entity.Null`,
- schema `3`: schema `2` plus owner.

## Normalization And Validation

TSP settings normalize after load:

- request horizon `0` and the legacy default `120` become `10`,
- request horizon values above `120` clamp to `120`,
- max green extension `0` becomes `45`,
- max green extension values above `600` clamp to `600`.

Loaded TSP settings are not forced back to tram-only defaults. The serialized
track/public-car flags are part of the payload contract. Public-car requests
back the player-facing soft bus-priority feature.

The migration/validation pass also repairs inherited data where possible:

- invalid signal-delay edges are removed and delay values clamp to `0..300`,
- invalid `TrafficGroupMember` references or out-of-range values are removed or
  reset. Signed phase offsets in `-300..300` are valid,
- invalid `TrafficGroup` timing/distances reset to defaults,
- invalid `CustomTrafficLights` enum values reset and affected intersections
  are recorded for UI notice,
- orphaned custom phase/mask buffers can recreate a default
  `CustomTrafficLights` component so the user can inspect and repair them.

## Runtime-Only Fields

The following fields or components are intentionally transient and should not be
treated as save data:

- `TrafficGroup.m_LastSyncTime`, `m_CycleTimer`, and all `m_Master*` clock
  fields. They are recomputed by group synchronization at runtime.
- `TransitSignalPriorityRequest`, the latched current request state. Includes
  `m_OnDedicatedLane`, which records whether the active bus request was detected
  on a marked (PublicOnly) bus lane. This field is derived fresh each tick from
  `CarLaneFlags.PublicOnly` and is never written to the save payload.
- `TransitSignalPriorityRuntimeDebugInfo`, selected-intersection probe and
  candidate diagnostics.
- `TransitSignalPriorityBusProgress` is a transient junction buffer, not a
  serialized component. Bus identities, lane identities, curve anchors, and
  suppression state are rebuilt at runtime. The bus identity fields on
  `TransitSignalPriorityRequest` are transient as well.
  `TransitSignalPriorityBusApproachDebugInfo.m_BusNoProgressTicks` and
  `m_BusNoProgressSuppressed` expose that transient state for diagnostics only.
  No-progress suppression adds no saved fields, payload version change, or
  migration; saved TSP settings remain unchanged.
- `TransitSignalPriorityDecisionTrace`, final TSP decision diagnostics. Includes
  `m_OnDedicatedLane` for the "Bus priority mode" diagnostic row; also transient.
- The aggressive-bus-lane feature (dedicated-lane buses receiving tram-style
  1-tick minimum green on conflicting phases) is entirely runtime-derived. It
  introduces no new saved fields, no payload version bump, and requires no
  migration. `TransitSignalPrioritySettings` payload version `2` is unchanged.
- `TrafficGroupTspDebugState`, group-level TSP diagnostics.
- JSONL TSP diagnostics files under `Application.persistentDataPath`; these are
  opt-in debugging artifacts, not save data.

`LaneFlowHistory` and several `CustomPhaseData` flow/wait fields are saved by
the current code even though they are measurement-like. Treat them as part of the
current payload until a migration explicitly removes or recomputes them.

## Compatibility Expectations

TLE Extended should preserve these assumptions while in drop-in mode:

- Existing saves from supported TLE versions load without user action.
- Existing TLE intersections without `TransitSignalPrioritySettings` behave as
  non-TSP intersections.
- TSP is additive per junction; disabling both source controls removes the
  settings component and leaves inherited TLE data intact.
- Grouped intersections do not run local TSP, including the group leader. Any
  future group-wide TSP behavior needs a new explicit design before save fields
  are added.
- Public-car/bus priority fields are active for the soft bus-priority feature.
  Changing their serialized meaning may need a TSP payload bump and migration
  notes.
- Downgrading an Extended save to upstream TLE is not guaranteed after Extended
  has written components unknown to the target upstream build.

Before changing any serialized field order, type, version, or normalization
rule, add or update serializer fixtures in
`TrafficLightsEnhancement.Serialization.Tests` and update this contract.

## Save/Load Compatibility Checklist

Use this checklist when changing serializers, migration jobs, save-facing UI
bindings, traffic-group persistence, or TSP settings. Keep each row tied to
automation or a named follow-up so compatibility remains an auditable claim.

| Case | Current automated evidence | Status | Next action |
| --- | --- | --- | --- |
| Existing TLE intersections | `CustomTrafficLights` current round trip plus `V1`, `V2`, and `V4` legacy payload fixtures in `InheritedComponentSerializationTests`; migration sequencing for loaded versions `0..6` in `TleMigrationPlanTests`. | Partially automated | Add game-runtime load fixtures with representative upstream TLE saves before public release. |
| TLE Extended-only data | `TransitSignalPrioritySettings` current round trip, TSP payload `1` fixture, pre-`V1` payload defaulting, and payload `2` normalization fixtures in `InheritedComponentSerializationTests`; TSP source/persistence policy coverage in `TspPolicyTests`. | Automated at serializer and policy level | Add a game-runtime save/load fixture proving Extended-only components survive a real save cycle. |
| Traffic groups | `TrafficGroup` and `TrafficGroupMember` current round trips; `TrafficGroup` payload `V2` legacy-discard fixture; `TrafficGroupMember` payload `V1` defaulting fixture; signed phase-offset source guard in `TrafficGroupSystemSourceTests`; timing-only, topology-local missing-phase initialization guards in `TrafficGroupCustomPhaseInitializationSourceTests`. | Partially automated | Add ECS/world-level migration tests for invalid references, empty-group cleanup, and missing follower phases when a lightweight harness exists. |
| Custom phases | `CustomPhaseData`, `EdgeGroupMask`, and `SubLaneGroupMask` current round trips; `CustomPhaseData` payload `V1`, `EdgeGroupMask` payload `V1`, and `GroupMask.Signal` payload `V1` fixtures; no-TSP custom-state-machine regression coverage in `CustomStateMachineNoTspRegressionTests`. | Automated at serializer and selected runtime-policy level | Add game-runtime fixture coverage for a saved custom-phase intersection with masks and timing settings. |
| TSP settings | Serializer fixtures listed above; default, grouped-intersection, request-horizon, max-extension, public-car/bus, and approach-index policy coverage in `TspPolicyTests`; runtime-only TSP request/debug fields are excluded from the serialized payload list above. | Automated at serializer and policy level | Keep future source-flag or timing changes paired with payload-version notes and focused serializer fixtures. |
| Downgrade and mod-removal limits | README and this contract warn that Extended saves are not downgrade-safe after Extended writes unknown components, and mod removal can only usually revert lights after road updates. | Documented limitation | Do not advertise downgrade or removal safety without a real downgrade/removal smoke-test playbook. |
| Migration and normalization behavior | `TleMigrationPlanTests` covers global migration step selection; serializer fixtures cover legacy field defaults and TSP normalization; source guards cover selected validation invariants. | Partially automated | Add ECS/world-level tests for validation jobs that repair or remove invalid loaded data. |

### Follow-Up Issue Drafts

Use these draft bodies if the missing coverage needs to be tracked publicly.
When opening them, apply the repository's required agent-work, priority, area,
and project metadata.

#### Add game-runtime save/load fixtures for TLE compatibility

*Written by Codex.*

Add a small game-runtime or harness-backed save/load fixture set that proves the
compatibility cases currently covered only by serializer-level tests:

- an upstream TLE save with predefined signal options,
- an upstream TLE save with custom phases and group masks,
- a TLE Extended save with `TransitSignalPrioritySettings`,
- a traffic-group save with leader/member metadata.

The test should load the fixture, assert the expected ECS components and values,
save again, reload, and assert the same values. Keep fixture documentation in
`docs/save-format-contract.md`.

#### Add downgrade and mod-removal compatibility smoke tests

*Written by Codex.*

Create a manual or automated smoke-test playbook for the downgrade/removal
limits documented in `README.md` and `docs/save-format-contract.md`.

Cover:

- loading a TLE Extended save in the intended current build,
- attempting to load or inspect the same save with an older upstream TLE build,
- removing the mod and triggering a road update at previously configured
  intersections,
- recording exactly which data is preserved, reset, ignored, or unsafe.

Do not upgrade the README claim beyond the observed results.

#### Add ECS validation coverage for loaded-data repair jobs

*Written by Codex.*

Add focused ECS or harness tests for the validation behavior in
`TLEDataMigrationSystem` and `TLEDataMigrationJobs`:

- invalid `SignalDelayData` edges are removed and delays clamp to `0..300`,
- invalid `TrafficGroupMember` references are removed or reset,
- invalid `TrafficGroup` timing values reset to defaults,
- invalid `CustomTrafficLights` values reset and record affected intersections,
- orphaned custom phase or mask buffers recreate default `CustomTrafficLights`
  so the user can inspect and repair them.

These tests should complement the existing serializer fixtures and migration
plan tests rather than replacing them.
