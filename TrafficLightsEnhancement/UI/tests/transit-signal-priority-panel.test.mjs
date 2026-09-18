import assert from "node:assert/strict";
import { readdir, readFile } from "node:fs/promises";
import test from "node:test";

const source = (path) => readFile(new URL(`../${path}`, import.meta.url), "utf8");
const repoSource = (path) => readFile(new URL(`../../${path}`, import.meta.url), "utf8");
const literalTranslatePattern = /(?<!["'`])\btranslate\(\s*(["'`])([^"'`$]+?)\1/g;
const localizationNamespace = "C2VM.TrafficLightsEnhancement";

function getLiteralLocalizationKeys(text) {
  literalTranslatePattern.lastIndex = 0;
  return [...text.matchAll(literalTranslatePattern)]
    .map(match => match[2])
    .filter(key => key.includes(localizationNamespace));
}

async function getUiSourceFiles(relativeDir = "src") {
  const dir = new URL(`../${relativeDir}/`, import.meta.url);
  const entries = await readdir(dir, { withFileTypes: true });
  const files = [];

  for (const entry of entries) {
    const relativePath = `${relativeDir}/${entry.name}`;

    if (entry.isDirectory()) {
      files.push(...await getUiSourceFiles(relativePath));
    } else if (/\.(ts|tsx)$/.test(entry.name)) {
      files.push(relativePath);
    }
  }

  return files;
}

test("main panel data exposes transit signal priority tram source state", async () => {
  const general = await source("src/mods/general.ts");

  assert.match(general, /transitSignalPriority\?\s*:\s*\{/);
  assert.match(general, /tram:\s*\{/);
  assert.match(general, /isVisible:\s*boolean/);
  assert.match(general, /isEnabled:\s*boolean/);
  assert.match(general, /isEditable:\s*boolean/);
  assert.match(general, /statusLabel\?:\s*string/);
  assert.match(general, /diagnostics\?:\s*\{/);
  assert.match(general, /summary\?:\s*\{\s*label:\s*string,\s*value:\s*string\s*\}/);
  assert.match(general, /events\?:\s*Array<\{\s*sequence:\s*number,\s*title:\s*string,\s*detail:\s*string\s*\}>/);
  assert.match(general, /rows:\s*Array<\{\s*label:\s*string,\s*value:\s*string,\s*valueLabel\?:\s*string\s*\}>/);
});

test("main panel data exposes transit signal priority bus source state", async () => {
  const general = await source("src/mods/general.ts");

  assert.match(general, /transitSignalPriority\?\s*:\s*\{/);
  assert.match(general, /bus:\s*\{/);
  assert.match(general, /isVisible:\s*boolean/);
  assert.match(general, /isEnabled:\s*boolean/);
  assert.match(general, /isEditable:\s*boolean/);
  assert.match(general, /statusLabel\?:\s*string/);
});

test("bindings exposes the transit signal priority tram toggle trigger", async () => {
  const bindings = await source("src/bindings.ts");

  assert.match(bindings, /toggleTransitSignalPriorityForTrams\s*=\s*triggers\.create<\[boolean\]>\("ToggleTransitSignalPriorityForTrams"\)/);
});

test("bindings exposes the transit signal priority bus toggle trigger", async () => {
  const bindings = await source("src/bindings.ts");
  const uiBindings = await repoSource("Systems/UI/UISystem.UIBIndings.cs");

  assert.match(bindings, /toggleTransitSignalPriorityForBuses\s*=\s*triggers\.create<\[boolean\]>\("ToggleTransitSignalPriorityForBuses"\)/);
  assert.match(uiBindings, /CreateTrigger<bool>\("ToggleTransitSignalPriorityForBuses",\s*ToggleTransitSignalPriorityForBuses\)/);
});

test("migration issue UI derives boolean state from affected entities", async () => {
  const bindings = await source("src/bindings.ts");
  const uiBindings = await repoSource("Systems/UI/UISystem.UIBIndings.cs");
  const content = await source("src/mods/components/main-panel/content.tsx");

  assert.match(bindings, /affectedEntities\s*=\s*new OneWayBinding<any\[\]>\("GetAffectedEntities",\s*\[\]\)/);
  assert.match(content, /const hasMigrationIssues\s*=\s*migrationEntities\s*&&\s*migrationEntities\.length\s*>\s*0/);
  assert.doesNotMatch(bindings, /hasMigrationIssues\s*=\s*new OneWayBinding/);
  assert.doesNotMatch(uiBindings, /HasMigrationIssues/);
  assert.doesNotMatch(uiBindings, /HasLoadingErrors/);
});

test("main panel renders tram and bus controls together before the diagnostics pane", async () => {
  const content = await source("src/mods/components/main-panel/content.tsx");
  const controlsStart = content.indexOf('title="TransitSignalPriority"');
  const controlsEnd = content.indexOf("{transitSignalPriorityDiagnostics && (", controlsStart);
  const controlsSource = content.slice(controlsStart, controlsEnd);
  const diagnosticsSource = content.slice(controlsEnd);

  assert.notEqual(controlsStart, -1);
  assert.notEqual(controlsEnd, -1);
  assert.match(controlsSource, /TransitSignalPriority/);
  assert.match(controlsSource, /EnableTransitPriorityForTrams/);
  assert.match(controlsSource, /EnableTransitPriorityForBuses/);
  assert.match(controlsSource, /toggleTransitSignalPriorityForBuses/);
  assert.doesNotMatch(controlsSource, /TransitSignalPriorityDiagnostics/);
  assert.match(diagnosticsSource, /TransitSignalPriorityDiagnostics/);
  assert.doesNotMatch(controlsSource, /title="TransitPriorityForBuses"/);
  assert.doesNotMatch(controlsSource, /source/i);
  assert.doesNotMatch(controlsSource, /public[-\s]?car|publicCar/i);
});

test("grouped intersections replace transit priority toggles with one disabled notice", async () => {
  const content = await source("src/mods/components/main-panel/content.tsx");
  const locale = JSON.parse(await repoSource("Locale.json"));

  assert.match(content, /const\s+showTransitSignalPriority\s*=\s*!mainData\.isGroupMember\s*&&/);
  assert.match(content, /const\s+showGroupedTransitSignalPriorityNotice\s*=\s*mainData\.isGroupMember\s*&&/);
  assert.match(content, /showGroupedTransitSignalPriorityNotice\s*&&[\s\S]*message="BusTransitPriorityGroupedUnavailable"/);
  assert.equal(
    locale["UI.LABEL[C2VM.TrafficLightsEnhancement.BusTransitPriorityGroupedUnavailable]"],
    "Transit signal priority is disabled while this intersection is in a traffic group."
  );
});

test("bus source row is visible independently from tram source row", async () => {
  const content = await source("src/mods/components/main-panel/content.tsx");
  const tramVisible = "mainData.transitSignalPriority?.tram.isVisible";
  const busVisible = "mainData.transitSignalPriority?.bus.isVisible";
  const tramVisibleIndex = content.indexOf(tramVisible);
  const busVisibleIndex = content.indexOf(busVisible);

  assert.notEqual(tramVisibleIndex, -1);
  assert.notEqual(busVisibleIndex, -1);
  assert.ok(busVisibleIndex > tramVisibleIndex);

  const betweenVisibilityChecks = content.slice(tramVisibleIndex, busVisibleIndex);
  const nestedFragments = betweenVisibilityChecks.split("<>").length - 1
    - (betweenVisibilityChecks.split("</>").length - 1);
  assert.equal(nestedFragments, 0);
});

test("backend exposes separate tram and bus transit priority controls", async () => {
  const uiBindings = await repoSource("Systems/UI/UISystem.UIBIndings.cs");
  const toggleStart = uiBindings.indexOf("private void ToggleTransitSignalPrioritySource");
  const toggleEnd = uiBindings.indexOf("protected void SetCustomPhase", toggleStart);
  const toggleSource = uiBindings.slice(toggleStart, toggleEnd);

  assert.match(uiBindings, /transitSignalPriority\s*=\s*new/);
  assert.match(uiBindings, /tram\s*=\s*new/);
  assert.match(uiBindings, /bus\s*=\s*new/);
  assert.match(uiBindings, /protected void ToggleTransitSignalPriorityForTrams\(bool enabled\)/);
  assert.match(uiBindings, /protected void ToggleTransitSignalPriorityForBuses\(bool enabled\)/);
  assert.match(uiBindings, /tramStatusLabel\s*=\s*isTrafficGroupMember\s*\?\s*"TramTransitPriorityGroupedUnavailable"/);
  assert.match(uiBindings, /busStatusLabel\s*=\s*isTrafficGroupMember\s*\?\s*"BusTransitPriorityGroupedUnavailable"/);
  assert.match(uiBindings, /settings\.m_AllowTrackRequests\s*=\s*enabled/);
  assert.match(uiBindings, /settings\.m_AllowPublicCarRequests\s*=\s*enabled/);
  assert.match(uiBindings, /settings\.m_Enabled\s*=\s*settings\.m_AllowTrackRequests\s*\|\|\s*settings\.m_AllowPublicCarRequests/);
  assert.match(toggleSource, /hasExistingTransitSignalPrioritySettings/);
  assert.match(toggleSource, /settings\.m_AllowTrackRequests\s*=\s*false/);
  assert.match(toggleSource, /settings\.m_AllowPublicCarRequests\s*=\s*false/);
});

test("transit signal priority has concise English base labels", async () => {
  const locale = JSON.parse(await repoSource("Locale.json"));

  assert.equal(locale["UI.LABEL[C2VM.TrafficLightsEnhancement.TransitSignalPriority]"], "Transit signal priority");
  assert.equal(locale["UI.LABEL[C2VM.TrafficLightsEnhancement.EnableTransitPriorityForTrams]"], "Enable for trams");
  assert.equal(locale["UI.LABEL[C2VM.TrafficLightsEnhancement.EnableTransitPriorityForBuses]"], "Enable for buses");
  assert.equal(locale["UI.LABEL[C2VM.TrafficLightsEnhancement.TransitSignalPriorityDiagnostics]"], "Diagnostics");
  assert.equal(locale["UI.LABEL[C2VM.TrafficLightsEnhancement.BusTransitPriorityGroupedUnavailable]"], "Transit signal priority is disabled while this intersection is in a traffic group.");
});

test("selected-junction diagnostics are gated by a general TLE mod option", async () => {
  const settings = await repoSource("Settings.cs");
  const uiBindings = await repoSource("Systems/UI/UISystem.UIBIndings.cs");
  const uiSystem = await repoSource("Systems/UI/UISystem.cs");
  const locale = await repoSource("Locale.json");
  const content = await source("src/mods/components/main-panel/content.tsx");

  assert.match(settings, /public\s+bool\s+m_ShowTransitSignalPriorityDiagnostics\s*\{\s*get;\s*set;\s*\}/);
  assert.match(settings, /m_ShowTransitSignalPriorityDiagnostics\s*=\s*false/);
  assert.match(uiBindings, /bool\s+showTransitSignalPriorityDiagnostics\s*=\s*Mod\.m_Setting\s*!=\s*null\s*&&\s*Mod\.m_Setting\.m_ShowTransitSignalPriorityDiagnostics/);
  assert.match(uiBindings, /showTransitSignalPriorityDiagnostics\s*\?\s*GetTransitSignalPriorityDiagnostics\(m_SelectedEntity,\s*tspSettings,\s*selectedJunction\)/);
  assert.match(uiBindings, /diagnostics\s*=\s*tspDiagnostics/);
  assert.match(uiSystem, /ShouldRefreshMainPanelForDiagnostics\(\)/);
  assert.match(uiSystem, /m_MainPanelState\s*==\s*MainPanelState\.Main/);
  assert.match(content, /const\s+transitSignalPriorityDiagnostics\s*=\s*mainData\.transitSignalPriority\?\.diagnostics/);
  assert.match(content, /transitSignalPriorityDiagnostics\.events/);
  assert.match(content, /transitSignalPriorityDiagnostics\.rows/);
  assert.match(locale, /Show diagnostics/);
  assert.doesNotMatch(locale, /Show transit signal priority diagnostics/);
  assert.match(locale, /TSPDiagnosticsRequest/);
  assert.match(locale, /TSPDiagnosticsCurrentGroup/);
  assert.match(locale, /TSPDiagnosticsCurveApproach/);
  assert.match(locale, /TSPDiagnosticsDecision/);
});

test("transit signal priority diagnostics option is declared after default options", async () => {
  const settings = await repoSource("Settings.cs");
  const diagnosticsIndex = settings.indexOf("public bool m_ShowTransitSignalPriorityDiagnostics");
  const defaultOptionNames = [
    "m_DefaultSplitPhasing",
    "m_DefaultAlwaysGreenKerbsideTurn",
    "m_DefaultExclusivePedestrian",
    "m_ForceNodeUpdate",
    "m_ComponentTypeToClear",
    "m_ClearSelectedComponent",
  ];

  assert.notEqual(diagnosticsIndex, -1);

  for (const optionName of defaultOptionNames) {
    const optionIndex = settings.indexOf(optionName);

    assert.notEqual(optionIndex, -1, `${optionName} is missing from Settings.cs`);
    assert.ok(optionIndex < diagnosticsIndex, `${optionName} should be declared before diagnostics`);
  }
});

test("backend provides transit signal priority summary and event history", async () => {
  const uiBindings = await repoSource("Systems/UI/UISystem.UIBIndings.cs");
  const uiSystem = await repoSource("Systems/UI/UISystem.cs");
  const locale = await repoSource("Locale.json");

  assert.match(uiBindings, /GetTspDiagnosticsSummary/);
  assert.match(uiBindings, /GetTspDiagnosticsEvents/);
  assert.match(uiBindings, /private\s+const\s+int\s+TspDiagnosticsEventHistoryLimit\s*=\s*100\s*;/);
  assert.match(uiBindings, /ShouldRecordTspDiagnosticsEvent/);
  assert.match(uiBindings, /RecordTspDiagnosticsEvent/);
  assert.match(uiBindings, /history\.Events\.Count\s*>\s*TspDiagnosticsEventHistoryLimit/);
  assert.match(uiBindings, /summary\s*=\s*GetTspDiagnosticsSummary/);
  assert.match(uiBindings, /diagnosticEvents\s*=\s*GetTspDiagnosticsEvents/);
  assert.match(uiBindings, /events\s*=\s*showTspEvents\s*\?\s*diagnosticEvents\s*:\s*new ArrayList\(\)/);
  assert.ok(
    uiBindings.indexOf("diagnosticEvents = GetTspDiagnosticsEvents") <
      uiBindings.indexOf("events = showTspEvents"),
    "trace collection must run before event visibility is filtered"
  );
  assert.match(uiSystem, /m_TspDiagnosticsEvents/);
  assert.match(locale, /TSPDiagnosticsSummary/);
  assert.match(locale, /TSPDiagnosticsEvents/);
});

test("backend exposes the retained transit signal priority history for scrolling", async () => {
  const uiBindings = await repoSource("Systems/UI/UISystem.UIBIndings.cs");
  const eventsStart = uiBindings.indexOf("private ArrayList GetTspDiagnosticsEvents");
  const eventsEnd = uiBindings.indexOf("private void PruneTspDiagnosticsEvents", eventsStart);
  const eventsSource = uiBindings.slice(eventsStart, eventsEnd);

  assert.notEqual(eventsStart, -1);
  assert.notEqual(eventsEnd, -1);
  assert.match(uiBindings, /private\s+const\s+int\s+TspDiagnosticsEventHistoryLimit\s*=\s*100\s*;/);
  assert.match(uiBindings, /history\.Events\.Count\s*>\s*TspDiagnosticsEventHistoryLimit/);
  assert.match(eventsSource, /foreach\s*\(\s*TspDiagnosticsEvent\s+diagnosticsEvent\s+in\s+history\.Events\s*\)/);
  assert.doesNotMatch(uiBindings, /TspDiagnosticsEventDisplayLimit/);
});

test("diagnostics panel keeps recent events above the full diagnostic rows", async () => {
  const content = await source("src/mods/components/main-panel/content.tsx");
  const diagnosticsStart = content.indexOf("styles.diagnosticsPane");
  const diagnosticsEnd = content.indexOf("if (emptyData)", diagnosticsStart);
  const diagnosticsSource = content.slice(diagnosticsStart, diagnosticsEnd);

  assert.notEqual(diagnosticsStart, -1);
  assert.notEqual(diagnosticsEnd, -1);
  assert.doesNotMatch(diagnosticsSource, /transitSignalPriorityDiagnostics\.summary/);
  assert.ok(
    diagnosticsSource.indexOf("transitSignalPriorityDiagnostics.events.map") <
      diagnosticsSource.indexOf("transitSignalPriorityDiagnostics.rows.map"),
    "event history should render before diagnostic rows"
  );
  assert.match(diagnosticsSource, /styles\.diagnosticEventTitle/);
  assert.match(diagnosticsSource, /styles\.diagnosticEventDetail/);
});

test("diagnostics use compact independently scrollable event and detail regions", async () => {
  const content = await source("src/mods/components/main-panel/content.tsx");
  const styles = await source("src/mods/components/main-panel/mainPanel.module.scss");
  const controlsPane = content.indexOf("styles.controlsPane");
  const diagnosticsCondition = content.indexOf("{transitSignalPriorityDiagnostics && (");
  const diagnosticsPane = content.indexOf("styles.diagnosticsPane", diagnosticsCondition);
  const diagnosticsTitle = content.indexOf("TrafficLightsEnhancement.TransitSignalPriorityDiagnostics", diagnosticsPane);

  assert.notEqual(controlsPane, -1);
  assert.notEqual(diagnosticsCondition, -1);
  assert.notEqual(diagnosticsPane, -1);
  assert.notEqual(diagnosticsTitle, -1);
  assert.ok(controlsPane < diagnosticsCondition);
  assert.ok(diagnosticsCondition < diagnosticsPane);
  assert.ok(diagnosticsPane < diagnosticsTitle);
  assert.match(styles, /\.controlsPane\s*\{[^}]*width:\s*18em;/s);
  assert.match(styles, /\.diagnosticsPane\s*\{[^}]*width:\s*30em;/s);
  assert.match(styles, /\.controlsPane\s*\{[^}]*background-color:\s*var\(--panelColorNormal\);/s);
  assert.match(styles, /\.diagnosticsPane\s*\{[^}]*background-color:\s*var\(--panelColorDark\);/s);
  assert.match(styles, /\.diagnosticsPane\s*\{[^}]*backdrop-filter:\s*var\(--panelBlur\);/s);
  assert.match(content, /styles\.diagnosticEventsSection[\s\S]*<Scrollable[\s\S]*transitSignalPriorityDiagnostics\.events\.map/);
  assert.match(content, /styles\.diagnosticDetailsSection[\s\S]*<Scrollable[\s\S]*transitSignalPriorityDiagnostics\.rows\.map/);
  assert.match(styles, /\.diagnosticsContent\s*\{[^}]*font-size:\s*0\.75em;/s);
  assert.match(styles, /\.diagnosticEventsSection\s*\{[^}]*height:\s*16em;/s);
  assert.match(styles, /\.diagnosticDetailsSection\s*\{[^}]*flex:\s*1;/s);
});

test("diagnostic section headings use the traffic-group heading treatment", async () => {
  const content = await source("src/mods/components/main-panel/content.tsx");
  const styles = await source("src/mods/components/main-panel/mainPanel.module.scss");

  assert.equal((content.match(/styles\.diagnosticSectionTitle/g) ?? []).length, 2);
  assert.match(styles, /\.diagnosticSectionTitle\s*\{[^}]*color:\s*var\(--textColorDim\);/s);
  assert.match(styles, /\.diagnosticSectionTitle\s*\{[^}]*font-weight:\s*400;/s);
  assert.match(styles, /\.diagnosticSectionTitle\s*\{[^}]*letter-spacing:\s*0\.04em;/s);
  assert.match(styles, /\.diagnosticSectionTitle\s*\{[^}]*text-align:\s*start;/s);
  assert.match(styles, /\.diagnosticSectionTitle\s*\{[^}]*text-transform:\s*uppercase;/s);
});

test("traffic group sections use the native-style secondary heading treatment", async () => {
  const styles = await source("src/mods/components/traffic-groups/main-panel/IndexComponent/index.module.scss");

  assert.match(styles, /\.sectionTitle\s*\{[^}]*color:\s*var\(--textColorDim\);/s);
  assert.match(styles, /\.sectionTitle\s*\{[^}]*font-size:\s*0\.75em;/s);
  assert.match(styles, /\.sectionTitle\s*\{[^}]*text-align:\s*start;/s);
  assert.match(styles, /\.sectionTitle\s*\{[^}]*text-transform:\s*uppercase;/s);
});

test("traffic group details use the same dark panel background as diagnostics", async () => {
  const styles = await source("src/mods/components/traffic-groups/main-panel/IndexComponent/index.module.scss");

  assert.match(styles, /\.rightPanelContainer\s*\{[^}]*background-color:\s*var\(--panelColorDark\);/s);
  assert.match(styles, /\.rightPanelContainer\s*\{[^}]*backdrop-filter:\s*var\(--panelBlur\);/s);
});

test("traffic group labels are start-aligned and the name field fills its row", async () => {
  const styles = await source("src/mods/components/traffic-groups/main-panel/IndexComponent/index.module.scss");

  assert.match(styles, /\.rightPanelContainer\s*\{[^}]*text-align:\s*start;/s);
  assert.match(styles, /\.infoText\s*\{[^}]*text-align:\s*start;/s);
  assert.match(styles, /\.nameInputContainer\s*\{[\s\S]*?input\s*\{[^}]*max-width:\s*none;/s);
  assert.match(styles, /\.nameInputContainer\s*\{[\s\S]*?>\s*div\s*\{[^}]*flex:\s*1;/s);
});

test("traffic group list labels and icon buttons share vertical alignment", async () => {
  const styles = await source("src/mods/components/traffic-groups/main-panel/GroupItemComponent/group-item.module.scss");

  assert.match(styles, /\.labelWrapper\s*\{[^}]*align-items:\s*center;/s);
  assert.match(styles, /\.labelWrapper\s*\{[^}]*display:\s*flex;/s);
  assert.match(styles, /\.icon-bar-container\s*\{[^}]*align-items:\s*center;/s);
  assert.match(styles, /\.icon-container\s*\{[^}]*align-items:\s*center;/s);
  assert.match(styles, /\.icon-container\s*\{[^}]*justify-content:\s*center;/s);
});

test("add-member actions stack vertically within the panel", async () => {
  const styles = await source("src/mods/components/main-panel/mainPanel.module.scss");

  assert.match(styles, /\.buttonRow\s*\{[^}]*flex-direction:\s*column;/s);
  assert.match(styles, /\.buttonRow\s*\{[\s\S]*?>\s*\*\s*\{[^}]*width:\s*100%;/s);
});

test("add-member heading presents its localized action and group name separately", async () => {
  const content = await source("src/mods/components/main-panel/content.tsx");
  const styles = await source("src/mods/components/main-panel/mainPanel.module.scss");

  assert.doesNotMatch(content, /AddingMemberTo:\$\{emptyData\.targetGroupName\}/);
  assert.match(content, /UI\.LABEL\[C2VM\.TrafficLightsEnhancement\.AddMember\]/);
  assert.match(content, /styles\.addMemberGroupName[\s\S]*emptyData\.targetGroupName/);
  assert.match(styles, /\.addMemberHeader\s*\{[^}]*flex-direction:\s*column;/s);
  assert.match(styles, /\.addMemberTitle\s*\{[^}]*text-transform:\s*uppercase;/s);
});

test("compact main panels are wide enough for the product title", async () => {
  const content = await source("src/mods/components/main-panel/content.tsx");
  const styles = await source("src/mods/components/main-panel/mainPanel.module.scss");
  const mainPanelStart = content.indexOf("if (mainData)");
  const compactPanelStart = content.indexOf("if (emptyData)");
  const mainPanelSource = content.slice(mainPanelStart, compactPanelStart);
  const compactPanelSource = content.slice(compactPanelStart);

  assert.doesNotMatch(mainPanelSource, /styles\.compactControlsPane/);
  assert.match(compactPanelSource, /styles\.controlsPane\}\s+\$\{styles\.compactControlsPane/);
  assert.match(styles, /\.compactControlsPane\s*\{[^}]*width:\s*20\.5em;/s);
});

test("custom phase details use the standard dark panel background", async () => {
  const styles = await source("src/mods/components/custom-phase-tool/main-panel/modules/index.module.scss");

  assert.match(styles, /\.rightPanelContainer\s*\{[^}]*background-color:\s*var\(--panelColorDark\);/s);
  assert.match(styles, /\.rightPanelContainer\s*\{[^}]*backdrop-filter:\s*var\(--panelBlur\);/s);
  assert.doesNotMatch(styles, /\.rightPanelContainer\s*\{[^}]*var\(--sectionBackgroundColor\)/s);
});

test("main panel section titles use the secondary all-caps treatment", async () => {
  const title = await source("src/mods/components/main-panel/items/title.tsx");

  assert.match(title, /color:\s*var\(--textColorDim\);/);
  assert.match(title, /font-size:\s*0\.75em;/);
  assert.match(title, /font-weight:\s*400;/);
  assert.match(title, /letter-spacing:\s*0\.04em;/);
  assert.match(title, /text-align:\s*start;/);
  assert.match(title, /text-transform:\s*uppercase;/);
});

test("group statistics uses the group section-header treatment", async () => {
  const trafficGroups = await source("src/mods/components/traffic-groups/main-panel/IndexComponent/index.tsx");

  assert.match(
    trafficGroups,
    /className=\{styles\.sectionTitle\}[^>]*>\{translate\("UI\.LABEL\[C2VM\.TrafficLightsEnhancement\.Statistics\]"\)/
  );
  assert.doesNotMatch(trafficGroups, /<ItemTitle title="Statistics"\s*\/>/);
});

test("backend event history provides compact event title and detail fields", async () => {
  const general = await source("src/mods/general.ts");
  const uiBindings = await repoSource("Systems/UI/UISystem.UIBIndings.cs");
  const eventsStart = uiBindings.indexOf("private ArrayList GetTspDiagnosticsEvents");
  const eventsEnd = uiBindings.indexOf("private void PruneTspDiagnosticsEvents", eventsStart);
  const eventsSource = uiBindings.slice(eventsStart, eventsEnd);

  assert.match(general, /events\?:\s*Array<\{\s*sequence:\s*number,\s*title:\s*string,\s*detail:\s*string\s*\}>/);
  assert.match(uiBindings, /title\s*=\s*\$"#\{diagnosticsEvent\.Sequence\} \{eventTitle\}"/);
  assert.match(uiBindings, /detail\s*=\s*eventDetail/);
  assert.doesNotMatch(eventsSource, /label\s*=\s*"TSPDiagnosticsEvent"/);
  assert.doesNotMatch(eventsSource, /value\s*=\s*\$"#\{diagnosticsEvent\.Sequence\} \{diagnosticsEvent\.Value\}"/);
});

test("backend hides empty bus detail diagnostics until a bus sample exists", async () => {
  const uiBindings = await repoSource("Systems/UI/UISystem.UIBIndings.cs");
  const rowsStart = uiBindings.indexOf("if (hasBusApproachDebug)");
  const rowsEnd = uiBindings.indexOf("if (hasDecisionTrace)", rowsStart);
  const busRowsSource = uiBindings.slice(rowsStart, rowsEnd);

  assert.notEqual(rowsStart, -1);
  assert.notEqual(rowsEnd, -1);
  assert.match(busRowsSource, /bool\s+hasBusSample\s*=\s*busApproachDebug\.m_BusHitCount\s*>\s*0/);
  assert.match(busRowsSource, /if\s*\(\s*hasBusSample\s*\)/);
  assert.ok(
    busRowsSource.indexOf("if (hasBusSample)") <
      busRowsSource.indexOf("TSPDiagnosticsBusLane"),
    "bus lane and vehicle details should be inside the sampled-bus block"
  );
  assert.match(busRowsSource, /if\s*\(\s*busApproachDebug\.m_BusTargetSignalGroup\s*>\s*0\s*\)/);
  assert.doesNotMatch(busRowsSource, /TSPDiagnosticsBusNavigationLanes[\s\S]*\?\s*busApproachDebug\.m_BusNavigationLaneCount\.ToString\(CultureInfo\.InvariantCulture\)\s*:\s*"-"/);
});

test("backend keeps in-game diagnostics source-aware and hides raw deep debug rows", async () => {
  const uiBindings = await repoSource("Systems/UI/UISystem.UIBIndings.cs");
  const locale = JSON.parse(await repoSource("Locale.json"));
  const rowsStart = uiBindings.indexOf("if (hasRuntimeDebug)");
  const rowsEnd = uiBindings.indexOf("if (hasBusApproachDebug)", rowsStart);
  const runtimeRowsSource = uiBindings.slice(rowsStart, rowsEnd);
  const busRowsEnd = uiBindings.indexOf("if (hasDecisionTrace)", rowsEnd);
  const busRowsSource = uiBindings.slice(rowsEnd, busRowsEnd);

  assert.notEqual(rowsStart, -1);
  assert.notEqual(rowsEnd, -1);
  assert.notEqual(busRowsEnd, -1);
  assert.match(runtimeRowsSource, /bool\s+isTrackRequest\s*=\s*\(global::TrafficLightsEnhancement\.Logic\.Tsp\.TspSource\)runtimeDebug\.m_SourceType\s*==\s*global::TrafficLightsEnhancement\.Logic\.Tsp\.TspSource\.Track/);
  assert.match(runtimeRowsSource, /if\s*\(\s*isTrackRequest\s*\)/);
  assert.ok(
    runtimeRowsSource.indexOf("if (isTrackRequest)") <
      runtimeRowsSource.indexOf("TSPDiagnosticsProbeSignaled"),
    "tram probe fields should only render for track requests"
  );
  assert.doesNotMatch(busRowsSource, /TSPDiagnosticsBusNavigationLanes/);
  assert.doesNotMatch(busRowsSource, /TSPDiagnosticsBusTransportState/);
  assert.doesNotMatch(busRowsSource, /TSPDiagnosticsBusVehicleFlags/);
  assert.equal(locale["UI.LABEL[C2VM.TrafficLightsEnhancement.TSPDiagnosticsBusNavigationLanes]"], undefined);
  assert.equal(locale["UI.LABEL[C2VM.TrafficLightsEnhancement.TSPDiagnosticsBusTransportState]"], undefined);
  assert.equal(locale["UI.LABEL[C2VM.TrafficLightsEnhancement.TSPDiagnosticsBusVehicleFlags]"], undefined);
});

test("backend reports current-phase extension only while target group is current", async () => {
  const uiBindings = await repoSource("Systems/UI/UISystem.UIBIndings.cs");
  const rowsStart = uiBindings.indexOf("if (hasRuntimeDebug)");
  const rowsEnd = uiBindings.indexOf("if (hasBusApproachDebug)", rowsStart);
  const runtimeRowsSource = uiBindings.slice(rowsStart, rowsEnd);

  assert.notEqual(rowsStart, -1);
  assert.notEqual(rowsEnd, -1);
  assert.match(runtimeRowsSource, /bool\s+isCurrentTargetGroup\s*=\s*hasTrafficLights\s*&&\s*runtimeDebug\.m_TargetSignalGroup\s*==\s*trafficLights\.m_CurrentSignalGroup/);
  assert.match(runtimeRowsSource, /bool\s+isExtendingCurrentPhase\s*=\s*runtimeDebug\.m_ExtendCurrentPhase\s*&&\s*isCurrentTargetGroup/);
  assert.match(runtimeRowsSource, /TSPDiagnosticsExtend\",\s*value\s*=\s*isExtendingCurrentPhase\s*\?\s*\"Yes\"\s*:\s*\"No\"/);
});

test("diagnostic row labels are localized", async () => {
  const uiBindings = await repoSource("Systems/UI/UISystem.UIBIndings.cs");
  const locale = JSON.parse(await repoSource("Locale.json"));
  const labelPattern = /label\s*=\s*"([^"]+)"/g;
  const labels = new Set();
  let match;

  while ((match = labelPattern.exec(uiBindings)) !== null) {
    if (match[1].startsWith("TSPDiagnostics")) {
      labels.add(match[1]);
    }
  }

  for (const label of labels) {
    const localeKey = `UI.LABEL[C2VM.TrafficLightsEnhancement.${label}]`;

    assert.equal(typeof locale[localeKey], "string", `${label} needs a locale entry`);
    assert.notEqual(locale[localeKey].trim(), "", `${label} locale entry cannot be empty`);
  }

  assert.equal(
    locale["UI.LABEL[C2VM.TrafficLightsEnhancement.TSPDiagnosticsPendingPedestrianFairness]"],
    "Pedestrian phase due"
  );
});

test("backend exposes selected junction expected UI rows in TSP diagnostics", async () => {
  const uiBindings = await repoSource("Systems/UI/UISystem.UIBIndings.cs");
  const locale = JSON.parse(await repoSource("Locale.json"));

  const requiredLabels = [
    "TSPDiagnosticsJunctionTopology",
    "TSPDiagnosticsAvailablePatterns",
    "TSPDiagnosticsExtraOptions",
    "TSPDiagnosticsOptionTurningOnRed",
    "TSPDiagnosticsOptionGiveWay",
    "TSPDiagnosticsOptionExclusivePedestrian",
    "TSPDiagnosticsPedestrianDuration",
    "TSPDiagnosticsTramControl",
    "TSPDiagnosticsBusControl",
  ];

  for (const label of requiredLabels) {
    assert.match(uiBindings, new RegExp(`label\\s*=\\s*"${label}"`));
    assert.equal(typeof locale[`UI.LABEL[C2VM.TrafficLightsEnhancement.${label}]`], "string");
  }
});

test("backend exposes read-only traffic group rows in TSP diagnostics", async () => {
  const uiBindings = await repoSource("Systems/UI/UISystem.UIBIndings.cs");
  const locale = JSON.parse(await repoSource("Locale.json"));

  const requiredLabels = [
    "TSPDiagnosticsTrafficGroupRole",
    "TSPDiagnosticsTrafficGroupMode",
    "TSPDiagnosticsTrafficGroupCycleLength",
    "TSPDiagnosticsTrafficGroupSignalDelay",
    "TSPDiagnosticsTrafficGroupPhaseOffset",
    "TSPDiagnosticsTrafficGroupMemberCycleTimer",
    "TSPDiagnosticsTrafficGroupMasterPhase",
    "TSPDiagnosticsTrafficGroupTspSuspended",
  ];

  for (const label of requiredLabels) {
    assert.match(uiBindings, new RegExp(`label\\s*=\\s*"${label}"`));
    assert.equal(typeof locale[`UI.LABEL[C2VM.TrafficLightsEnhancement.${label}]`], "string");
    assert.notEqual(locale[`UI.LABEL[C2VM.TrafficLightsEnhancement.${label}]`].trim(), "");
  }
});

test("backend presents bus-originated TSP requests as bus diagnostics", async () => {
  const uiBindings = await repoSource("Systems/UI/UISystem.UIBIndings.cs");
  const helperStart = uiBindings.indexOf("private static string GetTspSourceName");
  const helperEnd = uiBindings.indexOf("private static string GetTrackProbeName", helperStart);
  const helperSource = uiBindings.slice(helperStart, helperEnd);

  assert.notEqual(helperStart, -1);
  assert.notEqual(helperEnd, -1);
  assert.match(helperSource, /TspSource\.PublicCar\s*=>\s*"Bus"/);
  assert.doesNotMatch(helperSource, /"Public car"/);
});

test("backend hides pedestrian decision diagnostics when no pedestrian context is active", async () => {
  const uiBindings = await repoSource("Systems/UI/UISystem.UIBIndings.cs");
  const traceStart = uiBindings.indexOf("if (hasDecisionTrace)");
  const traceEnd = uiBindings.indexOf("return new { summary, events, rows };", traceStart);
  const decisionRowsSource = uiBindings.slice(traceStart, traceEnd);

  assert.notEqual(traceStart, -1);
  assert.notEqual(traceEnd, -1);
  assert.match(decisionRowsSource, /HasPedestrianDecisionContext\(decisionTrace\)/);
  assert.match(decisionRowsSource, /else\s*\{\s*rows\.Add\(new\s*\{\s*label\s*=\s*"TSPDiagnosticsDecision",\s*value\s*=\s*"None"\s*\}\);\s*\}/);
  assert.ok(
    decisionRowsSource.indexOf("HasPedestrianDecisionContext(decisionTrace)") <
      decisionRowsSource.indexOf("TSPDiagnosticsExclusivePedestrian"),
    "pedestrian rows should be gated behind an active pedestrian decision context"
  );
});

test("backend writes selected transit signal priority diagnostics to a trace file", async () => {
  const uiBindings = await repoSource("Systems/UI/UISystem.UIBIndings.cs");

  assert.match(uiBindings, /TspDiagnosticsTraceFileName/);
  assert.match(uiBindings, /C2VM\.TrafficLightsEnhancement\.TspDiagnostics\.jsonl/);
  assert.match(uiBindings, /WriteTspDiagnosticsTraceEvent/);
  assert.match(uiBindings, /Application\.persistentDataPath/);
  assert.match(uiBindings, /simulationFrame\s*=\s*m_SimulationSystem\.frameIndex/);
  assert.match(uiBindings, /signalConfiguration\s*=\s*GetTspSignalConfigurationTrace/);
  assert.match(uiBindings, /trafficGroup\s*=\s*GetTspTrafficGroupTrace/);
  assert.match(uiBindings, /laneSignals\s*=\s*GetTspLaneSignalTrace/);
  assert.match(uiBindings, /TspDiagnosticsTraceFileLock/);
  assert.match(uiBindings, /RotateTspDiagnosticsTraceFileIfNeeded/);
  assert.match(uiBindings, /TspDiagnosticsTraceMaxRotatedFiles/);
  assert.match(uiBindings, /PruneTspDiagnosticsTraceFiles/);
  assert.match(uiBindings, /FileMode\.Append/);
});

test("backend derives selected junction panel and diagnostics from a shared snapshot", async () => {
  const uiBindings = await repoSource("Systems/UI/UISystem.UIBIndings.cs");
  const mainPanelStart = uiBindings.indexOf("protected string GetMainPanel()");
  const mainPanelEnd = uiBindings.indexOf("else if (m_MainPanelState == MainPanelState.CustomPhase)", mainPanelStart);
  const mainPanelSource = uiBindings.slice(mainPanelStart, mainPanelEnd);
  const traceStart = uiBindings.indexOf("private void WriteTspDiagnosticsTraceEvent");
  const traceEnd = uiBindings.indexOf("private object GetTspSignalConfigurationTrace", traceStart);
  const traceSource = uiBindings.slice(traceStart, traceEnd);

  assert.notEqual(mainPanelStart, -1);
  assert.notEqual(mainPanelEnd, -1);
  assert.notEqual(traceStart, -1);
  assert.notEqual(traceEnd, -1);
  assert.match(uiBindings, /private\s+SelectedJunctionDiagnosticsSnapshot\s+GetSelectedJunctionDiagnosticsSnapshot/);
  assert.match(mainPanelSource, /SelectedJunctionDiagnosticsSnapshot\s+selectedJunction\s*=\s*GetSelectedJunctionDiagnosticsSnapshot\(\s*m_SelectedEntity,\s*tspSettings,\s*showTransitSignalPriorityDiagnostics\s*\)/);
  assert.match(traceSource, /selectedJunction\s*=\s*selectedJunction\.ToTraceObject\(\)/);
});

test("backend trace includes selected junction topology and expected UI state", async () => {
  const uiBindings = await repoSource("Systems/UI/UISystem.UIBIndings.cs");

  assert.match(uiBindings, /selectedJunction\s*=\s*selectedJunction\.ToTraceObject\(\)/);
  assert.match(uiBindings, /connectedEdgeCount\s*=\s*ConnectedEdgeCount/);
  assert.match(uiBindings, /hasTrainTrack\s*=\s*HasTrainTrack/);
  assert.match(uiBindings, /hasTrackTurnLanes\s*=\s*HasTrackTurnLanes/);
  assert.match(uiBindings, /isQualifyingFourWay\s*=\s*IsQualifyingFourWay/);
  assert.match(uiBindings, /isComplexJunction\s*=\s*IsComplexJunction/);
  assert.match(uiBindings, /trainTrackCount\s*=\s*TrainTrackCount/);
  assert.match(uiBindings, /trackLaneLeftCount\s*=\s*TrackLaneLeftCount/);
  assert.match(uiBindings, /trackLaneStraightCount\s*=\s*TrackLaneStraightCount/);
  assert.match(uiBindings, /trackLaneRightCount\s*=\s*TrackLaneRightCount/);
  assert.match(uiBindings, /totalTrackLaneCount\s*=\s*TotalTrackLaneCount/);
  assert.match(uiBindings, /splitPhasingSupported\s*=\s*SplitPhasingSupported/);
  assert.match(uiBindings, /protectedCentreTurnSupported\s*=\s*ProtectedCentreTurnSupported/);
  assert.match(uiBindings, /splitPhasingProtectedLeftSupported\s*=\s*SplitPhasingProtectedLeftSupported/);
  assert.match(uiBindings, /availablePatterns\s*=\s*AvailablePatterns/);
  assert.match(uiBindings, /turningOnRed\s*=\s*TurningOnRed\.ToTraceObject\(\)/);
  assert.match(uiBindings, /giveWayToOncomingVehicles\s*=\s*GiveWayToOncomingVehicles\.ToTraceObject\(\)/);
  assert.match(uiBindings, /exclusivePedestrianPhase\s*=\s*ExclusivePedestrianPhase\.ToTraceObject\(\)/);
  assert.match(uiBindings, /pedestrianDurationAdjustment\s*=\s*PedestrianDurationAdjustment\.ToTraceObject\(\)/);
  assert.match(uiBindings, /tram\s*=\s*TramTransitPriority\.ToTraceObject\(\)/);
  assert.match(uiBindings, /bus\s*=\s*BusTransitPriority\.ToTraceObject\(\)/);
});

test("backend diagnostics snapshot exposes expected fields", async () => {
  const uiBindings = await repoSource("Systems/UI/UISystem.UIBIndings.cs");
  const snapshotStart = uiBindings.indexOf("private sealed class SelectedJunctionDiagnosticsSnapshot");
  const snapshotEnd = uiBindings.indexOf("public ArrayList AvailablePatterns", snapshotStart);
  const snapshotSource = uiBindings.slice(snapshotStart, snapshotEnd);

  assert.notEqual(snapshotStart, -1);
  assert.notEqual(snapshotEnd, -1);
  assert.match(snapshotSource, /public\s+int\s+TrainTrackCount;/);
  assert.match(snapshotSource, /public\s+int\s+TrackLaneLeftCount;/);
  assert.match(snapshotSource, /public\s+int\s+TrackLaneStraightCount;/);
  assert.match(snapshotSource, /public\s+int\s+TrackLaneRightCount;/);
  assert.match(snapshotSource, /public\s+int\s+TotalTrackLaneCount;/);
});

test("backend distinguishes selected junction option support from current visibility", async () => {
  const uiBindings = await repoSource("Systems/UI/UISystem.UIBIndings.cs");
  const snapshotStart = uiBindings.indexOf("private SelectedJunctionDiagnosticsSnapshot GetSelectedJunctionDiagnosticsSnapshot");
  const snapshotEnd = uiBindings.indexOf("private List<SelectedJunctionPatternSnapshot> GetAvailablePatternSnapshots", snapshotStart);
  const snapshotSource = uiBindings.slice(snapshotStart, snapshotEnd);

  assert.notEqual(snapshotStart, -1);
  assert.notEqual(snapshotEnd, -1);
  assert.match(snapshotSource, /bool\s+extraOptionsSupported\s*=\s*!hasTrainTrack\s*&&\s*edgeInfoArray\.Length\s*<=\s*7/);
  assert.match(snapshotSource, /bool\s+extraOptionsVisible\s*=\s*extraOptionsSupported\s*&&\s*patternOnly\s*<\s*\(uint\)CustomTrafficLights\.Patterns\.ModDefault/);
  assert.match(snapshotSource, /ExtraOptionsSupported\s*=\s*extraOptionsSupported/);
  assert.match(snapshotSource, /ExtraOptionsVisible\s*=\s*extraOptionsVisible/);
  assert.match(snapshotSource, /GetExtraOptionsReason\(patternOnly,\s*hasTrainTrack,\s*edgeInfoArray\.Length\)/);
});

test("backend only builds per-edge selected junction trace details when diagnostics are enabled", async () => {
  const uiBindings = await repoSource("Systems/UI/UISystem.UIBIndings.cs");
  const mainPanelStart = uiBindings.indexOf("protected string GetMainPanel()");
  const mainPanelEnd = uiBindings.indexOf("else if (m_MainPanelState == MainPanelState.CustomPhase)", mainPanelStart);
  const mainPanelSource = uiBindings.slice(mainPanelStart, mainPanelEnd);
  const snapshotStart = uiBindings.indexOf("private SelectedJunctionDiagnosticsSnapshot GetSelectedJunctionDiagnosticsSnapshot");
  const snapshotEnd = uiBindings.indexOf("private List<SelectedJunctionPatternSnapshot> GetAvailablePatternSnapshots", snapshotStart);
  const snapshotSource = uiBindings.slice(snapshotStart, snapshotEnd);

  assert.match(mainPanelSource, /bool\s+showTransitSignalPriorityDiagnostics\s*=\s*Mod\.m_Setting\s*!=\s*null\s*&&\s*Mod\.m_Setting\.m_ShowTransitSignalPriorityDiagnostics/);
  assert.match(mainPanelSource, /GetSelectedJunctionDiagnosticsSnapshot\(\s*m_SelectedEntity,\s*tspSettings,\s*showTransitSignalPriorityDiagnostics\s*\)/);
  assert.match(snapshotSource, /bool\s+includeDiagnosticsDetails/);
  assert.match(snapshotSource, /EdgeSummaries\s*=\s*includeDiagnosticsDetails\s*\?\s*GetSelectedJunctionEdgeSummaries\(edgeInfoArray\)\s*:\s*\[\]/);
});

test("runtime suspends transit signal priority for all traffic group members", async () => {
  const runtime = await repoSource("Systems/TrafficLightSystems/Simulation/TransitSignalPriorityRuntime.cs");
  const patchedSystem = await repoSource("Systems/TrafficLightSystems/Simulation/PatchedTrafficLightSystem.cs");
  const policy = await repoSource("../TrafficLightsEnhancement.Logic/Tsp/TspPolicy.cs");
  const uiBindings = await repoSource("Systems/UI/UISystem.UIBIndings.cs");

  assert.match(runtime, /return\s+!job\.m_ExtraTypeHandle\.m_TrafficGroupMember\.HasComponent\(junctionEntity\)/);
  assert.doesNotMatch(runtime, /return\s+member\.m_IsGroupLeader/);
  assert.match(patchedSystem, /bool\s+isGroupedIntersection\s*=\s*m_TrafficGroupMemberLookup\.HasComponent\(entity\)/);
  assert.doesNotMatch(patchedSystem, /bool\s+isGroupedFollower\s*=/);
  assert.match(policy, /IsApproachIndexEligibleSetting\(\s*TransitSignalPrioritySettings\s+settings,\s*bool\s+isGroupedIntersection/);
  assert.match(policy, /IsBusApproachIndexEligibleSetting\(\s*TransitSignalPrioritySettings\s+settings,\s*bool\s+isGroupedIntersection/);
  assert.match(uiBindings, /isEditable\s*=\s*!isTrafficGroupMember/);
});

test("bus requests remember extension eligibility after the target group becomes current", async () => {
  const runtime = await repoSource("Systems/TrafficLightSystems/Simulation/TransitSignalPriorityRuntime.cs");
  const createStart = runtime.indexOf("private static TransitSignalPriorityRequest CreateRequest");
  const createEnd = runtime.indexOf("private static TspSignalRequest ToSignalRequest", createStart);
  const createSource = runtime.slice(createStart, createEnd);

  assert.notEqual(createStart, -1);
  assert.notEqual(createEnd, -1);
  assert.match(createSource, /m_ExtendCurrentPhase\s*=\s*request\.ExtensionEligible\s*&&\s*\(laneSignal\.m_Flags\s*&\s*LaneSignalFlags\.CanExtend\)\s*!=\s*0/);
  assert.doesNotMatch(createSource, /currentSignalGroup\s*==\s*targetSignalGroup/);
});

test("backend trace writes follow selected diagnostics event filtering", async () => {
  const uiBindings = await repoSource("Systems/UI/UISystem.UIBIndings.cs");
  const eventsStart = uiBindings.indexOf("private ArrayList GetTspDiagnosticsEvents");
  const eventsEnd = uiBindings.indexOf("private void PruneTspDiagnosticsEvents", eventsStart);
  const eventsSource = uiBindings.slice(eventsStart, eventsEnd);

  assert.notEqual(eventsStart, -1);
  assert.notEqual(eventsEnd, -1);
  assert.match(eventsSource, /bool\s+hasTspActivity\s*=\s*hasRuntimeDebug\s*\|\|\s*hasBusApproachDebug\s*\|\|\s*hasDecisionTrace/);
  assert.match(eventsSource, /bool\s+shouldRecordEvent\s*=\s*isNewSelection\s*\|\|\s*\(\s*signatureChanged\s*&&\s*ShouldRecordTspDiagnosticsEvent\(\s*history,\s*hasTspActivity,\s*EntityManager\.HasComponent<TrafficGroupMember>\(entity\)\)\s*\)/);
  assert.match(eventsSource, /if\s*\(\s*signatureChanged\s*\)/);
  assert.match(eventsSource, /if\s*\(\s*shouldRecordEvent\s*\)/);
  assert.ok(eventsSource.indexOf("bool shouldRecordEvent") < eventsSource.indexOf("WriteTspDiagnosticsTraceEvent"));
  assert.ok(eventsSource.indexOf("bool shouldRecordEvent") < eventsSource.indexOf("RecordTspDiagnosticsEvent"));
});

test("backend treats reselecting a previously tracked junction as a new diagnostics selection", async () => {
  const uiBindings = await repoSource("Systems/UI/UISystem.UIBIndings.cs");
  const uiSystem = await repoSource("Systems/UI/UISystem.cs");
  const eventsStart = uiBindings.indexOf("private ArrayList GetTspDiagnosticsEvents");
  const eventsEnd = uiBindings.indexOf("private void PruneTspDiagnosticsEvents", eventsStart);
  const eventsSource = uiBindings.slice(eventsStart, eventsEnd);

  assert.notEqual(eventsStart, -1);
  assert.notEqual(eventsEnd, -1);
  assert.match(uiSystem, /private\s+Entity\s+m_TspDiagnosticsSelectedEntity\s*=\s*Entity\.Null\s*;/);
  assert.match(eventsSource, /bool\s+selectionChanged\s*=\s*m_TspDiagnosticsSelectedEntity\s*!=\s*entity\s*;/);
  assert.match(eventsSource, /bool\s+isNewSelection\s*=\s*selectionChanged\s*\|\|\s*isNewHistory\s*;/);
  assert.match(eventsSource, /m_TspDiagnosticsSelectedEntity\s*=\s*entity\s*;/);
  assert.ok(
    eventsSource.indexOf("bool isNewSelection") < eventsSource.indexOf("bool shouldRecordEvent"),
    "selection-change state should feed trace event filtering"
  );
});

test("selecting a junction for the first time forces an initial diagnostics trace write", async () => {
  const uiBindings = await repoSource("Systems/UI/UISystem.UIBIndings.cs");
  const eventsStart = uiBindings.indexOf("private ArrayList GetTspDiagnosticsEvents");
  const eventsEnd = uiBindings.indexOf("private void PruneTspDiagnosticsEvents", eventsStart);
  const eventsSource = uiBindings.slice(eventsStart, eventsEnd);

  assert.notEqual(eventsStart, -1);
  assert.notEqual(eventsEnd, -1);
  // A junction that is not yet tracked must be flagged as a new selection so the first
  // selection always writes an initial trace, even when the junction is idle (issue #114).
  assert.match(eventsSource, /if\s*\(\s*!m_TspDiagnosticsEvents\.TryGetValue\(\s*entity,\s*out\s+TspDiagnosticsHistory\s+history\s*\)\s*\)/);
  assert.match(eventsSource, /m_TspDiagnosticsEvents\[entity\]\s*=\s*history;\s*isNewHistory\s*=\s*true;/);
});

test("backend owns generated edge info arrays instead of leaking native lists", async () => {
  const nodeUtils = await repoSource("Utils/NodeUtils.cs");
  const initializationSystem = await repoSource("Systems/TrafficLightSystems/Initialisation/PatchedTrafficLightInitializationSystem.cs");
  const uiSystem = await repoSource("Systems/UI/UISystem.cs");

  assert.match(nodeUtils, /public\s+static\s+NativeArray<EdgeInfo>\s+GetEdgeInfoList/);
  assert.doesNotMatch(nodeUtils, /public\s+static\s+NativeList<EdgeInfo>\s+GetEdgeInfoList/);
  assert.match(nodeUtils, /using\s+NativeList<EdgeInfo>\s+edgeInfoList\s*=\s*new\(4,\s*Allocator\.Temp\)/);
  assert.match(nodeUtils, /edgeInfo\.m_SubLaneInfoList\s*=\s*new\s+NativeArray<SubLaneInfo>\(subLaneInfoList\.Length,\s*allocator\)/);
  assert.match(nodeUtils, /var\s+edgeInfoArray\s*=\s*new\s+NativeArray<EdgeInfo>\(edgeInfoList\.Length,\s*allocator\)/);
  assert.match(initializationSystem, /var\s+edgeInfoArray\s*=\s*NodeUtils\.GetEdgeInfoList\(Allocator\.Temp/);
  assert.match(initializationSystem, /NodeUtils\.Dispose\(edgeInfoArray\)/);
  assert.doesNotMatch(initializationSystem, /GetEdgeInfoList\([\s\S]*?\)\.AsArray\(\)/);
  assert.doesNotMatch(uiSystem, /GetEdgeInfoList\([\s\S]*?\)\.AsArray\(\)/);
});

test("diagnostics trace logging is limited to the initial selection write", async () => {
  const uiBindings = await repoSource("Systems/UI/UISystem.UIBIndings.cs");

  // The forced initial write logs exactly once, gated on the new-selection flag, so live QA
  // can confirm a selection change triggered a write without spamming Player.log per event.
  const eventsStart = uiBindings.indexOf("private ArrayList GetTspDiagnosticsEvents");
  const eventsEnd = uiBindings.indexOf("private void PruneTspDiagnosticsEvents", eventsStart);
  const eventsSource = uiBindings.slice(eventsStart, eventsEnd);
  assert.notEqual(eventsStart, -1);
  assert.notEqual(eventsEnd, -1);
  assert.match(eventsSource, /if\s*\(\s*isNewSelection\s*\)\s*{\s*Mod\.log\.Info\(/);

  // The per-event trace writer must not log on every successful write (avoids Player.log spam),
  // but must still log failures with the exception-first Error overload.
  const writeStart = uiBindings.indexOf("private void WriteTspDiagnosticsTraceEvent");
  const writeEnd = uiBindings.indexOf("private SelectedJunctionDiagnosticsSnapshot GetSelectedJunctionDiagnosticsSnapshot", writeStart);
  const writeSource = uiBindings.slice(writeStart, writeEnd);
  assert.notEqual(writeStart, -1);
  assert.notEqual(writeEnd, -1);
  assert.doesNotMatch(writeSource, /Mod\.log\.Info\(/);
  assert.match(writeSource, /Mod\.log\.Error\(\s*ex,/);
});

test("static locale provides descriptions for visible mod options", async () => {
  const locale = JSON.parse(await repoSource("Locale.json"));
  const optionPrefix =
    "Options.OPTION[C2VM.TrafficLightsEnhancement.C2VM.TrafficLightsEnhancement.Mod.Settings.";
  const descriptionPrefix =
    "Options.OPTION_DESCRIPTION[C2VM.TrafficLightsEnhancement.C2VM.TrafficLightsEnhancement.Mod.Settings.";
  const visibleOptions = [
    "m_LocaleOption",
    "m_CompatibilityModeOption",
    "m_DefaultSplitPhasing",
    "m_DefaultAlwaysGreenKerbsideTurn",
    "m_DefaultExclusivePedestrian",
    "m_ShowTransitSignalPriorityDiagnostics",
    "m_ForceNodeUpdate",
    "m_ComponentTypeToClear",
    "m_ClearSelectedComponent",
    "m_ReleaseChannel",
    "m_TleVersion",
    "m_SuppressCanaryWarning",
    "m_MainPanelToggleKeyboardBinding",
    "m_MultiSelectEntityKeyboardBinding",
    "m_ResetBindings",
  ];

  for (const option of visibleOptions) {
    const optionKey = `${optionPrefix}${option}]`;
    const descriptionKey = `${descriptionPrefix}${option}]`;

    assert.equal(typeof locale[optionKey], "string", `${option} needs a label`);
    assert.equal(typeof locale[descriptionKey], "string", `${option} needs a description`);
    assert.notEqual(locale[descriptionKey].trim(), "", `${option} description cannot be empty`);
    assert.doesNotMatch(locale[descriptionKey], /^Options\.OPTION_DESCRIPTION/, `${option} description cannot be a raw localization key`);
  }
});

test("display name is separate from compatibility identifiers", async () => {
  const manifest = JSON.parse(await repoSource("UI/mod.json"));
  const locale = JSON.parse(await repoSource("Locale.json"));
  const webpack = await repoSource("UI/webpack.config.js");

  assert.equal(manifest.id, "C2VM.TrafficLightsEnhancement");
  assert.equal(manifest.displayName, "Traffic Lights Enhancement");
  assert.equal(manifest.deployFolder, "TrafficLightsEnhancementExtended");
  assert.equal(
    locale["Options.SECTION[C2VM.TrafficLightsEnhancement.C2VM.TrafficLightsEnhancement.Mod]"],
    manifest.displayName,
  );
  assert.equal(
    locale["Options.INPUT_MAP[C2VM.TrafficLightsEnhancement.C2VM.TrafficLightsEnhancement.Mod]"],
    manifest.displayName,
  );
  assert.match(webpack, /Mods\\\\\$\{MOD\.deployFolder\}/);
});

test("packaged player-facing sources use the Traffic Lights Enhancement brand", async () => {
  const localeFiles = (await readdir(new URL("../../Locale", import.meta.url)))
    .filter(file => file.endsWith(".json"))
    .map(file => `Locale/${file}`);
  const playerFacingFiles = [
    "Locale.json",
    ...localeFiles,
    "Properties/PublishConfiguration.xml",
    "Systems/Serialization/TLEDataMigrationSystem.cs",
    "Systems/UI/UISystem.UIBIndings.cs",
    "UI/mod.json",
  ];

  for (const file of playerFacingFiles) {
    const contents = await repoSource(file);
    assert.doesNotMatch(
      contents,
      /Traffic Lights Enhancement Extended|TLE Extended/,
      `${file} contains the retired in-game brand`,
    );
  }
});

test("UI does not carry unused TypeScript localization fallback dictionaries", async () => {
  const sourceFiles = await readdir(new URL("../src/mods", import.meta.url), { recursive: true });
  const localizationFallbackFiles = sourceFiles.filter((file) => file === "localisations" || file.startsWith("localisations/") || file.startsWith("localisations\\"));

  assert.deepEqual(localizationFallbackFiles, []);
});

test("backend localization uses Locale.json instead of legacy resource dictionaries", async () => {
  const mod = await repoSource("Mod.cs");
  const resourceFiles = await readdir(new URL("../../Resources", import.meta.url), { recursive: true });
  const utilsFiles = await readdir(new URL("../../Utils", import.meta.url), { recursive: true });
  const legacyResourceFiles = resourceFiles.filter(
    (file) => file === "Localisations" || file.startsWith("Localisations/") || file.startsWith("Localisations\\"));
  const legacyUtils = utilsFiles.filter(
    (file) => file === "LocalisationUtils.cs" || file.endsWith("/LocalisationUtils.cs") || file.endsWith("\\LocalisationUtils.cs"));

  assert.match(mod, /new LocaleHelper\(modName \+ "\.Locale\.json"\)\.GetAvailableLanguages\(\)/);
  assert.deepEqual(legacyResourceFiles, []);
  assert.deepEqual(legacyUtils, []);
});

test("backend localization aliases imported Crowdin locale files to game locale ids", async () => {
  const localeHelper = await repoSource("Extensions/LocaleHelper.cs");

  assert.match(localeHelper, /"pt-PT",\s*\["pt-BR"\]/);
  assert.match(localeHelper, /"zh-CN",\s*\["zh-HANS"\]/);
  assert.match(localeHelper, /"zh-TW",\s*\["zh-HANT",\s*"zh-HK"\]/);
  assert.match(localeHelper, /_locale\[alias\]\s*=\s*dictionary/);
});

test("Crowdin locale dictionaries are embedded as Locale.json siblings", async () => {
  const localeFiles = await readdir(new URL("../../Locale", import.meta.url));
  const baseLocale = JSON.parse(await repoSource("Locale.json"));
  const baseKeys = Object.keys(baseLocale);

  assert.deepEqual([...localeFiles].sort(), [
    "de-DE.json",
    "es-ES.json",
    "fr-FR.json",
    "it-IT.json",
    "ja-JP.json",
    "ko-KR.json",
    "pl-PL.json",
    "pt-PT.json",
    "ru-RU.json",
    "zh-CN.json",
    "zh-TW.json",
  ]);

  for (const localeFile of localeFiles) {
    const locale = JSON.parse(await readFile(new URL(`../../Locale/${localeFile}`, import.meta.url), "utf8"));

    assert.deepEqual(
      Object.keys(locale).sort(),
      [...baseKeys].sort(),
      `${localeFile} must cover all live Locale.json keys`);
  }
});

test("reviewed Crowdin translations preserve traffic-control terms", async () => {
  const readLocale = async (locale) => JSON.parse(await readFile(new URL(`../../Locale/${locale}.json`, import.meta.url), "utf8"));
  const de = await readLocale("de-DE");
  const es = await readLocale("es-ES");
  const fr = await readLocale("fr-FR");
  const it = await readLocale("it-IT");
  const ja = await readLocale("ja-JP");
  const pl = await readLocale("pl-PL");
  const pt = await readLocale("pt-PT");
  const ko = await readLocale("ko-KR");
  const ru = await readLocale("ru-RU");
  const zh = await readLocale("zh-CN");
  const zhTw = await readLocale("zh-TW");

  assert.equal(de["UI.LABEL[C2VM.TrafficLightsEnhancement.TrackWeight]"], "Schienengewichtung");
  assert.equal(de["UI.LABEL[C2VM.TrafficLightsEnhancement.GiveWayToOncomingVehicles]"], "Entgegenkommenden Fahrzeugen Vorfahrt gewähren");
  assert.equal(es["UI.LABEL[C2VM.TrafficLightsEnhancement.GiveWayToOncomingVehicles]"], "Ceder el paso a los vehículos que vienen de frente");
  assert.equal(fr["UI.LABEL[C2VM.TrafficLightsEnhancement.TrackWeight]"], "Poids des rails");
  assert.equal(it["UI.LABEL[C2VM.TrafficLightsEnhancement.TrackWeight]"], "Peso dei binari");
  assert.equal(ja["UI.LABEL[C2VM.TrafficLightsEnhancement.TrackWeight]"], "軌道の重み");
  assert.equal(pl["UI.LABEL[C2VM.TrafficLightsEnhancement.WhenEmpty]"], "Gdy pusto");
  assert.equal(pl["UI.LABEL[C2VM.TrafficLightsEnhancement.WhenNoDemand]"], "Gdy brak zapotrzebowania");
  assert.equal(pt["UI.LABEL[C2VM.TrafficLightsEnhancement.TrackWeight]"], "Peso dos carris");
  assert.equal(ko["Options.OPTION[C2VM.TrafficLightsEnhancement.C2VM.TrafficLightsEnhancement.Mod.Settings.m_DefaultExclusivePedestrian].tooltip"], "사용자 정의 구성이 없는 모든 신호등에 보행자 전용 신호를 추가합니다. 새로 건설된 도로에 적용되며 게임 상태 업데이트 시 기존 도로에도 적용됩니다.");
  assert.equal(ru["UI.LABEL[C2VM.TrafficLightsEnhancement.TrackWeight]"], "Вес рельсового транспорта");
  assert.equal(zh["Tooltip.LABEL[C2VM.TrafficLightsEnhancement.SelectGroupMember]"], "选择一个信号灯组成员");
  assert.equal(zh["UI.LABEL[C2VM.TrafficLightsEnhancement.TrackWeight]"], "轨道权重");
  assert.equal(zhTw["UI.LABEL[C2VM.TrafficLightsEnhancement.TrackWeight]"], "軌道權重");
});

test("localized traffic-control review fixes avoid high-risk mistranslations", async () => {
  const readLocale = async (locale) => JSON.parse(await readFile(new URL(`../../Locale/${locale}.json`, import.meta.url), "utf8"));
  const de = await readLocale("de-DE");
  const es = await readLocale("es-ES");
  const fr = await readLocale("fr-FR");
  const it = await readLocale("it-IT");
  const ja = await readLocale("ja-JP");
  const ko = await readLocale("ko-KR");
  const pl = await readLocale("pl-PL");
  const pt = await readLocale("pt-PT");
  const ru = await readLocale("ru-RU");
  const zh = await readLocale("zh-CN");
  const zhTw = await readLocale("zh-TW");
  const diagnosticsOptionKey =
    "Options.OPTION[C2VM.TrafficLightsEnhancement.C2VM.TrafficLightsEnhancement.Mod.Settings.m_ShowTransitSignalPriorityDiagnostics]";
  const diagnosticsDescriptionKey =
    "Options.OPTION_DESCRIPTION[C2VM.TrafficLightsEnhancement.C2VM.TrafficLightsEnhancement.Mod.Settings.m_ShowTransitSignalPriorityDiagnostics]";
  const diagnosticsTooltipKey =
    "Options.OPTION[C2VM.TrafficLightsEnhancement.C2VM.TrafficLightsEnhancement.Mod.Settings.m_ShowTransitSignalPriorityDiagnostics].tooltip";
  const kerbsideTurnOptionKey =
    "Options.OPTION[C2VM.TrafficLightsEnhancement.C2VM.TrafficLightsEnhancement.Mod.Settings.m_DefaultAlwaysGreenKerbsideTurn]";
  const kerbsideTurnDescriptionKey =
    "Options.OPTION_DESCRIPTION[C2VM.TrafficLightsEnhancement.C2VM.TrafficLightsEnhancement.Mod.Settings.m_DefaultAlwaysGreenKerbsideTurn]";
  const kerbsideTurnTooltipKey =
    "Options.OPTION[C2VM.TrafficLightsEnhancement.C2VM.TrafficLightsEnhancement.Mod.Settings.m_DefaultAlwaysGreenKerbsideTurn].tooltip";
  const label = (name) => `UI.LABEL[C2VM.TrafficLightsEnhancement.${name}]`;
  const tooltip = (name) => `Tooltip.LABEL[C2VM.TrafficLightsEnhancement.${name}]`;
  const warning = (setting) => `Options.WARNING[C2VM.TrafficLightsEnhancement.C2VM.TrafficLightsEnhancement.Mod.Settings.${setting}]`;
  const action = (name) => `Common.ACTION[C2VM.TrafficLightsEnhancement.C2VM.TrafficLightsEnhancement.Mod/${name}]`;

  assert.equal(fr[tooltip("TrafficSignYield")], "Céder le passage");
  assert.equal(it[tooltip("TrafficSignYield")], "Dare precedenza");
  assert.equal(ja[tooltip("TrafficSignYield")], "譲れ");
  assert.equal(pt[tooltip("TrafficSignYield")], "Cedência de passagem");
  assert.equal(ru[tooltip("TrafficSignYield")], "Уступить дорогу");
  assert.equal(zhTw[tooltip("TrafficSignYield")], "讓行");

  assert.equal(fr[kerbsideTurnOptionKey], "Virage côté trottoir au rouge par défaut");
  assert.equal(it[kerbsideTurnOptionKey], "Svolta lato marciapiede con il rosso predefinita");
  assert.equal(ja[kerbsideTurnOptionKey], "赤信号での路肩側右左折を既定にする");
  assert.equal(pt[kerbsideTurnOptionKey], "Viragem junto ao passeio no vermelho por predefinição");
  assert.equal(zhTw[kerbsideTurnOptionKey], "預設允許靠路緣紅燈轉向");
  for (const locale of [fr, it, ja, pt, zhTw]) {
    assert.equal(locale[kerbsideTurnDescriptionKey], locale[kerbsideTurnTooltipKey]);
  }
  assert.equal(fr[label("AllowTurningOnRed")], "Autoriser le virage au rouge");
  assert.equal(it[label("AllowTurningOnRed")], "Consenti svolta con il rosso");
  assert.equal(ja[label("AllowTurningOnRed")], "赤信号での右左折を許可");
  assert.equal(pt[label("AllowTurningOnRed")], "Permitir viragem no vermelho");
  assert.equal(zhTw[label("AllowTurningOnRed")], "允許紅燈轉向");
  assert.equal(fr[label("TSPDiagnosticsOptionTurningOnRed")], "Option virage au rouge");
  assert.equal(it[label("TSPDiagnosticsOptionTurningOnRed")], "Opzione svolta con il rosso");
  assert.equal(ja[label("TSPDiagnosticsOptionTurningOnRed")], "赤信号右左折オプション");
  assert.equal(pt[label("TSPDiagnosticsOptionTurningOnRed")], "Opção de viragem no vermelho");
  assert.equal(zhTw[label("TSPDiagnosticsOptionTurningOnRed")], "紅燈轉向選項");

  assert.equal(fr[label("TSPDiagnosticsBusHitCount")], "Correspondances bus");
  assert.equal(it[label("TSPDiagnosticsBusHitCount")], "Corrispondenze bus");
  assert.equal(pt[label("TSPDiagnosticsBusHitCount")], "Correspondências de autocarro");
  assert.equal(zhTw[label("TSPDiagnosticsBusHitCount")], "公車符合數");
  assert.equal(fr[label("BalancedForPeds")], "Équilibré pour les piétons");
  assert.equal(ja[label("BalancedForPeds")], "歩行者向けに調整済み");
  assert.equal(pt[label("BalancedForPeds")], "Equilibrado para peões");
  assert.equal(ru[label("BalancedForPeds")], "Сбалансировано для пешеходов");
  assert.equal(zhTw[label("BalancedForPeds")], "已為行人平衡");

  assert.equal(de[diagnosticsOptionKey], "Diagnose anzeigen");
  assert.equal(es[diagnosticsOptionKey], "Mostrar diagnóstico");
  assert.equal(ko[diagnosticsOptionKey], "진단 표시");
  assert.equal(pl[diagnosticsOptionKey], "Pokaż diagnostykę");
  assert.equal(zh[diagnosticsOptionKey], "显示诊断");
  for (const locale of [de, es, ko, pl, zh]) {
    assert.equal(locale[diagnosticsDescriptionKey], locale[diagnosticsTooltipKey]);
    assert.doesNotMatch(locale[diagnosticsDescriptionKey], /active request|solicitud activa|활성 요청|aktywne żądanie|当前请求/i);
  }

  assert.equal(fr[label("ControlledByLeader")], "Contrôlé par le leader: les phases sont synchronisées en lockstep.");
  assert.equal(it[label("ControlledByLeader")], "Controllato dal leader: le fasi sono sincronizzate in lockstep.");
  assert.equal(zhTw[label("ControlledByLeader")], "由領導者控制：階段以 lockstep 同步。");
  assert.equal(fr[label("Lockstep")], "Mode lockstep");
  assert.equal(it[label("Lockstep")], "Modalità Lockstep");
  assert.equal(ru[label("Lockstep")], "Режим lockstep");
  assert.equal(zhTw[label("Lockstep")], "鎖步");

  assert.equal(fr[label("TSPDiagnosticsApproachOwner")], "Propriétaire de l'approche");
  assert.equal(it[label("TSPDiagnosticsApproachOwner")], "Proprietario dell'approccio");
  assert.equal(ja[label("TSPDiagnosticsApproachOwner")], "進入側の所有者");
  assert.equal(ru[label("TSPDiagnosticsApproachOwner")], "Владелец подхода");
  assert.equal(zhTw[label("TSPDiagnosticsApproachOwner")], "進入側擁有者");

  assert.equal(ru[label("Disabled")], "Отключено");
  assert.equal(ru[label("TSPDiagnosticsUpstreamOwner")], "Владелец вышележащего участка");
  assert.equal(ru[label("TSPDiagnosticsBusPriorityMode")], "Режим приоритета автобуса");
  assert.equal(ru[label("Reset")], "Сбросить");
  assert.equal(ru[label("Offset")], "Смещение");
  assert.equal(zhTw[label("Disabled")], "已停用");
  assert.equal(zhTw[diagnosticsDescriptionKey], zhTw[diagnosticsTooltipKey]);
  assert.equal(zhTw[label("TSPDiagnosticsBusControl")], "公車控制");
  assert.equal(zhTw[label("TSPDiagnosticsStrength")], "強度");
  assert.equal(zhTw[label("TSPDiagnosticsBaseGroup")], "基準群組");
  assert.equal(zhTw[label("TSPDiagnosticsCandidates")], "候選項目");
  assert.equal(zhTw[label("TSPDiagnosticsJunctionTopology")], "路口拓撲");
  assert.equal(zhTw[label("TransitSignalPriority")], "交通號誌優先");
  assert.equal(zhTw[label("TSPDiagnosticsOptionExclusivePedestrian")], "專屬行人相選項");
  assert.equal(zhTw[label("TSPDiagnosticsApproachRole")], "引道角色");
  assert.equal(zhTw[label("TSPDiagnosticsProbeApproach")], "引道探測");
  assert.equal(zhTw[label("TSPDiagnosticsCurveApproach")], "引道曲線");
  assert.equal(fr[label("Options")], "Paramètres");
  assert.equal(fr[label("VeryShortSkipsEmpty")], "Très court, ignore les phases vides");
  assert.equal(fr[label("TSPDiagnosticsSiblingSamples")], "Échantillons de voies associées");
  assert.equal(it[label("TSPDiagnosticsSiblingSamples")], "Campioni di corsie correlate");
  assert.equal(ja[label("TSPDiagnosticsSiblingSamples")], "関連レーンのサンプル");
  assert.equal(zhTw[label("TSPDiagnosticsSiblingSamples")], "相關車道樣本");
  assert.equal(it[tooltip("TrafficSignStop")], "Fermarsi");
  assert.equal(it[label("BackToGroup")], "Torna al gruppo");
  assert.equal(ja[label("TSPDiagnosticsPendingPedestrianFairness")], "フェーズが必要な歩行者");
  assert.equal(ja[label("TSPDiagnosticsTargetGroup")], "対象グループ");
  assert.equal(ja[label("TSPDiagnosticsPedestrianDuration")], "歩行者時間オプション");
  assert.equal(pt[label("TSPDiagnosticsEvents")], "Eventos recentes de TSP");
  assert.equal(de[tooltip("Auto")], "Gleicht automatisch Verkehrsfluss und Wartezeit aus, um zu entscheiden, wann die Phase gewechselt wird.");
  assert.equal(de[tooltip("WhenNoDemand")], "Wechselt die Phase nur, wenn auf anderen Spuren Verkehr wartet. Vermeidet unnötige Wechsel.");
  assert.equal(es[kerbsideTurnDescriptionKey], es[kerbsideTurnTooltipKey]);
  assert.equal(ko[label("TurnsSinceLastRun")], "마지막 실행 이후 주기 수");
  assert.equal(pl[action("KeyboardBindingMainPanelToggle")], "Przełącz panel główny");
  assert.equal(pl[warning("m_ForceNodeUpdate")], "To wymusi aktualizację wszystkich skrzyżowań z sygnalizacją świetlną. Ustawienia domyślne zostaną zastosowane do skrzyżowań bez konfiguracji niestandardowej.");
  assert.doesNotMatch(ru[diagnosticsDescriptionKey], /соединительную панель|шлюз/);
  assert.equal(ru[diagnosticsDescriptionKey], ru[diagnosticsTooltipKey]);
  assert.match(zh[label("CanaryBuildWarning")], /测试/);
  assert.match(zh[label("CanaryBuildWarning")], /破坏游戏|破坏存档/);
  assert.doesNotMatch(zh[label("CanaryBuildWarning")], /备份|未完成/);
  assert.equal(zh[label("LaneDirectionTool")], "车道方向工具");
  assert.match(zh[label("LdtMigrationNotice")], /^车道方向工具/);
  assert.match(zh[label("LdtRetirementNotice")], /^车道方向工具/);
});

test("machine-assisted localization metadata tracks only live locale keys", async () => {
  const metadata = JSON.parse(await repoSource("../docs/localization-ai-review.json"));
  const baseLocale = JSON.parse(await repoSource("Locale.json"));

  assert.equal(metadata.reviewStatus, "needs-native-speaker-review");

  for (const [localeId, localeMetadata] of Object.entries(metadata.locales)) {
    const locale = JSON.parse(await readFile(new URL(`../../Locale/${localeId}.json`, import.meta.url), "utf8"));

    assert.match(localeMetadata.source, /^upstream-crowdin-plus-ai-/);
    assert.equal(localeMetadata.aiTranslatedKeys.length, localeMetadata.aiTranslatedKeyCount);

    for (const key of localeMetadata.aiTranslatedKeys) {
      assert.ok(Object.hasOwn(baseLocale, key), `${localeId} metadata references unknown key ${key}`);
      assert.ok(Object.hasOwn(locale, key), `${localeId} locale is missing metadata key ${key}`);
      assert.notEqual(locale[key], baseLocale[key], `${localeId} AI-filled key still matches English fallback: ${key}`);
    }
  }
});

test("custom phase vehicle weights expose bicycle weight control", async () => {
  const subPanel = await source("src/mods/components/custom-phase-tool/main-panel/sub-panel.tsx");
  const locale = JSON.parse(await repoSource("Locale.json"));

  assert.match(subPanel, /keyName="BicycleWeight"/);
  assert.match(subPanel, /label="BicycleWeight"/);
  assert.match(subPanel, /value=\{data\.bicycleWeight\}/);
  assert.match(subPanel, /Tooltip\.LABEL\[C2VM\.TrafficLightsEnhancement\.BicycleWeight\]/);
  assert.equal(locale["UI.LABEL[C2VM.TrafficLightsEnhancement.BicycleWeight]"], "Bicycle weight");
  assert.equal(
    typeof locale["Tooltip.LABEL[C2VM.TrafficLightsEnhancement.BicycleWeight]"],
    "string");
});

test("traffic group and custom phase chrome text is localized", async () => {
  const locale = JSON.parse(await repoSource("Locale.json"));
  const trafficGroups = await source("src/mods/components/traffic-groups/main-panel/IndexComponent/index.tsx");
  const groupItem = await source("src/mods/components/traffic-groups/main-panel/GroupItemComponent/group-item.tsx");
  const migrationModal = await source("src/mods/components/migration-issues/migration-issues-modal.tsx");
  const customPhasePanel = await source("src/mods/components/custom-phase-tool/main-panel/sub-panel.tsx");
  const presetManager = await source("src/mods/components/common/preset-manager/preset-manager.tsx");

  const expectedKeys = [
    "UI.LABEL[C2VM.TrafficLightsEnhancement.AddMember]",
    "UI.LABEL[C2VM.TrafficLightsEnhancement.RemoveFromGroup]",
    "Tooltip.LABEL[C2VM.TrafficLightsEnhancement.RemoveFromGroup]",
    "UI.LABEL[C2VM.TrafficLightsEnhancement.SelectMemberInWorld]",
    "Tooltip.LABEL[C2VM.TrafficLightsEnhancement.SelectMemberInWorld]",
    "UI.LABEL[C2VM.TrafficLightsEnhancement.CopyPhasesToAllMembers]",
    "Tooltip.LABEL[C2VM.TrafficLightsEnhancement.TrafficSignGo]",
    "Tooltip.LABEL[C2VM.TrafficLightsEnhancement.TrafficSignYield]",
    "Tooltip.LABEL[C2VM.TrafficLightsEnhancement.TrafficSignStop]",
    "UI.LABEL[C2VM.TrafficLightsEnhancement.NewGroup]",
    "UI.LABEL[C2VM.TrafficLightsEnhancement.UnnamedGroup]",
    "UI.LABEL[C2VM.TrafficLightsEnhancement.GroupName]",
    "UI.LABEL[C2VM.TrafficLightsEnhancement.Edge]",
    "UI.LABEL[C2VM.TrafficLightsEnhancement.GroupInfo]",
    "UI.LABEL[C2VM.TrafficLightsEnhancement.GroupMembers]",
    "Tooltip.LABEL[C2VM.TrafficLightsEnhancement.GroupMemberFoldout]",
    "UI.LABEL[C2VM.TrafficLightsEnhancement.GroupSettings]",
    "UI.LABEL[C2VM.TrafficLightsEnhancement.EnableCoordination]",
    "UI.LABEL[C2VM.TrafficLightsEnhancement.EnableGreenWave]",
    "UI.LABEL[C2VM.TrafficLightsEnhancement.NoMembersInGroup]",
    "UI.LABEL[C2VM.TrafficLightsEnhancement.NoEdgeDataAvailable]",
    "UI.LABEL[C2VM.TrafficLightsEnhancement.DataMigrationIssues]",
    "UI.LABEL[C2VM.TrafficLightsEnhancement.AffectedIntersections]",
    "UI.LABEL[C2VM.TrafficLightsEnhancement.MigrationIssuesDescription]",
    "Tooltip.LABEL[C2VM.TrafficLightsEnhancement.NavigateToIntersection]",
    "Tooltip.LABEL[C2VM.TrafficLightsEnhancement.RemoveFromList]",
    "UI.LABEL[C2VM.TrafficLightsEnhancement.DismissAll]",
    "UI.LABEL[C2VM.TrafficLightsEnhancement.BackToGroup]",
    "UI.LABEL[C2VM.TrafficLightsEnhancement.StartDelay]",
    "UI.LABEL[C2VM.TrafficLightsEnhancement.EndEarly]",
    "UI.LABEL[C2VM.TrafficLightsEnhancement.QuickCycle]",
    "UI.LABEL[C2VM.TrafficLightsEnhancement.HeavyTraffic]",
    "UI.LABEL[C2VM.TrafficLightsEnhancement.PedestrianFriendly]",
    "UI.LABEL[C2VM.TrafficLightsEnhancement.RailPriority]",
    "UI.LABEL[C2VM.TrafficLightsEnhancement.NightMode]",
  ];

  for (const key of expectedKeys) {
    assert.equal(typeof locale[key], "string", `${key} should be present in Locale.json`);
    assert.notEqual(locale[key].trim(), "", `${key} should not be empty`);
  }

  assert.match(trafficGroups, /label="RemoveFromGroup"/);
  assert.match(trafficGroups, /Tooltip\.LABEL\[C2VM\.TrafficLightsEnhancement\.RemoveFromGroup\]/);
  assert.match(trafficGroups, /Tooltip\.LABEL\[C2VM\.TrafficLightsEnhancement\.TrafficSignGo\]/);
  assert.match(trafficGroups, /Tooltip\.LABEL\[C2VM\.TrafficLightsEnhancement\.TrafficSignYield\]/);
  assert.match(trafficGroups, /Tooltip\.LABEL\[C2VM\.TrafficLightsEnhancement\.TrafficSignStop\]/);
  assert.match(trafficGroups, /UI\.LABEL\[C2VM\.TrafficLightsEnhancement\.Edge\]/);
  assert.match(groupItem, /UI\.LABEL\[C2VM\.TrafficLightsEnhancement\.UnnamedGroup\]/);
  assert.match(migrationModal, /UI\.LABEL\[C2VM\.TrafficLightsEnhancement\.DataMigrationIssues\]/);
  assert.match(customPhasePanel, /label:\s*"StartDelay"/);
  assert.match(presetManager, /TrafficLightsEnhancement\.\$\{template\.name\}/);

  assert.doesNotMatch(trafficGroups, /label="Remove from group"|label="Add member"|label="Select member in world"|label="Copy phases to all members"/);
  assert.doesNotMatch(trafficGroups, /●Go|●Yield|●Stop|>\s*Edge\s*\{/);
  assert.doesNotMatch(migrationModal, />Data migration issues<|>Affected intersections<|>Dismiss all</);
  assert.doesNotMatch(customPhasePanel, /"Start delay"|"End early"|"Quick cycle"|"Heavy traffic"|"Pedestrian friendly"|"Rail priority"|"Night mode"/);
});

test("lockstep followers keep their custom phase editor available", async () => {
  const trafficGroups = await source("src/mods/components/traffic-groups/main-panel/IndexComponent/index.tsx");

  assert.match(trafficGroups, /const isFollowerInLockstep = isLockstep && !member\.isLeader;/);
  assert.match(trafficGroups, /<MemberPatternSelector[\s\S]*memberIndex=\{member\.index\}[\s\S]*memberVersion=\{member\.version\}/);
  assert.doesNotMatch(trafficGroups, /\{isFollowerInLockstep \? \(/);
});

test("literal UI localization calls reference live locale keys", async () => {
  const locale = JSON.parse(await repoSource("Locale.json"));
  const missingKeys = [];

  for (const file of await getUiSourceFiles()) {
    const text = await source(file);

    for (const key of getLiteralLocalizationKeys(text)) {
      if (!Object.prototype.hasOwnProperty.call(locale, key)) {
        missingKeys.push(`${file}: ${key}`);
      }
    }
  }

  assert.deepEqual(missingKeys, []);
});

test("literal UI localization parser accepts fallback arguments", () => {
  const text = 'translate("UI.LABEL[C2VM.TrafficLightsEnhancement.TrafficSignal]", "Traffic signal")';
  const keys = getLiteralLocalizationKeys(text);

  assert.deepEqual(keys, ["UI.LABEL[C2VM.TrafficLightsEnhancement.TrafficSignal]"]);

  const typoText = 'translate("UI.Label[C2VM.TrafficLightsEnhancement.TrafficSignal]")';
  assert.deepEqual(getLiteralLocalizationKeys(typoText), ["UI.Label[C2VM.TrafficLightsEnhancement.TrafficSignal]"]);

  const transformText = 'style={{transform: "translate(" + left + "px, " + top + "px)"}}';
  assert.deepEqual([...transformText.matchAll(literalTranslatePattern)], []);
  assert.deepEqual(getLiteralLocalizationKeys(transformText), []);
});

test("traffic group tooltips do not use placeholder English", async () => {
  const trafficGroups = await source("src/mods/components/traffic-groups/main-panel/IndexComponent/index.tsx");

  assert.match(trafficGroups, /title=\{signalTitle\}/);
  assert.doesNotMatch(trafficGroups, /click to cycle/i);
});

test("backend toggle removes transit signal priority settings when all sources are disabled", async () => {
  const uiBindings = await repoSource("Systems/UI/UISystem.UIBIndings.cs");
  const toggleStart = uiBindings.indexOf("protected void ToggleTransitSignalPriorityForTrams(bool enabled)");

  assert.notEqual(toggleStart, -1);
  const toggleEnd = uiBindings.indexOf("protected void CallMainPanelUpdatePosition", toggleStart);
  const toggleSource = toggleEnd === -1 ? uiBindings.slice(toggleStart) : uiBindings.slice(toggleStart, toggleEnd);

  assert.match(toggleSource, /ToggleTransitSignalPrioritySource\(enabled,\s*allowTrackRequests:\s*true\)/);
  assert.match(toggleSource, /ToggleTransitSignalPrioritySource\(enabled,\s*allowTrackRequests:\s*false\)/);
  assert.match(toggleSource, /settings\.m_Enabled\s*=\s*settings\.m_AllowTrackRequests\s*\|\|\s*settings\.m_AllowPublicCarRequests/);
  assert.match(toggleSource, /if\s*\(!settings\.m_Enabled\)/);
  assert.match(toggleSource, /EntityManager\.RemoveComponent<TransitSignalPrioritySettings>\(m_SelectedEntity\)/);
});

test("tool removal clears transit signal priority runtime components", async () => {
  const toolSystem = await repoSource("Systems/Tool/ToolSystem.cs");
  const helperStart = toolSystem.indexOf("private void RemoveTransitSignalPriorityComponents(Entity entity)");

  assert.notEqual(helperStart, -1);
  const helperEnd = toolSystem.indexOf("private void", helperStart + 1);
  const helperSource = helperEnd === -1 ? toolSystem.slice(helperStart) : toolSystem.slice(helperStart, helperEnd);

  assert.match(helperSource, /RemoveComponent<TransitSignalPrioritySettings>/);
  assert.match(helperSource, /RemoveComponent<TransitSignalPriorityRequest>/);
  assert.match(helperSource, /RemoveComponent<TransitSignalPriorityRuntimeDebugInfo>/);
  assert.match(helperSource, /RemoveComponent<TransitSignalPriorityBusApproachDebugInfo>/);
  assert.match(helperSource, /RemoveComponent<TransitSignalPriorityDecisionTrace>/);
  assert.match(helperSource, /RemoveComponent<TransitSignalPriorityPedestrianFairnessState>/);
  assert.match(helperSource, /RemoveComponent<TransitSignalPriorityVehicleFairnessState>/);

  const removalStart = toolSystem.indexOf("EntityManager.RemoveComponent<CustomTrafficLights>(m_RaycastResult)");
  const removalEnd = toolSystem.indexOf("EntityManager.AddComponentData(m_RaycastResult", removalStart);
  const removalSource = removalEnd === -1 ? toolSystem.slice(removalStart) : toolSystem.slice(removalStart, removalEnd);

  assert.match(removalSource, /RemoveTransitSignalPriorityComponents\(m_RaycastResult\)/);
});

test("transit signal priority settings preserve bus priority without persisting group propagation", async () => {
  const settings = await repoSource("Components/TransitSignalPrioritySettings.cs");
  const normalizeStart = settings.indexOf("public void Normalize()");
  const serializeStart = settings.indexOf("public void Serialize");
  const deserializeStart = settings.indexOf("public void Deserialize");

  assert.notEqual(normalizeStart, -1);
  assert.notEqual(serializeStart, -1);
  assert.notEqual(deserializeStart, -1);

  const normalizeSource = settings.slice(normalizeStart, serializeStart);
  const serializeSource = settings.slice(serializeStart, deserializeStart);
  const deserializeSource = settings.slice(deserializeStart);

  assert.doesNotMatch(normalizeSource, /m_AllowPublicCarRequests\s*=/);
  assert.match(serializeSource, /writer\.Write\(2\)/);
  assert.match(serializeSource, /writer\.Write\(m_AllowPublicCarRequests\)/);
  assert.doesNotMatch(serializeSource, /m_AllowGroupPropagation/);
  assert.match(deserializeSource, /if\s*\(version\s*==\s*1\)/);
  assert.doesNotMatch(deserializeSource, /reader\.Read\(out m_AllowGroupPropagation\)/);
});

test("backend exposes bus approach index details", async () => {
  const patchedSystem = await repoSource("Systems/TrafficLightSystems/Simulation/PatchedTrafficLightSystem.cs");
  const extraTypeHandle = await repoSource("Systems/TrafficLightSystems/Simulation/ExtraTypeHandle.cs");
  const busIndex = await repoSource("Systems/TrafficLightSystems/Simulation/BusApproachIndex.cs");
  const runtime = await repoSource("Systems/TrafficLightSystems/Simulation/TransitSignalPriorityRuntime.cs");
  const components = await repoSource("Components/TransitSignalPriorityBusApproachDebugInfo.cs");
  const uiBindings = await repoSource("Systems/UI/UISystem.UIBIndings.cs");
  const locale = JSON.parse(await repoSource("Locale.json"));
  const busBuildCondition = patchedSystem.match(/var busApproachIndex = ([\s\S]*?)\? BusApproachIndex\.Build/);

  assert.match(patchedSystem, /m_BusTransitQuery/);
  assert.match(patchedSystem, /ComponentType\.ReadOnly<PassengerTransport>\(\)/);
  assert.match(patchedSystem, /BusApproachIndex\.Build/);
  assert.match(patchedSystem, /m_ShowTransitSignalPriorityDiagnostics/);
  assert.ok(busBuildCondition, "bus approach index should have an explicit build condition");
  assert.match(busBuildCondition[1], /shouldBuildBusApproachIndex/);
  assert.doesNotMatch(busBuildCondition[1], /shouldBuildTramApproachIndex/);
  assert.match(patchedSystem, /m_BusApproachIndex\s*=/);
  assert.match(patchedSystem, /m_BusApproachIndexLaneCount\s*=/);
  const busDebugStart = patchedSystem.indexOf("if (m_TransitSignalPriorityDiagnosticsEnabled");
  const busDebugEnd = patchedSystem.indexOf("if (hasActiveBusApproachDebugInfo)", busDebugStart);
  const busDebugGate = patchedSystem.slice(busDebugStart, busDebugEnd);

  assert.notEqual(busDebugStart, -1);
  assert.notEqual(busDebugEnd, -1);
  assert.match(busDebugGate, /BuildBusApproachDebugInfo/);
  assert.doesNotMatch(busDebugGate, /TransitSignalPrioritySettingsLookup/);
  assert.doesNotMatch(busDebugGate, /m_Enabled/);
  assert.match(extraTypeHandle, /CarCurrentLane/);
  assert.match(extraTypeHandle, /CarNavigation/);
  assert.match(extraTypeHandle, /CarNavigationLane/);
  assert.doesNotMatch(extraTypeHandle, /m_PassengerTransport/);
  assert.match(extraTypeHandle, /PublicTransportVehicleData/);
  assert.match(busIndex, /TransportType\.Bus/);
  assert.match(busIndex, /PublicOnly/);
  assert.match(busIndex, /m_ChangeLane/);
  assert.match(runtime, /BuildBusApproachDebugInfo/);
  assert.match(uiBindings, /if\s*\(\s*hasBusApproachDebug\s*&&\s*busApproachDebug\.m_BusHitCount\s*>\s*0\s*\)/);
  const summaryStart = uiBindings.indexOf("private string GetTspDiagnosticsSummaryValue");
  const summaryEnd = uiBindings.indexOf("private ArrayList GetTspDiagnosticsEvents", summaryStart);
  const summarySource = uiBindings.slice(summaryStart, summaryEnd);
  assert.notEqual(summaryStart, -1);
  assert.notEqual(summaryEnd, -1);
  assert.ok(summarySource.indexOf("if (!settings.m_Enabled)") < summarySource.indexOf("if (hasBusApproachDebug && busApproachDebug.m_BusHitCount > 0)"));
  assert.match(components, /TransitSignalPriorityBusProbeResult/);
  assert.match(uiBindings, /TransitSignalPriorityBusApproachDebugInfo/);
  assert.doesNotMatch(summarySource, /No tram request/);
  assert.match(summarySource, /No active request/);
  assert.match(uiBindings, /TSPDiagnosticsBusIndexLanes/);
  assert.match(uiBindings, /TSPDiagnosticsBusLaneType/);
  assert.match(uiBindings, /TSPDiagnosticsBusLaneChange/);
  assert.match(uiBindings, /vehicleLaneFlags\s*=\s*busApproachDebug\.m_BusVehicleLaneFlags\.ToString\(\)/);
  assert.equal(locale["UI.LABEL[C2VM.TrafficLightsEnhancement.TSPDiagnosticsBusIndexLanes]"], "Indexed bus lanes");
  assert.equal(locale["UI.LABEL[C2VM.TrafficLightsEnhancement.TSPDiagnosticsBusLaneType]"], "Bus lane type");
});

test("runtime can build public-car requests from bus approach samples", async () => {
  const runtime = await repoSource("Systems/TrafficLightSystems/Simulation/TransitSignalPriorityRuntime.cs");
  const helperStart = runtime.indexOf("public static bool TryBuildBusApproachRequestFromSample");
  const helperEnd = runtime.indexOf("TryResolveActiveLocalRequest(", helperStart);
  const helperSource = runtime.slice(helperStart, helperEnd);
  const requestStart = runtime.indexOf("private static bool TryBuildBusApproachRequestForLane");
  const requestEnd = runtime.indexOf("private static bool TryBuildPetitionerRequestForLane", requestStart);
  const requestSource = runtime.slice(requestStart, requestEnd);

  assert.match(runtime, /TryBuildBusApproachRequestFromSample/);
  assert.match(runtime, /TryBuildBusApproachRequestForLane/);
  assert.notEqual(helperStart, -1);
  assert.notEqual(helperEnd, -1);
  assert.notEqual(requestStart, -1);
  assert.notEqual(requestEnd, -1);
  assert.match(requestSource, /TryBuildBusApproachRequestFromSample/);
  assert.match(helperSource, /isPublicCarLane:\s*true/);
  assert.match(helperSource, /TspSource\.PublicCar/);
  assert.match(helperSource, /BusPrioritySuppressionPolicy\.EvaluateStopSuppression/);
  assert.match(helperSource, /BusStopRelation\.Unknown/);
  assert.match(helperSource, /HasAmbiguousBusLaneChange\(sample\)/);
});

test("bus diagnostics include request and suppression decisions", async () => {
  const components = await repoSource("Components/TransitSignalPriorityBusApproachDebugInfo.cs");
  const runtime = await repoSource("Systems/TrafficLightSystems/Simulation/TransitSignalPriorityRuntime.cs");
  const uiBindings = await repoSource("Systems/UI/UISystem.UIBIndings.cs");
  const locale = JSON.parse(await repoSource("Locale.json"));

  assert.match(components, /TransitSignalPriorityBusDecision/);
  assert.match(components, /RequestEmitted/);
  assert.match(components, /SuppressedBoarding/);
  assert.match(components, /SuppressedNearSideStop/);
  assert.match(components, /SuppressedUnknownStopRelation/);
  assert.match(components, /SuppressedAmbiguousLaneChange/);
  assert.doesNotMatch(components, /SuppressedAggressivePreemption/);
  assert.match(runtime, /m_BusDecision\s*=\s*TransitSignalPriorityBusDecision\.RequestEmitted/);
  assert.match(uiBindings, /TSPDiagnosticsBusDecision/);
  assert.match(uiBindings, /GetBusDecisionName/);
  assert.match(uiBindings, /SuppressedNearSideStop => "Suppressed: near-side stop"/);
  assert.doesNotMatch(uiBindings, /SuppressedAggressivePreemption/);
  assert.ok(locale["UI.LABEL[C2VM.TrafficLightsEnhancement.TSPDiagnosticsBusDecision]"]);
  assert.ok(locale["UI.LABEL[C2VM.TrafficLightsEnhancement.TSPDiagnosticsBusDecisionSuppressedNearSideStop]"]);
});

test("bus no-progress diagnostics expose the measured interval and localized suppression reason", async () => {
  const uiBindings = await repoSource("Systems/UI/UISystem.UIBIndings.cs");
  const panel = await source("src/mods/components/main-panel/content.tsx");
  const locale = JSON.parse(await repoSource("Locale.json"));

  assert.match(uiBindings, /SuppressedNoProgress => "Suppressed: no progress"/);
  assert.match(uiBindings, /valueLabel = busApproachDebug\.m_BusDecision == TransitSignalPriorityBusDecision\.SuppressedNoProgress/);
  assert.match(uiBindings, /label = "TSPDiagnosticsBusNoProgressTicks", value = busApproachDebug\.m_BusNoProgressTicks/);
  assert.match(uiBindings, /noProgressTicks = busApproachDebug\.m_BusNoProgressTicks/);
  assert.match(uiBindings, /noProgressSuppressed = busApproachDebug\.m_BusNoProgressSuppressed/);
  assert.match(panel, /row\.valueLabel\s*\?\s*translate\(`UI\.LABEL\[C2VM\.TrafficLightsEnhancement\.\$\{row\.valueLabel\}\]`\)\s*\?\?\s*row\.value\s*:\s*row\.value/);
  assert.equal(locale["UI.LABEL[C2VM.TrafficLightsEnhancement.TSPDiagnosticsBusDecisionSuppressedNoProgress]"], "Suppressed: no progress");
  assert.equal(locale["UI.LABEL[C2VM.TrafficLightsEnhancement.TSPDiagnosticsBusNoProgressTicks]"], "Bus no-progress ticks");
});

test("bus priority builds bus approach index without requiring diagnostics", async () => {
  const patchedSystem = await repoSource("Systems/TrafficLightSystems/Simulation/PatchedTrafficLightSystem.cs");

  assert.match(patchedSystem, /bool\s+shouldBuildBusApproachIndex\s*=/);
  assert.match(patchedSystem, /showTransitSignalPriorityDiagnostics\s*\|\|\s*HasApproachIndexEligibleTransitSignalPrioritySettings\(requirePublicCarRequests:\s*true\)/);
  assert.match(patchedSystem, /shouldBuildBusApproachIndex\s*\?\s*BusApproachIndex\.Build/);
});

test("bus diagnostics reuse runtime bus scan when priority already scanned the junction", async () => {
  const runtime = await repoSource("Systems/TrafficLightSystems/Simulation/TransitSignalPriorityRuntime.cs");
  const patchedSystem = await repoSource("Systems/TrafficLightSystems/Simulation/PatchedTrafficLightSystem.cs");
  const diagnosticsStart = patchedSystem.indexOf("if (m_TransitSignalPriorityDiagnosticsEnabled)");
  const diagnosticsEnd = patchedSystem.indexOf("if (hasActiveBusApproachDebugInfo)", diagnosticsStart);
  const diagnosticsSource = patchedSystem.slice(diagnosticsStart, diagnosticsEnd);

  assert.match(runtime, /out TransitSignalPriorityBusApproachDebugInfo reusableBusApproachDebugInfo/);
  assert.match(runtime, /out bool hasReusableBusApproachDebugInfo/);
  assert.match(patchedSystem, /m_TransitSignalPriorityDiagnosticsEnabled,\s*out var tspRequest/);
  assert.match(diagnosticsSource, /hasReusableBusApproachDebugInfo\s*\?\s*reusableBusApproachDebugInfo/);
  assert.match(diagnosticsSource, /:\s*TspRuntime\.BuildBusApproachDebugInfo/);
});

test("bus priority can select target group at normal transition without aggressive preemption", async () => {
  const patchedSystem = await repoSource("Systems/TrafficLightSystems/Simulation/PatchedTrafficLightSystem.cs");
  const getNextStart = patchedSystem.indexOf("private int GetNextSignalGroup(");
  const getNextEnd = patchedSystem.indexOf("private static bool IsExclusivePedestrianEnabled", getNextStart);
  const getNextSource = patchedSystem.slice(getNextStart, getNextEnd);

  assert.notEqual(getNextStart, -1);
  assert.notEqual(getNextEnd, -1);
  assert.match(getNextSource, /ShouldApplyTargetGroupSelection/);
  assert.match(getNextSource, /ApplySignalGroupOverride/);
  assert.doesNotMatch(getNextSource, /if\s*\(\s*!hasTspRequest\s*\|\|\s*!TspRuntime\.ShouldAggressivelyPreemptToTargetGroup/);
});

test("custom phase text fields preserve numeric regex escapes", async () => {
  const panel = await source("src/mods/components/custom-phase-tool/main-panel/sub-panel.tsx");
  const regexLiterals = [...panel.matchAll(/textFieldRegExp="([^"]+)"/g)].map((match) => match[1]);
  const regexes = regexLiterals.map((literal) => new RegExp(Function(`return "${literal}"`)()));

  for (const regex of regexes.slice(0, 5)) {
    assert.match("1.2", regex);
    assert.doesNotMatch("dxd", regex);
  }

  const smoothingRegex = regexes[5];
  assert.match("0.5", smoothingRegex);
  assert.match("1.0", smoothingRegex);
  assert.doesNotMatch("0d5", smoothingRegex);
});

test("bus and custom phase docs do not carry stale review notes", async () => {
  const busResearch = await repoSource("../docs/transit-signal-priority-bus-research.md");
  const tspArchitecture = await repoSource("../docs/tsp-architecture.md");
  const customPhaseExtraction = await repoSource("../docs/custom-phase-selection-extraction.md");
  const edgeCaseHeadings = busResearch.match(/^## Edge Cases$/gm) ?? [];

  assert.equal(edgeCaseHeadings.length, 1);
  assert.doesNotMatch(tspArchitecture, /reserved for future bus|effectively track-only|only emits `TspSource\.Track`/);
  assert.match(tspArchitecture, /Transit Signal Priority for buses/i);
  assert.match(tspArchitecture, /Diagnostic cost contract/);
  assert.match(busResearch, /runtime always passes `BusStopRelation\.Unknown`/);
  assert.match(busResearch, /#35/);
  assert.match(busResearch, /#36/);
  assert.match(busResearch, /tram-style aggressive minimum-green preemption via an `OnDedicatedLane` flag/);
  assert.match(busResearch, /The JSONL trace `decision` object now includes a boolean field `onDedicatedLane`/);
  assert.doesNotMatch(busResearch, /aggressive preemption remains tram-only|buses do not use tram-style aggressive minimum-green|No separate bus aggressive-preemption suppression diagnostic is exposed/);
  assert.doesNotMatch(customPhaseExtraction, /production selector reports `false`/);
  assert.match(customPhaseExtraction, /linked-phase\s+behavior remains in `CustomStateMachine`/);
});
