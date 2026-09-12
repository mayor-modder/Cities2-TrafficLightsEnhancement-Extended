# Traffic Lights Enhancement guide

## What this mod does

TLE Extended lets you change how individual traffic lights behave in Cities: Skylines II. Open the tool, select a signalized intersection, choose the signal behavior you want, and save it.

You can use it to:

- Choose a built-in signal mode for an intersection.
- Add options such as turning on red or a dedicated pedestrian phase.
- Build your own custom phase cycle.
- Give trams or buses signal priority at selected intersections.

The mod supports both left-hand traffic and right-hand traffic. It is also intended to keep existing Traffic Lights Enhancement intersection settings compatible when you load a city with TLE Extended; see the README for current compatibility notes and limitations.

> [!TIP]
> If a mode or option is missing, the selected intersection probably cannot use it. Try a simpler road junction, or use custom phases if you want to build the signal behavior yourself.
> If the toolbar button, panel, or selected-intersection controls are missing during local testing, collect the evidence listed in the [mod loading and UI visibility troubleshooting guide](docs/mod-loading-ui-troubleshooting.md).

## How to use

1. Select the Traffic Lights Enhancement button from the top-left toolbar.

<img width="400" height="141" alt="Traffic Lights Enhancement toolbar button" src="docs/images/guide/tle-button.png" />

2. The Traffic Lights Enhancement panel opens. Click a signalized intersection once to select it.

<img width="400" height="295" alt="Traffic Lights Enhancement panel before selecting an intersection" src="docs/images/guide/tle-panel.png" />

3. Choose the signal mode and options you want. Turn on [transit signal priority](#transit-signal-priority) only at intersections where you want the lights to give approaching trams or buses an edge.

<img width="400" height="823" alt="Traffic Lights Enhancement selected-intersection options" src="docs/images/guide/tle-options.png" />

4. Click Save. The selected intersection should now use those settings.

## Choosing a signal mode

Signal modes decide which lanes get green lights together and how the signal moves through its cycle. A lane group is just a set of lanes that turns green at the same time.

| Mode | What it does |
| --- | --- |
| Vanilla | Uses the base-game style signal behavior. This is the safest starting point if you only want small option changes. |
| Split phasing | Gives one direction or lane group a green light at a time. This can make busy turning movements easier to control, but it may also make the full cycle longer. |
| Protected left turns / Protected right turns | Adds a separate protected turn before normal traffic flow. In right-hand traffic, the UI shows Protected left turns. In left-hand traffic, it shows Protected right turns. |
| Split phasing + protected left | A split-phasing variant that also gives protected left turns when the intersection layout supports it. |
| Custom phases | Lets you build the signal cycle yourself by deciding which lanes and crossings are green in each phase. This is powerful, but easier to misconfigure. |

Some modes only appear for simple road intersections. Protected turn modes need a normal four-way intersection where traffic can continue straight ahead from each side. Split-phasing modes are hidden on intersections with more than seven connected road or track segments, and at crossings where train tracks or standalone tram tracks cross the road.

## Extra options

These options appear when the selected mode and intersection layout support them.

| Option | What it does |
| --- | --- |
| Allow turning on red | Lets vehicles make the curbside turn on red: right turns in right-hand traffic, left turns in left-hand traffic. |
| Give way to oncoming vehicles | Vanilla mode only. Turning vehicles should yield to oncoming traffic, though aggressive drivers may still reduce how well this works at busy junctions. |
| Exclusive pedestrian phase | Adds a separate pedestrian phase where vehicle traffic stops while crossings are served. |
| Pedestrian phase duration | Changes how long the exclusive pedestrian phase lasts. Treat this as a multiplier: larger values mean a longer pedestrian green. Pedestrian lights do not automatically extend when more pedestrians are waiting. |

Allow turning on red and give way to oncoming vehicles control road-vehicle behaviour, so they are hidden at tram-only junctions that have no road vehicle lanes. The exclusive pedestrian phase and its duration still appear there, since pedestrians may cross the tram tracks.

> [!WARNING]
> Some pedestrian pathfinding problems appear to come from the game's node or pathfinding behavior. This mod can control signal phases, but it cannot fix every pedestrian routing issue at unusual junctions.

## Custom phases

Custom phases are for intersections where the built-in modes are not enough. A phase is one step in the traffic-light cycle. For each phase, you choose which lanes, tram tracks, bike lanes, and crosswalks are allowed to move.

Select custom phases, then open the custom phase editor. The left side lists phases in cycle order. Use the edit button to change a phase, drag phases to reorder them, add phases when you need another step, or delete phases you no longer need. The editor supports up to 16 phases. Use manual control to force one phase while testing; leave manual control before saving normal automatic behavior.

While editing a phase, floating lane controls appear over the selected intersection. Click a movement to change what that movement does in the active phase:

| Movement type | Click behavior |
| --- | --- |
| Car and bus-lane movements | Cycles between stop, go, and yield. Yield means the movement may proceed while still giving way to conflicts. |
| Tram or train track movements | Toggles between stop and go. |
| Bicycle lanes and pedestrian crossings | Toggles between stop and go. |

The link icon between two phases links the first phase to the one after it. Linked phases still keep their own lane permissions and timing values, but the selector treats the linked block as a closer sequence when demand points into it. Linked selection does not wrap from the last phase back to the first.

The signal delays section can delay an edge's green at the start of a phase or end that edge's green before the phase ends. Use this for staggered starts, clearance time, or keeping one approach from moving for the full phase. These delays are per edge and per phase; they do not change the phase order.

### Timing styles

Custom phases have two timing styles:

| Timing style | What it means |
| --- | --- |
| Dynamic | The signal follows the phase order, but reacts to measured traffic demand while deciding when to end the current phase and which phase to serve next. |
| Fixed timed | The signal follows the phase order and configured durations more directly. If smart phase selection is enabled, it can still choose a demanded phase at transition points instead of always taking the next phase. |

Minimum duration is the earliest point where a phase may end. Maximum duration is the latest point where it may keep running. The UI shows these values with an `s` suffix, but they are signal update ticks rather than exact real-world seconds, so treat them as relative timing values.

In dynamic mode, a phase with a minimum duration of `0` is skippable. Skippable phases run when they have detected demand; otherwise the signal looks ahead to the next eligible phase. If every phase is skippable and empty, the signal falls back to the next phase so the controller does not stall. A phase with a minimum duration above `0` is always eligible and will not be skipped just because it is empty.

### Dynamic controls

These controls appear in dynamic mode:

| Control | What it does |
| --- | --- |
| Phase change mode | Decides what condition can end the current phase between the minimum and maximum duration. Auto balances current flow against waiting demand. On flow drop, On wait increase, When empty, and When no demand use narrower conditions. |
| Wait sensitivity | Changes how strongly waiting traffic pushes the signal toward another phase. Higher values make waiting demand matter sooner. |
| Target duration | Scales the computed target duration for the current phase. Higher values make a busy phase harder to end early, but the maximum duration still caps it. |
| Interval exponent | Raises the priority of phases that have not run recently, helping keep active phases from being starved. |
| Vehicle weights | Changes how much cars, buses, rail vehicles, pedestrians, or bicycles count when dynamic demand is calculated. These weights do not add lane permissions; they only affect demand scoring for movements already served by a phase. |
| Smoothing factor | Blends new demand readings with previous readings. Lower values react faster; higher values change more gradually. |

### Timing templates and presets

Timing templates are starting points for phase settings. They apply the same timing values to every custom phase in the selected intersection. They do not inspect which phase serves cars, pedestrians, tracks, or crossings, and they do not change lane permissions.

| Template | What it changes |
| --- | --- |
| Default | Restores standard timing values. |
| Quick cycle | Uses shorter durations and more responsive demand. |
| Heavy traffic | Uses longer durations and steadier flow. |
| Pedestrian friendly | Uses shorter, balanced timing intended for pedestrian-heavy custom cycles. |
| Rail priority | Uses a longer maximum duration and a wait-based change mode suited to track-heavy cycles. |
| Night mode | Uses very short timings and a no-demand change mode for quiet intersections. |

User presets save and reapply timing settings. Like built-in templates, they change timing and demand controls, not the lane or crossing movements assigned to each phase.

### Demand, skipping, and edge cases

Custom phases are powerful because the mod will run exactly the movement masks you build. That also means it can run an unsafe or unhelpful cycle if a phase omits an important movement, serves conflicting movements, or leaves every pedestrian crossing stopped. Save after testing, then watch the intersection for at least a few cycles.

Generating custom phases is a starting point, not a guarantee of perfect timing. Recheck lane permissions after road edits, lane-direction changes, or track changes. The mod tries to keep saved masks aligned after a road edit, but unusual geometry can still need manual cleanup.

Custom phase pedestrian service is controlled by the phase masks. Turning on the exclusive pedestrian phase option does not replace checking which custom phases actually serve crosswalks.

Transit signal priority can work with custom phases by extending the current phase or choosing a transit-serving next phase when the selected intersection is not in a traffic group. It does not create new phases or rewrite your custom movement masks.

Traffic group followers show leader timing as read-only. While an intersection is in a traffic group, group coordination controls timing and local transit signal priority is suspended, including for the group leader.

## Transit signal priority

Transit signal priority, or TSP, lets a selected intersection favor approaching transit vehicles. It is configured separately for every intersection.

| Source option | What it does |
| --- | --- |
| Enable for trams | Allows approaching trams to request priority. A tram request can cut a conflicting phase short to serve the tram sooner. |
| Enable for buses | Allows approaching buses to request priority. A bus request waits for a normal phase change, unless the bus is on a dedicated bus lane. |

TSP is meant to reduce avoidable transit delay. It does not guarantee that every bus or tram gets an instant green light.

Trams take precedence over buses. Buses detected on dedicated bus lanes can cut a conflicting phase short, just like trams. Buses in mixed lanes instead wait for a normal phase change to hold or bring up their green, because stop relation and lane-change uncertainty make it harder to safely cut other phases short there.

A bus that makes no meaningful forward progress while its green is served stops
requesting priority after ten observation ticks. Waiting at a red light does not
count toward this limit. Priority becomes available again when the bus moves
forward, changes to a replacement lane, or leaves the observed approach. Other
eligible buses and trams can still request priority. This initial threshold needs
fresh gameplay validation; it is not a ten-second wall-clock timer.

TSP also respects pedestrian protection. If an exclusive pedestrian phase is active or due, the mod may delay or ignore a transit request so pedestrians are not starved.

## Traffic groups and TSP

Traffic groups coordinate multiple intersections, usually for green waves. TSP and traffic groups are intentionally treated as incompatible controls.

When an intersection is part of a traffic group, local TSP is paused for that intersection, including the group leader. This lets the group timing stay in charge. The intersection's saved TSP settings are kept, so they can work again if you remove the intersection from the group.

## Diagnostics

Most players can ignore diagnostics. They are mainly for testing, bug reports, checking which traffic-light options should be available at a selected junction, and figuring out why a bus or tram did or did not receive priority.

To use them, enable the TLE diagnostics option in the mod settings, then select an intersection. The selected-intersection panel can show live traffic-signal state, expected option availability, topology details, TSP state, and recent decisions. The same diagnostics feature writes JSONL trace lines with extra details for troubleshooting.

On Windows, the active trace file is written to:

```text
C:\Users\<your user name>\AppData\LocalLow\Colossal Order\Cities Skylines II\C2VM.TrafficLightsEnhancement.TspDiagnostics.jsonl
```

When the file reaches 5 MB, the mod rotates it in the same folder with a timestamped name such as `C2VM.TrafficLightsEnhancement.TspDiagnostics.20260527091530.jsonl`. It keeps the active file plus the newest three rotated files.

### Common diagnostics fields

| Field | What it means |
| --- | --- |
| Enabled | Whether TSP is enabled for the selected intersection. |
| Signal state | What the signal controller is currently doing, such as ongoing, ending, changing, beginning, extending, or extended. |
| Current group | The lane group currently receiving service. |
| Next group | The lane group the controller is preparing to serve next. `-` means no next group is selected yet. |
| Timer | The current signal timer value. |
| Signal groups | How many lane groups the selected intersection has. |
| Junction topology | Connected edge count and compact topology flags used when checking pattern and option availability. |
| Available patterns | Predefined signal patterns the selected junction can use. |
| Extra options | Whether the extra Options section and each option should be visible, checked, or hidden. |
| Tram control / Bus control | Whether the TSP source row is visible, editable, and checked for the selected junction. |
| Traffic group role | Whether the selected grouped junction is the leader or a follower. |
| Traffic group mode | Whether the selected group is using lockstep timing, green-wave timing, or is missing valid group data. |
| Group cycle length | The traffic group's current cycle length value. Treat it as an inherited timing value, not wall-clock seconds. |
| Member signal delay | The selected member's stored green-wave signal delay. |
| Member phase offset | The selected member's signed phase offset. |
| Member cycle timer | The selected member's current cycle-position timer. |
| Group phase mapping | The leader phase, selected junction phase, group signal state, and master timers used to explain grouped synchronization. |
| TSP suspended while grouped | Whether local TSP is paused because the selected junction belongs to a traffic group. |
| Request | The active TSP request. `Early` means a fresh approaching vehicle was detected. `Latched` means a recent request is being held briefly after the sample disappeared. `None` means there is no active request. |
| Source | The request source, usually `Track` for trams or `Bus` for bus TSP. |
| Target group | The lane group the transit vehicle wants served. |
| Strength | The request strength used when more than one TSP request competes. |
| Expiry | How long a latched request can remain active before clearing. |
| Extend current phase | Whether the request can hold the current green because it already serves the transit vehicle. |
| Decision | The final TSP decision, such as extending the current phase, selecting the target phase, or waiting. |
| Base group | The group the signal would have served before TSP was considered. |
| Selected group | The group selected after TSP was considered. |
| Decision target | The group requested by the TSP decision being considered. |
| Decision source | Whether the decision came from a tram/track request or a bus request. |
| Exclusive pedestrian phase | Whether exclusive pedestrian phasing is enabled when pedestrian context affects the TSP decision. |
| Active pedestrian protection | Whether an active pedestrian phase is being protected from preemption. |
| Pedestrian phase due | Whether a waiting pedestrian phase may delay TSP so pedestrians are not starved. |
| Recent TSP events | A short history of recent request and decision changes. The newest event appears first. |

### Bus diagnostics

| Field or decision | What it means |
| --- | --- |
| Bus probe | How the bus detector matched the sampled bus to the intersection. |
| Bus decision | The bus detector outcome. This explains whether a bus request was emitted or why it was suppressed. |
| Bus no-progress ticks | Accumulated observation ticks without meaningful forward lane progress while the bus's green is served. Waiting at red does not add ticks. |
| Bus target group | The lane group matched to the sampled bus. |
| Bus hits | Number of bus samples contributing to the selected match. |
| Bus priority mode | Whether the active bus request is using aggressive or soft priority. "Aggressive (bus lane)" means the bus is on a marked bus-only lane and a conflicting phase can be cut short. "Soft" means the bus is in a mixed lane and can only hold a matching green or select at normal transition points. |
| Bus lane type | Whether the sampled bus is in a mixed lane or bus-only lane. |
| Bus lane change | Whether the sampled bus appears to be changing lanes. |
| Bus speed | The sampled bus speed. |
| Request emitted | A bus sample was eligible and generated a TSP request. |
| No eligible bus sample | No current bus sample met the detector and eligibility rules. |
| Bus priority disabled | Bus TSP is disabled at this intersection. |
| Suppressed: boarding | The bus appears to be stopped and boarding passengers. |
| Suppressed: no progress | The bus has reached the serving-green no-progress limit. Its requests remain suppressed until progress, lane replacement, or absence resets its observation. |
| Suppressed: near-side stop | Reserved for stop-aware detection. If shown, the bus is expected to stop before the signal, so holding the light would not help. |
| Suppressed: stop relation unknown | The stop relationship could not be determined safely. |
| Suppressed: lane change ambiguous | The bus appears to be changing lanes, so the target group may be unreliable. |

Some diagnostics rows use internal lane IDs, owner IDs, sample counts, curve positions, or fallback counts. These are not gameplay controls. They exist so trace logs and the colored lane overlay can be compared when investigating a problem.

For broader reports where the mod is installed but the button, panel, or controls
are missing, use the [mod loading and UI visibility troubleshooting guide](docs/mod-loading-ui-troubleshooting.md)
to classify install/playset failures separately from UI initialization,
selected-intersection binding, and unsupported-junction visibility.
