using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using C2VM.TrafficLightsEnhancement.Components;
using C2VM.TrafficLightsEnhancement.Extensions;
using C2VM.TrafficLightsEnhancement.Systems.TrafficLightSystems.Initialisation;
using C2VM.TrafficLightsEnhancement.Utils;
using Colossal.Entities;
using Colossal.UI.Binding;
using Game.Common;
using Game.Net;
using Game.Rendering;
using Game.Simulation;
using Newtonsoft.Json;
using TrafficLightsEnhancement.Logic.Diagnostics;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
namespace C2VM.TrafficLightsEnhancement.Systems.UI;

public partial class UISystem
{
    private const string TspDiagnosticsTraceFileName = "C2VM.TrafficLightsEnhancement.TspDiagnostics.jsonl";
    private const long TspDiagnosticsTraceMaxBytes = 5 * 1024 * 1024;
    private const int TspDiagnosticsTraceMaxRotatedFiles = 3;
    private const int TspDiagnosticsEventHistoryLimit = 100;
    private static readonly object TspDiagnosticsTraceFileLock = new();

    private sealed class TspDiagnosticsHistory
    {
        public string LastSignature = string.Empty;
        public bool HadTspActivity;
        public int Sequence;
        public List<TspDiagnosticsEvent> Events = [];
    }

    private readonly struct TspDiagnosticsEvent
    {
        public TspDiagnosticsEvent(int sequence, string value)
        {
            Sequence = sequence;
            Value = value;
        }

        public int Sequence { get; }

        public string Value { get; }
    }

    private sealed class SelectedJunctionDiagnosticsSnapshot
    {
        public int ConnectedEdgeCount;
        public bool HasTrainTrack;
        public bool HasTrackTurnLanes;
        public bool IsQualifyingFourWay;
        public bool IsComplexJunction;
        public int TrainTrackCount;
        public int TrackLaneLeftCount;
        public int TrackLaneStraightCount;
        public int TrackLaneRightCount;
        public int TotalTrackLaneCount;
        public bool HasCarLane;
        public int TotalCarLaneCount;
        public ArrayList EdgeSummaries = [];
        public bool SplitPhasingSupported;
        public bool ProtectedCentreTurnSupported;
        public bool SplitPhasingProtectedLeftSupported;
        public List<SelectedJunctionPatternSnapshot> AvailablePatternSnapshots = [];
        public bool ExtraOptionsSupported;
        public bool ExtraOptionsVisible;
        public string ExtraOptionsReason = "Supported";
        public SelectedJunctionOptionSnapshot TurningOnRed;
        public SelectedJunctionOptionSnapshot GiveWayToOncomingVehicles;
        public SelectedJunctionOptionSnapshot ExclusivePedestrianPhase;
        public SelectedJunctionOptionSnapshot PedestrianDurationAdjustment;
        public SelectedJunctionTspControlSnapshot TramTransitPriority;
        public SelectedJunctionTspControlSnapshot BusTransitPriority;

        public ArrayList AvailablePatterns
        {
            get
            {
                var patterns = new ArrayList();
                foreach (SelectedJunctionPatternSnapshot pattern in AvailablePatternSnapshots)
                {
                    patterns.Add(pattern.ToPanelObject());
                }

                return patterns;
            }
        }

        public object ToTraceObject() => new
        {
            topology = new
            {
                connectedEdgeCount = ConnectedEdgeCount,
                hasTrainTrack = HasTrainTrack,
                hasTrackTurnLanes = HasTrackTurnLanes,
                isQualifyingFourWay = IsQualifyingFourWay,
                isComplexJunction = IsComplexJunction,
                trainTrackCount = TrainTrackCount,
                trackLaneLeftCount = TrackLaneLeftCount,
                trackLaneStraightCount = TrackLaneStraightCount,
                trackLaneRightCount = TrackLaneRightCount,
                totalTrackLaneCount = TotalTrackLaneCount,
                hasCarLane = HasCarLane,
                totalCarLaneCount = TotalCarLaneCount,
                edges = EdgeSummaries,
            },
            patterns = new
            {
                splitPhasingSupported = SplitPhasingSupported,
                protectedCentreTurnSupported = ProtectedCentreTurnSupported,
                splitPhasingProtectedLeftSupported = SplitPhasingProtectedLeftSupported,
                availablePatterns = AvailablePatterns,
            },
            extraOptions = new
            {
                supported = ExtraOptionsSupported,
                visible = ExtraOptionsVisible,
                reason = ExtraOptionsReason,
                turningOnRed = TurningOnRed.ToTraceObject(),
                giveWayToOncomingVehicles = GiveWayToOncomingVehicles.ToTraceObject(),
                exclusivePedestrianPhase = ExclusivePedestrianPhase.ToTraceObject(),
                pedestrianDurationAdjustment = PedestrianDurationAdjustment.ToTraceObject(),
            },
            transitSignalPriority = new
            {
                tram = TramTransitPriority.ToTraceObject(),
                bus = BusTransitPriority.ToTraceObject(),
            },
        };
    }

    private readonly struct SelectedJunctionPatternSnapshot
    {
        public SelectedJunctionPatternSnapshot(string name, uint value)
        {
            Name = name;
            Value = value;
        }

        public string Name { get; }

        public uint Value { get; }

        public object ToPanelObject() => new { name = Name, value = Value };
    }

    private readonly struct SelectedJunctionOptionSnapshot
    {
        public SelectedJunctionOptionSnapshot(string label, string key, bool isVisible, bool isChecked, string reason)
        {
            Label = label;
            Key = key;
            IsVisible = isVisible;
            IsChecked = isChecked;
            Reason = reason;
        }

        public string Label { get; }

        public string Key { get; }

        public bool IsVisible { get; }

        public bool IsChecked { get; }

        public string Reason { get; }

        public object ToPanelOption() => new { label = Label, isChecked = IsChecked, key = Key };

        public object ToTraceObject() => new
        {
            visible = IsVisible,
            @checked = IsChecked,
            reason = Reason,
        };
    }

    private readonly struct SelectedJunctionTspControlSnapshot
    {
        public SelectedJunctionTspControlSnapshot(bool isVisible, bool isEditable, bool isChecked, string statusLabel, string reason)
        {
            IsVisible = isVisible;
            IsEditable = isEditable;
            IsChecked = isChecked;
            StatusLabel = statusLabel;
            Reason = reason;
        }

        public bool IsVisible { get; }

        public bool IsEditable { get; }

        public bool IsChecked { get; }

        public string StatusLabel { get; }

        public string Reason { get; }

        public object ToTraceObject() => new
        {
            visible = IsVisible,
            editable = IsEditable,
            @checked = IsChecked,
            statusLabel = StatusLabel,
            reason = Reason,
        };
    }

    public static GetterValueBinding<string> m_MainPanelBinding { get; private set; }

    private static GetterValueBinding<string> m_LocaleBinding;

    private GetterValueBinding<string> m_CityConfigurationBinding;

    private GetterValueBinding<Dictionary<string, UITypes.ScreenPoint>> m_ScreenPointBinding;

    private GetterValueBinding<Dictionary<Entity, NativeArray<NodeUtils.EdgeInfo>>> m_EdgeInfoBinding;

    private GetterValueBinding<string> m_SignalDelayDataBinding;

    private ValueBindingHelper<int> m_ActiveEditingCustomPhaseIndexBinding;

    private ValueBindingHelper<int> m_ActiveViewingCustomPhaseIndexBinding;
    internal ValueBindingHelper<UITypes.ToolTooltipMessage[]> m_ToolTooltipMessageBinding;
    
    private GetterValueBinding<string> m_AddMemberStateBinding;

    private GetterValueBinding<string> m_SelectMemberStateBinding;

    private GetterValueBinding<string> m_UncoveredConnectionsBinding;

    private GetterValueBinding<string> m_UserPresetsBinding;

    private GetterValueBinding<List<Entity>> m_AffectedEntitiesBinding;
    private List<Entity> m_AffectedIntersections;

    public List<Entity> AffectedIntersections => m_AffectedIntersections;

    private void AddUIBindings()
    {
        m_MainPanelBinding = CreateBinding("GetMainPanel", GetMainPanel, autoUpdate: false);
        m_LocaleBinding = CreateBinding("GetLocale", GetLocale, autoUpdate: false);
        m_CityConfigurationBinding = CreateBinding("GetCityConfiguration", GetCityConfiguration, autoUpdate: false);
        
        var screenPointBindingKey = UseKeyPrefixes ? "BINDING:GetScreenPoint" : "GetScreenPoint";
        AddBinding(m_ScreenPointBinding = new GetterValueBinding<Dictionary<string, UITypes.ScreenPoint>>(Mod.modName, screenPointBindingKey, GetScreenPoint, new DictionaryWriter<string, UITypes.ScreenPoint>(null, new ValueWriter<UITypes.ScreenPoint>()), new JsonWriter.FalseEqualityComparer<Dictionary<string, UITypes.ScreenPoint>>()));
        
        var edgeInfoBindingKey = UseKeyPrefixes ? "BINDING:GetEdgeInfo" : "GetEdgeInfo";
        AddBinding(m_EdgeInfoBinding = new GetterValueBinding<Dictionary<Entity, NativeArray<NodeUtils.EdgeInfo>>>(Mod.modName, edgeInfoBindingKey, GetEdgeInfo, new JsonWriter.EdgeInfoWriter(), new JsonWriter.FalseEqualityComparer<Dictionary<Entity, NativeArray<NodeUtils.EdgeInfo>>>()));
        
        var signalDelayDataBindingKey = UseKeyPrefixes ? "BINDING:GetSignalDelayData" : "GetSignalDelayData";
        m_SignalDelayDataBinding = CreateBinding(signalDelayDataBindingKey, GetSignalDelayData, autoUpdate: false);

        m_ActiveEditingCustomPhaseIndexBinding = CreateBinding("GetActiveEditingCustomPhaseIndex", -1, autoUpdate: false);
        m_ActiveViewingCustomPhaseIndexBinding = CreateBinding("GetActiveViewingCustomPhaseIndex", -1, autoUpdate: false);
        
        var toolTooltipBindingKey = UseKeyPrefixes ? "BINDING:GetToolTooltipMessage" : "GetToolTooltipMessage";
        m_ToolTooltipMessageBinding = new ValueBindingHelper<UITypes.ToolTooltipMessage[]>(new ValueBinding<UITypes.ToolTooltipMessage[]>(Mod.modName, toolTooltipBindingKey, new UITypes.ToolTooltipMessage[] { }, new ListWriter<UITypes.ToolTooltipMessage>(new ValueWriter<UITypes.ToolTooltipMessage>())));
        AddBinding(m_ToolTooltipMessageBinding.Binding);

        CreateTrigger<uint>("SetPattern", SetPattern);
        CreateTrigger<uint>("ToggleOption", ToggleOption);
        CreateTrigger<float>("SetPedestrianDuration", SetPedestrianDuration);
        CreateTrigger<bool>("ToggleTransitSignalPriorityForTrams", ToggleTransitSignalPriorityForTrams);
        CreateTrigger<bool>("ToggleTransitSignalPriorityForBuses", ToggleTransitSignalPriorityForBuses);
        CreateTrigger<string>("CallMainPanelUpdatePosition", CallMainPanelUpdatePosition);
        CreateTrigger("SavePanel", SavePanel);
        CreateTrigger("ExitPanel", ExitPanel);
        CreateTrigger("ResetLaneDirectionTool", ResetLaneDirectionTool);

        CreateTrigger<int>("SetPanelState", state => SetMainPanelState((MainPanelState)state));
        CreateTrigger<string>("CallAddCustomPhase", CallAddCustomPhase);
        CreateTrigger<string>("CallRemoveCustomPhase", CallRemoveCustomPhase);
        CreateTrigger<string>("CallSwapCustomPhase", CallSwapCustomPhase);
        CreateTrigger<string>("CallSetActiveCustomPhaseIndex", CallSetActiveCustomPhaseIndex);
        CreateTrigger<string>("CallUpdateEdgeGroupMask", CallUpdateEdgeGroupMask);
        CreateTrigger<string>("CallUpdateSubLaneGroupMask", CallUpdateSubLaneGroupMask);
        CreateTrigger<string>("CallUpdateCustomPhaseData", CallUpdateCustomPhaseData);
        CreateTrigger<string>("CallSetTrafficGroupSignalDelay", CallSetTrafficGroupSignalDelay);
        CreateTrigger<string>("CallRemoveSignalDelay", CallRemoveSignalDelay);
        CreateTrigger<string>("CallUpdateSignalDelay", CallUpdateSignalDelay);
        CreateTrigger<string>("CallUpdateEdgeDelay", CallUpdateEdgeDelay);
        CreateTrigger<string>("CallApplyPhaseTemplate", CallApplyPhaseTemplate);

        CreateTrigger<string>("CallKeyPress", CallKeyPress);

        CreateTrigger<string>("CallAddWorldPosition", CallAddWorldPosition);
        CreateTrigger<string>("CallRemoveWorldPosition", CallRemoveWorldPosition);

        CreateTrigger<string>("CallOpenBrowser", CallOpenBrowser);

        CreateTrigger<int>("SetDebugDisplayGroup", (group) => { m_DebugDisplayGroup = group; RedrawGizmo(); });

        CreateTrigger<string>("CallCreateTrafficGroup", CallCreateTrafficGroup);
        CreateTrigger<string>("CallAddJunctionToGroup", CallAddJunctionToGroup);
        CreateTrigger<string>("CallRemoveJunctionFromGroup", CallRemoveJunctionFromGroup);
        CreateTrigger<string>("CallDeleteTrafficGroup", CallDeleteTrafficGroup);
        CreateTrigger<string>("CallSetTrafficGroupName", CallSetTrafficGroupName);
        CreateTrigger<string>("CallSetGreenWaveEnabled", CallSetGreenWaveEnabled);
        CreateTrigger<string>("CallSetGreenWaveSpeed", CallSetGreenWaveSpeed);
        CreateTrigger<string>("CallSetGreenWaveOffset", CallSetGreenWaveOffset);
        CreateTrigger<string>("CallCalculateSignalDelays", CallCalculateSignalDelays);
        CreateTrigger<string>("CallSetCoordinated", CallSetCoordinated);
        CreateTrigger<string>("CallSetCycleLength", CallSetCycleLength);
        CreateTrigger<string>("CallSelectJunction", CallSelectJunction);
        CreateTrigger<string>("CallEnterAddMemberMode", CallEnterAddMemberMode);
        CreateTrigger("ExitAddMemberMode", ExitAddMemberMode);
        CreateTrigger("FinishAddMemberMode", FinishAddMemberMode);
        CreateTrigger<string>("CallEnterSelectMemberMode", CallEnterSelectMemberMode);
        CreateTrigger<string>("CallExitSelectMemberMode", CallExitSelectMemberMode);
        CreateTrigger<string>("CallGetGroupMembers", CallGetGroupMembers);
        CreateTrigger<string>("CallForceSyncToLeader", CallForceSyncToLeader);
        CreateTrigger<string>("CallRecalculateCycleLength", CallRecalculateCycleLength);
        CreateTrigger<string>("CallJoinGroups", CallJoinGroups);
        CreateTrigger<string>("CallSetGroupLeader", CallSetGroupLeader);
        CreateTrigger<string>("CallSkipStep", CallSkipStep);
        CreateTrigger<string>("CallMatchPhaseDurationsToLeader", CallMatchPhaseDurationsToLeader);
        CreateTrigger<string>("CallApplyBestPhase", CallApplyBestPhase);
        CreateTrigger<string>("CallHousekeepingGroup", CallHousekeepingGroup);
        CreateTrigger<string>("CallUpdateMemberPhaseData", CallUpdateMemberPhaseData);
        CreateTrigger<string>("CallUpdateEdgeGroupMaskForJunction", CallUpdateEdgeGroupMaskForJunction);
        CreateTrigger<string>("CallUpdateMemberPattern", CallUpdateMemberPattern);
        CreateTrigger<string>("CallHighlightEdge", CallHighlightEdge);
        CreateTrigger<string>("CallSaveUserPreset", CallSaveUserPreset);
        CreateTrigger<string>("CallDeleteUserPreset", CallDeleteUserPreset);
        CreateTrigger<string>("CallApplyUserPreset", CallApplyUserPreset);
        CreateTrigger<string>("CallUpdateUserPreset", CallUpdateUserPreset);
        AddBinding(new TriggerBinding<Entity>(Mod.modName, "GoTo", NavigateTo));

        m_UserPresetsBinding = CreateBinding("GetUserPresets", GetUserPresets, autoUpdate: false);
        m_AddMemberStateBinding = CreateBinding("GetAddMemberState", GetAddMemberState, autoUpdate: false);
        m_SelectMemberStateBinding = CreateBinding("GetSelectMemberState", GetSelectMemberState, autoUpdate: false);
        m_UncoveredConnectionsBinding = CreateBinding("GetUncoveredConnections", GetUncoveredConnections, autoUpdate: false);
        var affectedEntitiesBindingKey = UseKeyPrefixes ? "BINDING:GetAffectedEntities" : "GetAffectedEntities";
        AddUpdateBinding(m_AffectedEntitiesBinding = new GetterValueBinding<List<Entity>>(Mod.modName, affectedEntitiesBindingKey, () => AffectedIntersections, new ListWriter<Entity>()));
        CreateTrigger<string>("NavigateToEntity", CallNavigateToEntity);
        CreateTrigger<int>("RemoveAffectedEntity", RemoveAffectedEntity);
    }

    protected string GetUncoveredConnections()
    {
        if (m_SelectedEntity == Entity.Null || !m_EdgeInfoDictionary.ContainsKey(m_SelectedEntity))
        {
            return JsonConvert.SerializeObject(new { hasUncovered = false, uncoveredCount = 0, totalLaneConnections = 0, uncoveredConnections = new object[0] });
        }

        var edgeInfoArray = m_EdgeInfoDictionary[m_SelectedEntity];
        NativeList<NodeUtils.EdgeInfo> edgeInfoList = new(edgeInfoArray.Length, Allocator.Temp);
        foreach (var edgeInfo in edgeInfoArray)
        {
            edgeInfoList.Add(edgeInfo);
        }

        var result = UncoveredConnectionsAnalyzer.FindUncoveredConnections(Allocator.Temp, edgeInfoList);

        var uncoveredList = new List<object>();
        if (result.UncoveredConnections.IsCreated)
        {
            foreach (var connection in result.UncoveredConnections)
            {
                uncoveredList.Add(new
                {
                    edge = new { index = connection.m_Edge.Index, version = connection.m_Edge.Version },
                    position = new { x = connection.m_Position.x, y = connection.m_Position.y, z = connection.m_Position.z },
                    vehicleGroup = (int)connection.m_VehicleGroup,
                    vehicleGroupName = GetVehicleGroupName(connection.m_VehicleGroup),
                    turnType = (int)connection.m_TurnType,
                    turnTypeName = GetTurnTypeName(connection.m_TurnType),
                    laneCount = connection.m_LaneCount
                });
            }
        }

        var response = new
        {
            hasUncovered = result.HasUncovered,
            uncoveredCount = result.UncoveredCount,
            totalLaneConnections = result.TotalLaneConnections,
            uncoveredConnections = uncoveredList
        };

        result.Dispose();
        edgeInfoList.Dispose();

        return JsonConvert.SerializeObject(response);
    }

    private static string GetVehicleGroupName(VehicleGroup group)
    {
        if ((group & VehicleGroup.Car) != 0 && (group & VehicleGroup.PublicCar) == 0)
            return "Car";
        if ((group & VehicleGroup.PublicCar) != 0)
            return "PublicTransport";
        if ((group & (VehicleGroup.Tram | VehicleGroup.Train)) != 0)
            return "Track";
        if ((group & VehicleGroup.Bike) != 0)
            return "Bicycle";
        if ((group & VehicleGroup.Pedestrian) != 0)
            return "Pedestrian";
        return "Unknown";
    }

    private static string GetTurnTypeName(TurnType turnType)
    {
        return turnType switch
        {
            TurnType.Left => "Left",
            TurnType.Straight => "Straight",
            TurnType.Right => "Right",
            TurnType.UTurn => "UTurn",
            TurnType.GentleLeft => "GentleLeft",
            TurnType.GentleRight => "GentleRight",
            _ => "Unknown"
        };
    }

    private bool IsTrafficTypeActive(Entity junctionEntity, int phaseIndex, string trafficType)
    {
        if (junctionEntity == Entity.Null || !EntityManager.Exists(junctionEntity))
        {
            return false;
        }
        if (phaseIndex < 0 || phaseIndex >= 16)
        {
            return false;
        }

        ushort phaseBit = (ushort)(1 << phaseIndex);

        static bool IsSignalActive(GroupMask.Signal signal, ushort bit)
        {
            return (signal.m_GoGroupMask & bit) != 0 || (signal.m_YieldGroupMask & bit) != 0;
        }

        static bool IsTurnActive(GroupMask.Turn turn, ushort bit)
        {
            return
                IsSignalActive(turn.m_Left, bit) ||
                IsSignalActive(turn.m_Straight, bit) ||
                IsSignalActive(turn.m_Right, bit) ||
                IsSignalActive(turn.m_UTurn, bit);
        }

        if (EntityManager.HasBuffer<EdgeGroupMask>(junctionEntity))
        {
            var edgeMasks = EntityManager.GetBuffer<EdgeGroupMask>(junctionEntity);
            for (int i = 0; i < edgeMasks.Length; i++)
            {
                var mask = edgeMasks[i];
                switch (trafficType)
                {
                    case "car":
                        if (IsTurnActive(mask.m_Car, phaseBit)) return true;
                        break;
                    case "publicCar":
                        if (IsTurnActive(mask.m_PublicCar, phaseBit)) return true;
                        break;
                    case "track":
                        if (IsTurnActive(mask.m_Track, phaseBit)) return true;
                        break;
                    case "pedestrian":
                        if (IsSignalActive(mask.m_Pedestrian, phaseBit)) return true;
                        break;
                    case "bicycle":
                        if (IsSignalActive(mask.m_Bicycle, phaseBit)) return true;
                        break;
                }
            }
        }

        if (EntityManager.HasBuffer<SubLaneGroupMask>(junctionEntity))
        {
            var subLaneMasks = EntityManager.GetBuffer<SubLaneGroupMask>(junctionEntity);
            for (int i = 0; i < subLaneMasks.Length; i++)
            {
                var mask = subLaneMasks[i];
                switch (trafficType)
                {
                    case "car":
                        if (IsTurnActive(mask.m_Car, phaseBit)) return true;
                        break;
                    case "track":
                        if (IsTurnActive(mask.m_Track, phaseBit)) return true;
                        break;
                    case "pedestrian":
                        if (IsSignalActive(mask.m_Pedestrian, phaseBit)) return true;
                        break;
                }
            }
        }

        return false;
    }

    protected string GetMainPanel()
    {
        object mainData = null;
        object emptyData = null;
        object customPhaseHeader = null;
        ArrayList phasesArray = null;
        ArrayList groupsArray = null;

        if (m_MainPanelState == MainPanelState.Main && m_SelectedEntity != Entity.Null)
        {
            bool isGroupMember = EntityManager.HasComponent<TrafficGroupMember>(m_SelectedEntity);
            bool isCustomPhaseMode = m_CustomTrafficLights.GetPatternOnly() == CustomTrafficLights.Patterns.CustomPhase;
            uint selectedPattern = (uint)m_CustomTrafficLights.GetPattern();

            TransitSignalPrioritySettings tspSettings = EntityManager.HasComponent<TransitSignalPrioritySettings>(m_SelectedEntity)
                ? EntityManager.GetComponentData<TransitSignalPrioritySettings>(m_SelectedEntity)
                : TransitSignalPrioritySettings.CreateDefault();
            bool showTransitSignalPriorityDiagnostics = Mod.m_Setting != null && Mod.m_Setting.m_ShowTransitSignalPriorityDiagnostics;
            SelectedJunctionDiagnosticsSnapshot selectedJunction = GetSelectedJunctionDiagnosticsSnapshot(
                m_SelectedEntity,
                tspSettings,
                showTransitSignalPriorityDiagnostics);
            object tspDiagnostics = showTransitSignalPriorityDiagnostics
                ? GetTransitSignalPriorityDiagnostics(m_SelectedEntity, tspSettings, selectedJunction)
                : null;

            var options = new ArrayList();
            if (selectedJunction.TurningOnRed.IsVisible)
            {
                options.Add(selectedJunction.TurningOnRed.ToPanelOption());
            }

            if (selectedJunction.GiveWayToOncomingVehicles.IsVisible)
            {
                options.Add(selectedJunction.GiveWayToOncomingVehicles.ToPanelOption());
            }

            if (selectedJunction.ExclusivePedestrianPhase.IsVisible)
            {
                options.Add(selectedJunction.ExclusivePedestrianPhase.ToPanelOption());
            }

            mainData = new
            {
                isGroupMember,
                selectedPattern,
                availablePatterns = selectedJunction.AvailablePatterns,
                options,
                showOptions = selectedJunction.ExtraOptionsVisible,
                showPedestrianDuration = selectedJunction.PedestrianDurationAdjustment.IsVisible,
                pedestrianDurationMultiplier = m_CustomTrafficLights.m_PedestrianPhaseDurationMultiplier,
                hasLaneDirectionTool = EntityManager.HasBuffer<C2VM.CommonLibraries.LaneSystem.CustomLaneDirection>(m_SelectedEntity),
                hasUnsavedChanges = m_ShowNotificationUnsaved,
                isCustomPhaseMode,
                transitSignalPriority = new
                {
                    tram = new
                    {
                        isVisible = selectedJunction.TramTransitPriority.IsVisible,
                        isEnabled = selectedJunction.TramTransitPriority.IsChecked,
                        isEditable = selectedJunction.TramTransitPriority.IsEditable,
                        statusLabel = selectedJunction.TramTransitPriority.StatusLabel
                    },
                    bus = new
                    {
                        isVisible = selectedJunction.BusTransitPriority.IsVisible,
                        isEnabled = selectedJunction.BusTransitPriority.IsChecked,
                        isEditable = selectedJunction.BusTransitPriority.IsEditable,
                        statusLabel = selectedJunction.BusTransitPriority.StatusLabel
                    },
                    diagnostics = tspDiagnostics
                }
            };
        }
        else if (m_MainPanelState == MainPanelState.CustomPhase)
        {
            DynamicBuffer<CustomPhaseData> customPhaseDataBuffer;
            if (!EntityManager.TryGetBuffer(m_SelectedEntity, true, out customPhaseDataBuffer))
            {
                customPhaseDataBuffer = EntityManager.AddBuffer<CustomPhaseData>(m_SelectedEntity);
            }
            EntityManager.TryGetComponent(m_SelectedEntity, out TrafficLights trafficLights);
            EntityManager.TryGetComponent(m_SelectedEntity, out CustomTrafficLights customTrafficLights);

            bool isCoordinatedFollower = false;
            Entity leaderEntity = Entity.Null;
            DynamicBuffer<CustomPhaseData> leaderPhaseBuffer = default;
            CustomTrafficLights leaderCustomTrafficLights = default;
            bool hasLeaderData = false;

            if (EntityManager.TryGetComponent(m_SelectedEntity, out TrafficGroupMember member))
            {
                if (!member.m_IsGroupLeader && member.m_GroupEntity != Unity.Entities.Entity.Null)
                {
                    if (EntityManager.TryGetComponent(member.m_GroupEntity, out TrafficGroup group))
                    {
                        isCoordinatedFollower = group.m_IsCoordinated;
                        if (isCoordinatedFollower)
                        {
                            leaderEntity = member.m_LeaderEntity;
                            if (leaderEntity != Entity.Null &&
                                EntityManager.TryGetBuffer(leaderEntity, true, out leaderPhaseBuffer) &&
                                EntityManager.TryGetComponent(leaderEntity, out leaderCustomTrafficLights))
                            {
                                hasLeaderData = true;
                            }
                        }
                    }
                }
            }

            customPhaseHeader = new UITypes.ItemCustomPhaseHeader
            {
                trafficLightMode = customTrafficLights.GetMode() == CustomTrafficLights.TrafficMode.FixedTimed ? 1 : 0,
                phaseCount = customPhaseDataBuffer.Length,
                isCoordinatedFollower = isCoordinatedFollower
            };

            phasesArray = new ArrayList();
            for (int i = 0; i < customPhaseDataBuffer.Length; i++)
            {
                var sourcePhaseData = customPhaseDataBuffer[i];
                var sourceCustomTrafficLights = customTrafficLights;
                if (hasLeaderData && i < leaderPhaseBuffer.Length)
                {
                    sourcePhaseData = leaderPhaseBuffer[i];
                    sourceCustomTrafficLights = leaderCustomTrafficLights;
                }

                phasesArray.Add(new UITypes.ItemCustomPhase
                {
                    activeIndex = m_ActiveEditingCustomPhaseIndexBinding.Value,
                    activeViewingIndex = m_ActiveViewingCustomPhaseIndexBinding.Value,
                    currentSignalGroup = trafficLights.m_CurrentSignalGroup,
                    manualSignalGroup = customTrafficLights.m_ManualSignalGroup,
                    index = i,
                    length = customPhaseDataBuffer.Length,
                    timer = trafficLights.m_CurrentSignalGroup == i + 1 ? customTrafficLights.m_Timer : 0,
                    turnsSinceLastRun = customPhaseDataBuffer[i].m_TurnsSinceLastRun,
                    lowFlowTimer = customPhaseDataBuffer[i].m_LowFlowTimer,
                    carFlow = customPhaseDataBuffer[i].AverageCarFlow(),
                    carLaneOccupied = customPhaseDataBuffer[i].m_CarLaneOccupied,
                    publicCarLaneOccupied = customPhaseDataBuffer[i].m_PublicCarLaneOccupied,
                    trackLaneOccupied = customPhaseDataBuffer[i].m_TrackLaneOccupied,
                    pedestrianLaneOccupied = customPhaseDataBuffer[i].m_PedestrianLaneOccupied,
                    weightedWaiting = customPhaseDataBuffer[i].m_WeightedWaiting,
                    targetDuration = sourcePhaseData.m_TargetDuration,
                    priority = sourcePhaseData.m_Priority,
                    minimumDuration = sourcePhaseData.m_MinimumDuration,
                    maximumDuration = sourcePhaseData.m_MaximumDuration,
                    targetDurationMultiplier = sourcePhaseData.m_TargetDurationMultiplier,
                    intervalExponent = sourcePhaseData.m_IntervalExponent,
                    linkedWithNextPhase = (customPhaseDataBuffer[i].m_Options & CustomPhaseData.Options.LinkedWithNextPhase) != 0,
                    endPhasePrematurely = (customPhaseDataBuffer[i].m_Options & CustomPhaseData.Options.EndPhasePrematurely) != 0,
                    bicycleLaneOccupied = customPhaseDataBuffer[i].m_BicycleLaneOccupied,
                    changeMetric = (int)sourcePhaseData.m_ChangeMetric,
                    waitFlowBalance = sourcePhaseData.m_WaitFlowBalance,
                    trafficLightMode = sourceCustomTrafficLights.GetMode() == CustomTrafficLights.TrafficMode.FixedTimed ? 1 : 0,
                    smartPhaseSelection = (sourceCustomTrafficLights.GetOptions() & CustomTrafficLights.TrafficOptions.SmartPhaseSelection) != 0,
                    carActive = IsTrafficTypeActive(m_SelectedEntity, i, "car"),
                    publicCarActive = IsTrafficTypeActive(m_SelectedEntity, i, "publicCar"),
                    trackActive = IsTrafficTypeActive(m_SelectedEntity, i, "track"),
                    pedestrianActive = IsTrafficTypeActive(m_SelectedEntity, i, "pedestrian"),
                    bicycleActive = IsTrafficTypeActive(m_SelectedEntity, i, "bicycle"),
                    hasSignalDelays = true,
                    carOpenDelay = customPhaseDataBuffer[i].m_CarOpenDelay,
                    carCloseDelay = customPhaseDataBuffer[i].m_CarCloseDelay,
                    publicCarOpenDelay = customPhaseDataBuffer[i].m_PublicCarOpenDelay,
                    publicCarCloseDelay = customPhaseDataBuffer[i].m_PublicCarCloseDelay,
                    trackOpenDelay = customPhaseDataBuffer[i].m_TrackOpenDelay,
                    trackCloseDelay = customPhaseDataBuffer[i].m_TrackCloseDelay,
                    pedestrianOpenDelay = customPhaseDataBuffer[i].m_PedestrianOpenDelay,
                    pedestrianCloseDelay = customPhaseDataBuffer[i].m_PedestrianCloseDelay,
                    bicycleOpenDelay = customPhaseDataBuffer[i].m_BicycleOpenDelay,
                    bicycleCloseDelay = customPhaseDataBuffer[i].m_BicycleCloseDelay,
                    carWeight = sourcePhaseData.m_CarWeight,
                    publicCarWeight = sourcePhaseData.m_PublicCarWeight,
                    trackWeight = sourcePhaseData.m_TrackWeight,
                    pedestrianWeight = sourcePhaseData.m_PedestrianWeight,
                    bicycleWeight = sourcePhaseData.m_BicycleWeight,
                    smoothingFactor = sourcePhaseData.m_SmoothingFactor,
                    flowRatio = customPhaseDataBuffer[i].m_FlowRatio,
                    waitRatio = customPhaseDataBuffer[i].m_WaitRatio
                });
            }
        }
        else if (m_MainPanelState == MainPanelState.TrafficGroups)
        {
            var trafficGroupSystem = World.GetOrCreateSystemManaged<TrafficGroupSystem>();
            var allGroups = trafficGroupSystem.GetAllGroups();
            Entity currentJunctionGroup = trafficGroupSystem.GetJunctionGroup(m_SelectedEntity);
            
            foreach (var groupEntity in allGroups)
            {
                if (EntityManager.HasComponent<TrafficGroup>(groupEntity))
                {
                    var group = EntityManager.GetComponentData<TrafficGroup>(groupEntity);
                    string groupName = trafficGroupSystem.GetGroupName(groupEntity);
                    int memberCount = trafficGroupSystem.GetGroupMemberCount(groupEntity);
                    
                    Entity leaderEntity = Entity.Null;
                    float distanceToLeader = 0f;
                    int phaseOffset = 0;
                    int signalDelay = 0;
                    bool isCurrentJunctionLeader = false;
                    
                    if (groupEntity == currentJunctionGroup && m_SelectedEntity != Entity.Null && EntityManager.HasComponent<TrafficGroupMember>(m_SelectedEntity))
                    {
                        var currentMember = EntityManager.GetComponentData<TrafficGroupMember>(m_SelectedEntity);
                        leaderEntity = currentMember.m_LeaderEntity;
                        distanceToLeader = currentMember.m_DistanceToLeader;
                        phaseOffset = currentMember.m_PhaseOffset;
                        signalDelay = currentMember.m_SignalDelay;
                        isCurrentJunctionLeader = currentMember.m_IsGroupLeader;
                    }
                    
                    var membersList = new ArrayList();
                    var leaderMembersList = new ArrayList();
                    var groupMembers = trafficGroupSystem.GetGroupMembers(groupEntity);
                    foreach (var memberEntity in groupMembers)
                    {
                        if (EntityManager.HasComponent<TrafficGroupMember>(memberEntity))
                        {
                            var memberData = EntityManager.GetComponentData<TrafficGroupMember>(memberEntity);
                            
                            
                            var phases = new ArrayList();
                            if (EntityManager.HasBuffer<CustomPhaseData>(memberEntity))
                            {
                                var phaseBuffer = EntityManager.GetBuffer<CustomPhaseData>(memberEntity);
                                for (int i = 0; i < phaseBuffer.Length; i++)
                                {
                                    phases.Add(new {
                                        index = i,
                                        minimumDuration = (int)phaseBuffer[i].m_MinimumDuration,
                                        maximumDuration = (int)phaseBuffer[i].m_MaximumDuration
                                    });
                                }
                            }
                            
                            
                            uint currentPattern = 0;
                            var availablePatterns = new ArrayList();
                            bool hasTrainTrack = false;
                            
                            if (EntityManager.HasComponent<CustomTrafficLights>(memberEntity))
                            {
                                var memberTrafficLights = EntityManager.GetComponentData<CustomTrafficLights>(memberEntity);
                                currentPattern = (uint)memberTrafficLights.GetPattern();
                                
                                
                                if (m_EdgeInfoDictionary.ContainsKey(memberEntity))
                                {
                                    var memberEdgeInfo = m_EdgeInfoDictionary[memberEntity];
                                    hasTrainTrack = NodeUtils.HasTrainTrack(memberEdgeInfo);
                                    
                                    availablePatterns.Add(new { name = "Vanilla", value = (uint)CustomTrafficLights.Patterns.Vanilla });
                                    
                                    if (PredefinedPatternsProcessor.IsValidPattern(memberEdgeInfo, CustomTrafficLights.Patterns.SplitPhasing))
                                    {
                                        availablePatterns.Add(new { name = "SplitPhasing", value = (uint)CustomTrafficLights.Patterns.SplitPhasing });
                                    }
                                    if (PredefinedPatternsProcessor.IsValidPattern(memberEdgeInfo, CustomTrafficLights.Patterns.ProtectedCentreTurn))
                                    {
                                        string patternName = m_CityConfigurationSystem.leftHandTraffic ? "ProtectedRightTurns" : "ProtectedLeftTurns";
                                        availablePatterns.Add(new { name = patternName, value = (uint)CustomTrafficLights.Patterns.ProtectedCentreTurn });
                                    }
                                    if (PredefinedPatternsProcessor.IsValidPattern(memberEdgeInfo, CustomTrafficLights.Patterns.SplitPhasingProtectedLeft))
                                    {
                                        availablePatterns.Add(new { name = "SplitPhasingProtectedLeft", value = (uint)CustomTrafficLights.Patterns.SplitPhasingProtectedLeft });
                                    }
                                    availablePatterns.Add(new { name = "CustomPhases", value = (uint)CustomTrafficLights.Patterns.CustomPhase });
                                }
                            }

                            bool phaseSetupComplete = memberData.m_IsGroupLeader
                                || (EntityManager.TryGetComponent(
                                        memberEntity,
                                        out TrafficGroupPhaseMapping phaseMapping)
                                    && phaseMapping.m_Map.IsComplete);
                            
                            var memberInfo = new {
                                entity = memberEntity,
                                index = memberEntity.Index,
                                version = memberEntity.Version,
                                groupIndex = memberData.m_GroupIndex,
                                isLeader = memberData.m_IsGroupLeader,
                                distanceToLeader = memberData.m_DistanceToLeader,
                                phaseOffset = memberData.m_PhaseOffset,
                                signalDelay = memberData.m_SignalDelay,
                                isCurrentJunction = memberEntity == m_SelectedEntity,
                                phases = phases,
                                phaseCount = phases.Count,
                                currentPattern = currentPattern,
                                availablePatterns = availablePatterns,
                                hasTrainTrack = hasTrainTrack,
                                phaseSetupComplete = phaseSetupComplete
                            };
                            
                            if (memberData.m_IsGroupLeader)
                            {
                                leaderMembersList.Add(memberInfo);
                            }
                            else
                            {
                                membersList.Add(memberInfo);
                            }
                        }
                    }
                    groupMembers.Dispose();
                    
                    
                    var sortedMembers = membersList.ToArray()
                        .OrderBy(m => (int)m.GetType().GetProperty("groupIndex").GetValue(m))
                        .ToArray();
                    
                    var sortedMembersList = new ArrayList();
                    sortedMembersList.AddRange(leaderMembersList);
                    sortedMembersList.AddRange(sortedMembers);
                    
                    if (groupsArray == null) groupsArray = new ArrayList();
                    groupsArray.Add(new UITypes.ItemTrafficGroup
                    {
                        groupIndex = groupEntity.Index,
                        groupVersion = groupEntity.Version,
                        name = groupName,
                        memberCount = memberCount,
                        isCoordinated = group.m_IsCoordinated,
                        isCurrentJunctionInGroup = groupEntity == currentJunctionGroup,
                        greenWaveEnabled = group.m_GreenWaveEnabled,
                        greenWaveSpeed = group.m_GreenWaveSpeed,
                        greenWaveOffset = group.m_GreenWaveOffset,
                        leaderIndex = leaderEntity.Index,
                        leaderVersion = leaderEntity.Version,
                        currentJunctionIndex = m_SelectedEntity.Index,
                        currentJunctionVersion = m_SelectedEntity.Version,
                        distanceToLeader = distanceToLeader,
                        phaseOffset = phaseOffset,
                        signalDelay = signalDelay,
                        isCurrentJunctionLeader = isCurrentJunctionLeader,
                        members = sortedMembersList,
                        previousState = (int)m_PreviousMainPanelState,
                        cycleLength = group.m_CycleLength
                    });
                }
            }
            allGroups.Dispose();
        }
        else if (m_MainPanelState == MainPanelState.Empty)
        {
            if (m_IsAddingMember)
            {
                var trafficGroupSystem = World.GetOrCreateSystemManaged<TrafficGroupSystem>();
                string groupName = trafficGroupSystem.GetGroupName(m_TargetGroupForMember);
                emptyData = new { isAddingMember = true, targetGroupName = groupName };
            }
            else
            {
                emptyData = new { isAddingMember = false, targetGroupName = string.Empty };
            }
        }

        var menu = new
        {
            title = Mod.IsBeta() ? "TLE Beta" : "Traffic Lights Enhancement",
            image = "Media/Game/Icons/TrafficLights.svg",
            position = m_MainPanelPosition,
            showPanel = m_MainPanelState != MainPanelState.Hidden,
            showFloatingButton = true,
            state = m_MainPanelState,
            selectedEntity = new { index = m_SelectedEntity.Index, version = m_SelectedEntity.Version },
            mainData,
            emptyData,
            customPhaseHeader,
            phases = phasesArray,
            groups = groupsArray
        };

        return JsonConvert.SerializeObject(menu);
    }

    public static string GetLocale()
    {
        var result = new
        {
            locale = GetLocaleCode(),
        };

        return JsonConvert.SerializeObject(result);
    }

    public string GetCityConfiguration()
    {
        var result = new
        {
            leftHandTraffic = m_CityConfigurationSystem.leftHandTraffic,
        };

        return JsonConvert.SerializeObject(result);
    }

    protected Dictionary<string, UITypes.ScreenPoint> GetScreenPoint()
    {
        Dictionary<string, UITypes.ScreenPoint> screenPointDictionary = [];
        m_Camera = Camera.main;
        m_ScreenHeight = Screen.height;
        foreach (var wp in m_WorldPositionList)
        {
            if (!screenPointDictionary.ContainsKey(wp))
            {
                screenPointDictionary[wp] = new UITypes.ScreenPoint(m_Camera.WorldToScreenPoint(wp), m_ScreenHeight);
            }
        }
        return screenPointDictionary;
    }

    protected Dictionary<Entity, NativeArray<NodeUtils.EdgeInfo>> GetEdgeInfo()
    {
        return m_EdgeInfoDictionary;
    }

    protected void SetPattern(uint rawPatternValue)
    {
        if (EntityManager.HasComponent<TrafficGroupMember>(m_SelectedEntity))
        {
            rawPatternValue = (uint)CustomTrafficLights.Patterns.CustomPhase;
        }

        var selectedPattern = (CustomTrafficLights.Patterns)rawPatternValue;
        bool isCustomPhasePattern = (selectedPattern & (CustomTrafficLights.Patterns)0xFFFF) == CustomTrafficLights.Patterns.CustomPhase;
        
        m_CustomTrafficLights.SetPattern(selectedPattern);
        if (m_CustomTrafficLights.GetPattern() != CustomTrafficLights.Patterns.Vanilla)
        {
            var currentPattern = m_CustomTrafficLights.GetPattern();
            currentPattern = currentPattern & ~CustomTrafficLights.Patterns.CentreTurnGiveWay;
            m_CustomTrafficLights.SetPattern(currentPattern);
        }
        
        if (isCustomPhasePattern)
        {
            // Set to Dynamic mode when entering CustomPhases
            m_CustomTrafficLights.SetMode(CustomTrafficLights.TrafficMode.Dynamic);
            
            DynamicBuffer<CustomPhaseData> customPhaseDataBuffer;
            if (!EntityManager.TryGetBuffer(m_SelectedEntity, false, out customPhaseDataBuffer))
            {
                customPhaseDataBuffer = EntityManager.AddBuffer<CustomPhaseData>(m_SelectedEntity);
            }
            if (!EntityManager.HasBuffer<EdgeGroupMask>(m_SelectedEntity))
            {
                EntityManager.AddComponent<EdgeGroupMask>(m_SelectedEntity);
            }
            if (!EntityManager.HasBuffer<SubLaneGroupMask>(m_SelectedEntity))
            {
                EntityManager.AddComponent<SubLaneGroupMask>(m_SelectedEntity);
            }
            if (customPhaseDataBuffer.Length == 0)
            {
                customPhaseDataBuffer.Add(new CustomPhaseData());
            }
            UpdateEdgeInfo(m_SelectedEntity);
            UpdateActiveEditingCustomPhaseIndex(0);
        }
        else
        {
            // Clear custom phase mode when switching to a predefined pattern
            m_CustomTrafficLights.SetMode((CustomTrafficLights.TrafficMode)uint.MaxValue);
        }
        UpdateEntity();
        
        
        if (EntityManager.HasComponent<TrafficGroupMember>(m_SelectedEntity))
        {
            var member = EntityManager.GetComponentData<TrafficGroupMember>(m_SelectedEntity);
            if (member.m_IsGroupLeader && member.m_GroupEntity != Entity.Null)
            {
                var trafficGroupSystem = World.GetOrCreateSystemManaged<TrafficGroupSystem>();
                trafficGroupSystem.PropagatePatternToMembers(member.m_GroupEntity, m_CustomTrafficLights.GetPattern());
            }
        }
        
        m_MainPanelBinding.Update();
    }

    protected void ToggleOption(uint key)
    {
        foreach (CustomTrafficLights.Patterns pattern in System.Enum.GetValues(typeof(CustomTrafficLights.Patterns)))
        {
            if (((uint)pattern & 0xFFFF0000) != 0)
            {
                if (key == (uint)pattern)
                {
                    var currentPattern = m_CustomTrafficLights.GetPattern();
                    m_CustomTrafficLights.SetPattern(currentPattern ^ pattern);
                }
            }
        }
        UpdateEntity();
        m_MainPanelBinding.Update();
    }

    protected void SetPedestrianDuration(float value)
    {
        m_CustomTrafficLights.SetPedestrianPhaseDurationMultiplier(value);
        UpdateEntity();
        m_MainPanelBinding.Update();
    }

    protected void ToggleTransitSignalPriorityForTrams(bool enabled)
    {
        ToggleTransitSignalPrioritySource(enabled, allowTrackRequests: true);
    }

    protected void ToggleTransitSignalPriorityForBuses(bool enabled)
    {
        ToggleTransitSignalPrioritySource(enabled, allowTrackRequests: false);
    }

    private void ToggleTransitSignalPrioritySource(bool enabled, bool allowTrackRequests)
    {
        if (m_SelectedEntity == Entity.Null || !EntityManager.Exists(m_SelectedEntity))
        {
            return;
        }

        if (EntityManager.HasComponent<TrafficGroupMember>(m_SelectedEntity))
        {
            return;
        }

        bool hasExistingTransitSignalPrioritySettings =
            EntityManager.HasComponent<TransitSignalPrioritySettings>(m_SelectedEntity);
        TransitSignalPrioritySettings settings = hasExistingTransitSignalPrioritySettings
            ? EntityManager.GetComponentData<TransitSignalPrioritySettings>(m_SelectedEntity)
            : TransitSignalPrioritySettings.CreateDefault();

        if (!hasExistingTransitSignalPrioritySettings)
        {
            settings.m_AllowTrackRequests = false;
            settings.m_AllowPublicCarRequests = false;
        }

        if (allowTrackRequests)
        {
            settings.m_AllowTrackRequests = enabled;
        }
        else
        {
            settings.m_AllowPublicCarRequests = enabled;
        }

        settings.m_Enabled = settings.m_AllowTrackRequests || settings.m_AllowPublicCarRequests;
        settings.Normalize();

        if (!settings.m_Enabled)
        {
            if (hasExistingTransitSignalPrioritySettings)
            {
                EntityManager.RemoveComponent<TransitSignalPrioritySettings>(m_SelectedEntity);
                EntityManager.AddComponentData(m_SelectedEntity, default(Game.Common.Updated));
            }

            m_MainPanelBinding.Update();
            return;
        }

        if (hasExistingTransitSignalPrioritySettings)
        {
            EntityManager.SetComponentData(m_SelectedEntity, settings);
        }
        else
        {
            EntityManager.AddComponentData(m_SelectedEntity, settings);
        }

        EntityManager.AddComponentData(m_SelectedEntity, default(Game.Common.Updated));
        m_MainPanelBinding.Update();
    }

    private object GetTransitSignalPriorityDiagnostics(
        Entity entity,
        TransitSignalPrioritySettings settings,
        SelectedJunctionDiagnosticsSnapshot selectedJunction)
    {
        bool hasTrafficLights = EntityManager.TryGetComponent(entity, out TrafficLights trafficLights);
        bool hasRuntimeDebug = EntityManager.TryGetComponent(entity, out TransitSignalPriorityRuntimeDebugInfo runtimeDebug);
        bool hasBusApproachDebug = EntityManager.TryGetComponent(entity, out TransitSignalPriorityBusApproachDebugInfo busApproachDebug);
        bool hasDecisionTrace = EntityManager.TryGetComponent(entity, out TransitSignalPriorityDecisionTrace decisionTrace);
        var summary = GetTspDiagnosticsSummary(
            settings,
            hasTrafficLights,
            trafficLights,
            hasRuntimeDebug,
            runtimeDebug,
            hasBusApproachDebug,
            busApproachDebug,
            hasDecisionTrace,
            decisionTrace);
        ArrayList diagnosticEvents = GetTspDiagnosticsEvents(
            entity,
            summary.value,
            hasTrafficLights,
            trafficLights,
            hasRuntimeDebug,
            runtimeDebug,
            hasBusApproachDebug,
            busApproachDebug,
            hasDecisionTrace,
            decisionTrace,
            selectedJunction);
        bool showTspEvents = settings.m_Enabled
            && !EntityManager.HasComponent<TrafficGroupMember>(entity);
        ArrayList events = showTspEvents ? diagnosticEvents : new ArrayList();

        var rows = new ArrayList
        {
            new { label = "TSPDiagnosticsEnabled", value = settings.m_Enabled ? "Yes" : "No" }
        };
        rows.Add(new { label = "TSPDiagnosticsJunctionTopology", value = FormatSelectedJunctionTopology(selectedJunction) });
        rows.Add(new { label = "TSPDiagnosticsAvailablePatterns", value = FormatAvailablePatterns(selectedJunction.AvailablePatternSnapshots) });
        rows.Add(new { label = "TSPDiagnosticsExtraOptions", value = $"{FormatVisible(selectedJunction.ExtraOptionsVisible)}, {selectedJunction.ExtraOptionsReason}" });
        rows.Add(new { label = "TSPDiagnosticsOptionTurningOnRed", value = FormatOptionSnapshot(selectedJunction.TurningOnRed) });
        rows.Add(new { label = "TSPDiagnosticsOptionGiveWay", value = FormatOptionSnapshot(selectedJunction.GiveWayToOncomingVehicles) });
        rows.Add(new { label = "TSPDiagnosticsOptionExclusivePedestrian", value = FormatOptionSnapshot(selectedJunction.ExclusivePedestrianPhase) });
        rows.Add(new { label = "TSPDiagnosticsPedestrianDuration", value = FormatOptionSnapshot(selectedJunction.PedestrianDurationAdjustment) });
        rows.Add(new { label = "TSPDiagnosticsTramControl", value = FormatTspControlSnapshot(selectedJunction.TramTransitPriority) });
        rows.Add(new { label = "TSPDiagnosticsBusControl", value = FormatTspControlSnapshot(selectedJunction.BusTransitPriority) });

        if (EntityManager.TryGetComponent(entity, out TrafficGroupMember trafficGroupMember))
        {
            TrafficGroup trafficGroup = default;
            bool hasTrafficGroup = trafficGroupMember.m_GroupEntity != Entity.Null
                && EntityManager.TryGetComponent(trafficGroupMember.m_GroupEntity, out trafficGroup);
            rows.Add(new { label = "TSPDiagnosticsTrafficGroupRole", value = trafficGroupMember.m_IsGroupLeader ? "Leader" : "Follower" });
            rows.Add(new { label = "TSPDiagnosticsTrafficGroupMode", value = FormatTrafficGroupMode(hasTrafficGroup, trafficGroup) });
            rows.Add(new { label = "TSPDiagnosticsTrafficGroupCycleLength", value = hasTrafficGroup ? trafficGroup.m_CycleLength.ToString("0.##", CultureInfo.InvariantCulture) : "-" });
            rows.Add(new { label = "TSPDiagnosticsTrafficGroupSignalDelay", value = trafficGroupMember.m_SignalDelay.ToString(CultureInfo.InvariantCulture) });
            rows.Add(new { label = "TSPDiagnosticsTrafficGroupPhaseOffset", value = trafficGroupMember.m_PhaseOffset.ToString(CultureInfo.InvariantCulture) });
            rows.Add(new { label = "TSPDiagnosticsTrafficGroupMemberCycleTimer", value = trafficGroupMember.m_MemberCycleTimer.ToString("0.##", CultureInfo.InvariantCulture) });
            rows.Add(new { label = "TSPDiagnosticsTrafficGroupMasterPhase", value = FormatTrafficGroupMasterPhase(entity, hasTrafficGroup, trafficGroup, hasTrafficLights, trafficLights) });
            rows.Add(new { label = "TSPDiagnosticsTrafficGroupTspSuspended", value = "Yes" });
        }

        if (hasTrafficLights)
        {
            rows.Add(new { label = "TSPDiagnosticsSignalState", value = trafficLights.m_State.ToString() });
            rows.Add(new { label = "TSPDiagnosticsCurrentGroup", value = FormatByteValue(trafficLights.m_CurrentSignalGroup) });
            rows.Add(new { label = "TSPDiagnosticsNextGroup", value = FormatByteValue(trafficLights.m_NextSignalGroup) });
            rows.Add(new { label = "TSPDiagnosticsTimer", value = trafficLights.m_Timer.ToString(CultureInfo.InvariantCulture) });
            rows.Add(new { label = "TSPDiagnosticsSignalGroupCount", value = trafficLights.m_SignalGroupCount.ToString(CultureInfo.InvariantCulture) });
        }

        if (hasRuntimeDebug)
        {
            bool isTrackRequest = (global::TrafficLightsEnhancement.Logic.Tsp.TspSource)runtimeDebug.m_SourceType == global::TrafficLightsEnhancement.Logic.Tsp.TspSource.Track;
            bool isCurrentTargetGroup = hasTrafficLights
                && runtimeDebug.m_TargetSignalGroup == trafficLights.m_CurrentSignalGroup;
            bool isExtendingCurrentPhase = runtimeDebug.m_ExtendCurrentPhase && isCurrentTargetGroup;
            rows.Add(new { label = "TSPDiagnosticsRequest", value = GetTspRequestKindName(runtimeDebug.m_RequestKind) });
            rows.Add(new { label = "TSPDiagnosticsSource", value = GetTspSourceName(runtimeDebug.m_SourceType) });
            rows.Add(new { label = "TSPDiagnosticsTargetGroup", value = FormatByteValue(runtimeDebug.m_TargetSignalGroup) });
            rows.Add(new { label = "TSPDiagnosticsStrength", value = runtimeDebug.m_Strength.ToString("0.00", CultureInfo.InvariantCulture) });
            rows.Add(new { label = "TSPDiagnosticsExpiry", value = runtimeDebug.m_ExpiryTimer.ToString(CultureInfo.InvariantCulture) });
            rows.Add(new { label = "TSPDiagnosticsExtend", value = isExtendingCurrentPhase ? "Yes" : "No" });
            if (isTrackRequest)
            {
                rows.Add(new { label = "TSPDiagnosticsApproachRole", value = GetApproachLaneRoleName(runtimeDebug.m_ApproachLaneRole) });
                rows.Add(new { label = "TSPDiagnosticsCandidates", value = FormatCandidates(runtimeDebug) });
                rows.Add(new { label = "TSPDiagnosticsProbeSignaled", value = GetTrackProbeName(runtimeDebug.m_TrackSignaledLaneProbe) });
                rows.Add(new { label = "TSPDiagnosticsCurveSignaled", value = FormatCurvePosition(runtimeDebug.m_TrackSignaledLaneProbe, runtimeDebug.m_TrackSignaledLaneCurvePosition) });
                rows.Add(new { label = "TSPDiagnosticsProbeApproach", value = GetTrackProbeName(runtimeDebug.m_TrackApproachLaneProbe) });
                rows.Add(new { label = "TSPDiagnosticsCurveApproach", value = FormatCurvePosition(runtimeDebug.m_TrackApproachLaneProbe, runtimeDebug.m_TrackApproachLaneCurvePosition) });
                rows.Add(new { label = "TSPDiagnosticsProbeUpstream", value = GetTrackProbeName(runtimeDebug.m_TrackUpstreamLaneProbe) });
                rows.Add(new { label = "TSPDiagnosticsCurveUpstream", value = FormatCurvePosition(runtimeDebug.m_TrackUpstreamLaneProbe, runtimeDebug.m_TrackUpstreamLaneCurvePosition) });
                rows.Add(new { label = "TSPDiagnosticsIndexLanes", value = runtimeDebug.m_TramApproachIndexLaneCount.ToString(CultureInfo.InvariantCulture) });
                rows.Add(new { label = "TSPDiagnosticsSignaledLane", value = FormatEntity(runtimeDebug.m_TrackSignaledLaneEntity) });
                rows.Add(new { label = "TSPDiagnosticsApproachLane", value = FormatEntity(runtimeDebug.m_TrackApproachLaneEntity) });
                rows.Add(new { label = "TSPDiagnosticsUpstreamLane", value = FormatEntity(runtimeDebug.m_TrackUpstreamLaneEntity) });
                rows.Add(new { label = "TSPDiagnosticsSignaledOwner", value = FormatEntity(runtimeDebug.m_TrackSignaledLaneOwnerEntity) });
                rows.Add(new { label = "TSPDiagnosticsApproachOwner", value = FormatEntity(runtimeDebug.m_TrackApproachLaneOwnerEntity) });
                rows.Add(new { label = "TSPDiagnosticsUpstreamOwner", value = FormatEntity(runtimeDebug.m_TrackUpstreamLaneOwnerEntity) });
                rows.Add(new { label = "TSPDiagnosticsSiblingSamples", value = FormatSiblingSamples(runtimeDebug) });
                rows.Add(new { label = "TSPDiagnosticsMasterLanes", value = FormatMasterLanes(runtimeDebug) });
                rows.Add(new { label = "TSPDiagnosticsFallbackEdges", value = runtimeDebug.m_FallbackConnectedEdgeCount.ToString(CultureInfo.InvariantCulture) });
                rows.Add(new { label = "TSPDiagnosticsFallbackTramLanes", value = runtimeDebug.m_FallbackTramSublaneCount.ToString(CultureInfo.InvariantCulture) });
                rows.Add(new { label = "TSPDiagnosticsFallbackPathMatches", value = runtimeDebug.m_FallbackPathNodeMatchCount.ToString(CultureInfo.InvariantCulture) });
                rows.Add(new { label = "TSPDiagnosticsFallbackIndexHits", value = runtimeDebug.m_FallbackIndexHitCount.ToString(CultureInfo.InvariantCulture) });
                rows.Add(new { label = "TSPDiagnosticsFallbackBestCurve", value = FormatFallbackCurvePosition(runtimeDebug) });
            }
        }
        else
        {
            rows.Add(new { label = "TSPDiagnosticsRequest", value = "None" });
        }

        if (hasBusApproachDebug)
        {
            bool hasBusSample = busApproachDebug.m_BusHitCount > 0;
            rows.Add(new { label = "TSPDiagnosticsBusIndexLanes", value = busApproachDebug.m_BusApproachIndexLaneCount.ToString(CultureInfo.InvariantCulture) });
            rows.Add(new { label = "TSPDiagnosticsBusScannedSignalLanes", value = busApproachDebug.m_ScannedSignalLaneCount.ToString(CultureInfo.InvariantCulture) });
            rows.Add(new { label = "TSPDiagnosticsBusProbe", value = GetBusProbeName(busApproachDebug.m_BusProbe) });
            rows.Add(new
            {
                label = "TSPDiagnosticsBusDecision",
                value = GetBusDecisionName(busApproachDebug.m_BusDecision),
                valueLabel = busApproachDebug.m_BusDecision == TransitSignalPriorityBusDecision.SuppressedNoProgress
                    ? "TSPDiagnosticsBusDecisionSuppressedNoProgress"
                    : null
            });
            rows.Add(new { label = "TSPDiagnosticsBusNoProgressTicks", value = busApproachDebug.m_BusNoProgressTicks.ToString(CultureInfo.InvariantCulture) });
            if (busApproachDebug.m_BusTargetSignalGroup > 0)
            {
                rows.Add(new { label = "TSPDiagnosticsBusTargetGroup", value = FormatByteValue(busApproachDebug.m_BusTargetSignalGroup) });
            }

            if (hasBusSample)
            {
                rows.Add(new { label = "TSPDiagnosticsBusHitCount", value = busApproachDebug.m_BusHitCount.ToString(CultureInfo.InvariantCulture) });
                rows.Add(new { label = "TSPDiagnosticsBusLane", value = FormatEntity(busApproachDebug.m_BusLaneEntity) });
                rows.Add(new { label = "TSPDiagnosticsBusVehicle", value = FormatEntity(busApproachDebug.m_BusVehicleEntity) });
                rows.Add(new { label = "TSPDiagnosticsBusCurve", value = FormatBusCurvePosition(busApproachDebug) });
                rows.Add(new { label = "TSPDiagnosticsBusLaneType", value = busApproachDebug.m_BusLaneIsPublicOnly ? "Bus-only" : "Mixed" });
                rows.Add(new { label = "TSPDiagnosticsBusLaneChange", value = FormatBusLaneChange(busApproachDebug) });
                rows.Add(new { label = "TSPDiagnosticsBusSpeed", value = busApproachDebug.m_BusSpeed.ToString("0.00", CultureInfo.InvariantCulture) });
            }
        }

        if (hasDecisionTrace)
        {
            rows.Add(new { label = "TSPDiagnosticsDecision", value = GetTspDecisionReasonName(decisionTrace.m_Reason) });
            rows.Add(new { label = "TSPDiagnosticsBaseGroup", value = FormatByteValue(decisionTrace.m_BaseSignalGroup) });
            rows.Add(new { label = "TSPDiagnosticsSelectedGroup", value = FormatByteValue(decisionTrace.m_SelectedSignalGroup) });
            rows.Add(new { label = "TSPDiagnosticsDecisionTarget", value = FormatByteValue(decisionTrace.m_RequestTargetSignalGroup) });
            rows.Add(new { label = "TSPDiagnosticsDecisionSource", value = GetTspSourceName(decisionTrace.m_SourceType) });
            if ((global::TrafficLightsEnhancement.Logic.Tsp.TspSource)decisionTrace.m_SourceType == global::TrafficLightsEnhancement.Logic.Tsp.TspSource.PublicCar)
            {
                rows.Add(new { label = "TSPDiagnosticsBusPriorityMode", value = decisionTrace.m_OnDedicatedLane ? "Aggressive (bus lane)" : "Soft" });
            }
            if (HasPedestrianDecisionContext(decisionTrace))
            {
                rows.Add(new { label = "TSPDiagnosticsExclusivePedestrian", value = decisionTrace.m_ExclusivePedestrianEnabled ? "Yes" : "No" });
                rows.Add(new { label = "TSPDiagnosticsActivePedestrianProtection", value = decisionTrace.m_ActiveExclusivePedestrianPhase ? "Yes" : "No" });
                rows.Add(new { label = "TSPDiagnosticsPendingPedestrianFairness", value = decisionTrace.m_PendingPedestrianFairness ? $"G{FormatByteValue(decisionTrace.m_PendingPedestrianSignalGroup)}" : "No" });
            }
        }
        else
        {
            rows.Add(new { label = "TSPDiagnosticsDecision", value = "None" });
        }

        return new { summary, events, rows };
    }

    private (string label, string value) GetTspDiagnosticsSummary(
        TransitSignalPrioritySettings settings,
        bool hasTrafficLights,
        TrafficLights trafficLights,
        bool hasRuntimeDebug,
        TransitSignalPriorityRuntimeDebugInfo runtimeDebug,
        bool hasBusApproachDebug,
        TransitSignalPriorityBusApproachDebugInfo busApproachDebug,
        bool hasDecisionTrace,
        TransitSignalPriorityDecisionTrace decisionTrace)
    {
        return ("TSPDiagnosticsSummary", GetTspDiagnosticsSummaryValue(
            settings,
            hasTrafficLights,
            trafficLights,
            hasRuntimeDebug,
            runtimeDebug,
            hasBusApproachDebug,
            busApproachDebug,
            hasDecisionTrace,
            decisionTrace));
    }

    private string GetTspDiagnosticsSummaryValue(
        TransitSignalPrioritySettings settings,
        bool hasTrafficLights,
        TrafficLights trafficLights,
        bool hasRuntimeDebug,
        TransitSignalPriorityRuntimeDebugInfo runtimeDebug,
        bool hasBusApproachDebug,
        TransitSignalPriorityBusApproachDebugInfo busApproachDebug,
        bool hasDecisionTrace,
        TransitSignalPriorityDecisionTrace decisionTrace)
    {
        if (!settings.m_Enabled)
        {
            return "Disabled";
        }

        if (!hasRuntimeDebug)
        {
            if (hasBusApproachDebug && busApproachDebug.m_BusHitCount > 0)
            {
                return hasTrafficLights
                    ? $"No active request | bus {GetBusProbeName(busApproachDebug.m_BusProbe)} | G{FormatByteValue(trafficLights.m_CurrentSignalGroup)} -> G{FormatByteValue(trafficLights.m_NextSignalGroup)} | {trafficLights.m_State}"
                    : $"No active request | bus {GetBusProbeName(busApproachDebug.m_BusProbe)}";
            }

            return hasTrafficLights
                ? $"No request | G{FormatByteValue(trafficLights.m_CurrentSignalGroup)} -> G{FormatByteValue(trafficLights.m_NextSignalGroup)} | {trafficLights.m_State}"
                : "No request";
        }

        string request = GetTspRequestKindName(runtimeDebug.m_RequestKind);
        string source = GetTspSourceName(runtimeDebug.m_SourceType);
        string groups = hasTrafficLights
            ? $"G{FormatByteValue(trafficLights.m_CurrentSignalGroup)} -> G{FormatByteValue(trafficLights.m_NextSignalGroup)}"
            : "G? -> G?";
        string action = GetTspDiagnosticsAction(
            hasTrafficLights,
            trafficLights,
            runtimeDebug,
            hasDecisionTrace,
            decisionTrace);

        return $"{request} {source} target G{FormatByteValue(runtimeDebug.m_TargetSignalGroup)} | {groups} | {action}";
    }

    private static string GetTspDiagnosticsAction(
        bool hasTrafficLights,
        TrafficLights trafficLights,
        TransitSignalPriorityRuntimeDebugInfo runtimeDebug,
        bool hasDecisionTrace,
        TransitSignalPriorityDecisionTrace decisionTrace)
    {
        if (hasDecisionTrace)
        {
            return GetTspDecisionReasonName(decisionTrace.m_Reason);
        }

        if (!hasTrafficLights)
        {
            return "Waiting for signal data";
        }

        if (runtimeDebug.m_TargetSignalGroup == trafficLights.m_CurrentSignalGroup)
        {
            return runtimeDebug.m_ExtendCurrentPhase ? "Holding target group" : "Target already current";
        }

        if (runtimeDebug.m_TargetSignalGroup == trafficLights.m_NextSignalGroup)
        {
            return "Changing to target";
        }

        if (runtimeDebug.m_ExpiryTimer == 0)
        {
            return "Request expired";
        }

        return "Waiting to preempt";
    }

    private ArrayList GetTspDiagnosticsEvents(
        Entity entity,
        string summary,
        bool hasTrafficLights,
        TrafficLights trafficLights,
        bool hasRuntimeDebug,
        TransitSignalPriorityRuntimeDebugInfo runtimeDebug,
        bool hasBusApproachDebug,
        TransitSignalPriorityBusApproachDebugInfo busApproachDebug,
        bool hasDecisionTrace,
        TransitSignalPriorityDecisionTrace decisionTrace,
        SelectedJunctionDiagnosticsSnapshot selectedJunction)
    {
        PruneTspDiagnosticsEvents();

        object trafficGroupLockstepTrace =
            GetTrafficGroupLockstepTrace(entity);
        string trafficGroupLockstepSignature =
            JsonConvert.SerializeObject(trafficGroupLockstepTrace);
        string signature = GetTspDiagnosticsSignature(
            summary,
            hasTrafficLights,
            trafficLights,
            hasRuntimeDebug,
            runtimeDebug,
            hasBusApproachDebug,
            busApproachDebug,
            hasDecisionTrace,
            decisionTrace,
            trafficGroupLockstepSignature);

        bool isNewHistory = false;
        if (!m_TspDiagnosticsEvents.TryGetValue(entity, out TspDiagnosticsHistory history))
        {
            history = new TspDiagnosticsHistory();
            m_TspDiagnosticsEvents[entity] = history;
            isNewHistory = true;
        }

        bool selectionChanged = m_TspDiagnosticsSelectedEntity != entity;
        bool isNewSelection = selectionChanged || isNewHistory;
        m_TspDiagnosticsSelectedEntity = entity;

        bool signatureChanged = history.LastSignature != signature;
        bool hasTspActivity = hasRuntimeDebug || hasBusApproachDebug || hasDecisionTrace;
        bool shouldRecordEvent = isNewSelection
            || (signatureChanged
                && ShouldRecordTspDiagnosticsEvent(
                    history,
                    hasTspActivity,
                    EntityManager.HasComponent<TrafficGroupMember>(entity)));
        if (signatureChanged)
        {
            history.LastSignature = signature;
        }

        if (shouldRecordEvent)
        {
            WriteTspDiagnosticsTraceEvent(
                entity,
                summary,
                hasTrafficLights,
                trafficLights,
                hasRuntimeDebug,
                runtimeDebug,
                hasBusApproachDebug,
                busApproachDebug,
                hasDecisionTrace,
                decisionTrace,
                selectedJunction,
                trafficGroupLockstepTrace);
            RecordTspDiagnosticsEvent(history, summary);

            if (isNewSelection)
            {
                Mod.log.Info($"[TLE] Wrote initial TSP diagnostics trace for newly selected junction {entity.Index}:{entity.Version} (Simulation frame {m_SimulationSystem.frameIndex}).");
            }
        }

        var events = new ArrayList();
        foreach (TspDiagnosticsEvent diagnosticsEvent in history.Events)
        {
            string[] eventParts = diagnosticsEvent.Value.Split(new[] { " | " }, 2, StringSplitOptions.None);
            string eventTitle = eventParts[0];
            string eventDetail = eventParts.Length > 1 ? eventParts[1] : string.Empty;
            events.Add(new
            {
                sequence = diagnosticsEvent.Sequence,
                title = $"#{diagnosticsEvent.Sequence} {eventTitle}",
                detail = eventDetail
            });
        }

        return events;
    }

    private void PruneTspDiagnosticsEvents()
    {
        if (m_TspDiagnosticsEvents.Count == 0)
        {
            return;
        }

        foreach (Entity entity in m_TspDiagnosticsEvents.Keys.ToArray())
        {
            if (!EntityManager.Exists(entity))
            {
                m_TspDiagnosticsEvents.Remove(entity);
            }
        }
    }

    private static bool ShouldRecordTspDiagnosticsEvent(
        TspDiagnosticsHistory history,
        bool hasTspActivity,
        bool isTrafficGroupMember)
    {
        bool shouldRecord = DiagnosticsTracePolicy.ShouldRecordStateChange(
            hasTspActivity,
            history.HadTspActivity,
            isTrafficGroupMember);
        history.HadTspActivity = hasTspActivity;
        return shouldRecord;
    }

    private static string GetTspDiagnosticsSignature(
        string summary,
        bool hasTrafficLights,
        TrafficLights trafficLights,
        bool hasRuntimeDebug,
        TransitSignalPriorityRuntimeDebugInfo runtimeDebug,
        bool hasBusApproachDebug,
        TransitSignalPriorityBusApproachDebugInfo busApproachDebug,
        bool hasDecisionTrace,
        TransitSignalPriorityDecisionTrace decisionTrace,
        string trafficGroupLockstepSignature)
    {
        string trafficSignature = hasTrafficLights
            ? $"{trafficLights.m_State}:{trafficLights.m_CurrentSignalGroup}:{trafficLights.m_NextSignalGroup}"
            : "no-traffic";
        string requestSignature = hasRuntimeDebug
            ? $"{runtimeDebug.m_RequestKind}:{runtimeDebug.m_SourceType}:{runtimeDebug.m_TargetSignalGroup}:{runtimeDebug.m_TrackApproachLaneProbe}:{runtimeDebug.m_TrackApproachLaneCurvePosition:0.00}"
            : "no-request";
        string busSignature = hasBusApproachDebug
            ? $"{busApproachDebug.m_BusProbe}:{busApproachDebug.m_BusDecision}:{busApproachDebug.m_BusTargetSignalGroup}:{busApproachDebug.m_BusHitCount}:{busApproachDebug.m_BusLaneEntity.Index}:{busApproachDebug.m_BusCurvePosition:0.00}:{busApproachDebug.m_BusIsChangingLane}:{busApproachDebug.m_BusSpeed:0.00}:{busApproachDebug.m_BusNoProgressTicks}:{busApproachDebug.m_BusNoProgressSuppressed}"
            : "no-bus";
        string decisionSignature = hasDecisionTrace
            ? $"{decisionTrace.m_Reason}:{decisionTrace.m_BaseSignalGroup}:{decisionTrace.m_SelectedSignalGroup}:{decisionTrace.m_RequestTargetSignalGroup}:{decisionTrace.m_ExclusivePedestrianEnabled}:{decisionTrace.m_ActiveExclusivePedestrianPhase}:{decisionTrace.m_PendingPedestrianFairness}:{decisionTrace.m_PendingPedestrianSignalGroup}"
            : "no-decision";

        return $"{summary}|{trafficSignature}|{requestSignature}|{busSignature}|{decisionSignature}|{trafficGroupLockstepSignature}";
    }

    private void WriteTspDiagnosticsTraceEvent(
        Entity entity,
        string summary,
        bool hasTrafficLights,
        TrafficLights trafficLights,
        bool hasRuntimeDebug,
        TransitSignalPriorityRuntimeDebugInfo runtimeDebug,
        bool hasBusApproachDebug,
        TransitSignalPriorityBusApproachDebugInfo busApproachDebug,
        bool hasDecisionTrace,
        TransitSignalPriorityDecisionTrace decisionTrace,
        SelectedJunctionDiagnosticsSnapshot selectedJunction,
        object trafficGroupLockstepTrace)
    {
        try
        {
            string path = Path.Combine(Application.persistentDataPath, TspDiagnosticsTraceFileName);
            string line = JsonConvert.SerializeObject(new
            {
                timestampUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                simulationFrame = m_SimulationSystem.frameIndex,
                selectedEntity = new { index = entity.Index, version = entity.Version },
                signalConfiguration = GetTspSignalConfigurationTrace(entity),
                selectedJunction = selectedJunction.ToTraceObject(),
                trafficGroup = GetTspTrafficGroupTrace(entity),
                leaderTrafficLights = GetTspTrafficGroupLeaderTrace(entity),
                trafficGroupLockstep = trafficGroupLockstepTrace,
                laneSignals = GetTspLaneSignalTrace(entity, hasTrafficLights, trafficLights, hasRuntimeDebug, runtimeDebug),
                summary,
                trafficLights = hasTrafficLights
                    ? new
                    {
                        state = trafficLights.m_State.ToString(),
                        currentGroup = trafficLights.m_CurrentSignalGroup,
                        nextGroup = trafficLights.m_NextSignalGroup,
                        timer = trafficLights.m_Timer,
                        signalGroupCount = trafficLights.m_SignalGroupCount,
                        updateFrameIndex = GetUpdateFrameIndex(entity),
                    }
                    : null,
                request = hasRuntimeDebug
                    ? new
                    {
                        kind = GetTspRequestKindName(runtimeDebug.m_RequestKind),
                        source = GetTspSourceName(runtimeDebug.m_SourceType),
                        targetGroup = runtimeDebug.m_TargetSignalGroup,
                        strength = runtimeDebug.m_Strength,
                        expiry = runtimeDebug.m_ExpiryTimer,
                        extendCurrentPhase = runtimeDebug.m_ExtendCurrentPhase,
                        approachRole = GetApproachLaneRoleName(runtimeDebug.m_ApproachLaneRole),
                        candidates = FormatCandidates(runtimeDebug),
                        signaledProbe = GetTrackProbeName(runtimeDebug.m_TrackSignaledLaneProbe),
                        signaledCurve = runtimeDebug.m_TrackSignaledLaneCurvePosition,
                        approachProbe = GetTrackProbeName(runtimeDebug.m_TrackApproachLaneProbe),
                        approachCurve = runtimeDebug.m_TrackApproachLaneCurvePosition,
                        upstreamProbe = GetTrackProbeName(runtimeDebug.m_TrackUpstreamLaneProbe),
                        upstreamCurve = runtimeDebug.m_TrackUpstreamLaneCurvePosition,
                        indexedTramLanes = runtimeDebug.m_TramApproachIndexLaneCount,
                        signaledLane = FormatEntity(runtimeDebug.m_TrackSignaledLaneEntity),
                        approachLane = FormatEntity(runtimeDebug.m_TrackApproachLaneEntity),
                        upstreamLane = FormatEntity(runtimeDebug.m_TrackUpstreamLaneEntity),
                        signaledOwner = FormatEntity(runtimeDebug.m_TrackSignaledLaneOwnerEntity),
                        approachOwner = FormatEntity(runtimeDebug.m_TrackApproachLaneOwnerEntity),
                        upstreamOwner = FormatEntity(runtimeDebug.m_TrackUpstreamLaneOwnerEntity),
                        siblingSamples = FormatSiblingSamples(runtimeDebug),
                        masterLanes = FormatMasterLanes(runtimeDebug),
                        fallbackConnectedEdges = runtimeDebug.m_FallbackConnectedEdgeCount,
                        fallbackTramSublanes = runtimeDebug.m_FallbackTramSublaneCount,
                        fallbackPathNodeMatches = runtimeDebug.m_FallbackPathNodeMatchCount,
                        fallbackIndexHits = runtimeDebug.m_FallbackIndexHitCount,
                        fallbackBestCurve = runtimeDebug.m_FallbackBestCurvePosition,
                    }
                    : null,
                busApproach = hasBusApproachDebug
                    ? new
                    {
                        indexedBusLanes = busApproachDebug.m_BusApproachIndexLaneCount,
                        scannedSignalLanes = busApproachDebug.m_ScannedSignalLaneCount,
                        probe = GetBusProbeName(busApproachDebug.m_BusProbe),
                        decision = GetBusDecisionName(busApproachDebug.m_BusDecision),
                        noProgressTicks = busApproachDebug.m_BusNoProgressTicks,
                        noProgressSuppressed = busApproachDebug.m_BusNoProgressSuppressed,
                        targetGroup = busApproachDebug.m_BusTargetSignalGroup,
                        hitCount = busApproachDebug.m_BusHitCount,
                        lane = FormatEntity(busApproachDebug.m_BusLaneEntity),
                        vehicle = FormatEntity(busApproachDebug.m_BusVehicleEntity),
                        curve = busApproachDebug.m_BusCurvePosition,
                        laneType = busApproachDebug.m_BusLaneIsPublicOnly ? "Bus-only" : "Mixed",
                        laneChange = FormatBusLaneChange(busApproachDebug),
                        navigationLanes = busApproachDebug.m_BusHasNavigation ? (byte?)busApproachDebug.m_BusNavigationLaneCount : null,
                        speed = busApproachDebug.m_BusSpeed,
                        publicTransportState = busApproachDebug.m_BusPublicTransportState.ToString(),
                        vehicleLaneFlags = busApproachDebug.m_BusVehicleLaneFlags.ToString(),
                    }
                    : null,
                decision = hasDecisionTrace
                    ? new
                    {
                        reason = GetTspDecisionReasonName(decisionTrace.m_Reason),
                        baseGroup = decisionTrace.m_BaseSignalGroup,
                        selectedGroup = decisionTrace.m_SelectedSignalGroup,
                        targetGroup = decisionTrace.m_RequestTargetSignalGroup,
                        source = GetTspSourceName(decisionTrace.m_SourceType),
                        onDedicatedLane = decisionTrace.m_OnDedicatedLane,
                        exclusivePedestrianEnabled = decisionTrace.m_ExclusivePedestrianEnabled,
                        activeExclusivePedestrianPhase = decisionTrace.m_ActiveExclusivePedestrianPhase,
                        pendingPedestrianFairness = decisionTrace.m_PendingPedestrianFairness,
                        pendingPedestrianGroup = decisionTrace.m_PendingPedestrianSignalGroup,
                        pendingVehicleFairness = decisionTrace.m_PendingVehicleFairness,
                        pendingVehicleGroup = decisionTrace.m_PendingVehicleSignalGroup,
                        preemptionSuppressedByPedestrianPhase =
                            decisionTrace.m_Reason == (byte)global::TrafficLightsEnhancement.Logic.Tsp.TspSelectionReason.DeferredForPedestrianFairness
                            || decisionTrace.m_ActiveExclusivePedestrianPhase,
                        preemptionSuppressedByVehicleFairness =
                            decisionTrace.m_Reason == (byte)global::TrafficLightsEnhancement.Logic.Tsp.TspSelectionReason.DeferredForVehicleFairness,
                    }
                    : null
            });

            AppendTspDiagnosticsTraceLine(path, line);
        }
        catch (Exception ex)
        {
            Mod.log.Error(ex, $"[TLE] Failed to write TSP diagnostics trace for entity {entity.Index}:{entity.Version}: {ex.Message}");
        }
    }

    private SelectedJunctionDiagnosticsSnapshot GetSelectedJunctionDiagnosticsSnapshot(
        Entity entity,
        TransitSignalPrioritySettings tspSettings,
        bool includeDiagnosticsDetails)
    {
        NativeArray<NodeUtils.EdgeInfo> edgeInfoArray = m_EdgeInfoDictionary[entity];
        CustomTrafficLights customTrafficLights = EntityManager.TryGetComponent(entity, out CustomTrafficLights value)
            ? value
            : new CustomTrafficLights(CustomTrafficLights.Patterns.Vanilla);
        uint selectedPattern = (uint)customTrafficLights.GetPattern();
        uint patternOnly = (uint)customTrafficLights.GetPatternOnly();
        bool hasTrainTrack = NodeUtils.HasTrainTrack(edgeInfoArray);
        bool isTrafficGroupMember = EntityManager.HasComponent<TrafficGroupMember>(entity);
        bool splitPhasingSupported = PredefinedPatternsProcessor.IsValidPattern(edgeInfoArray, CustomTrafficLights.Patterns.SplitPhasing);
        bool protectedCentreTurnSupported = PredefinedPatternsProcessor.IsValidPattern(edgeInfoArray, CustomTrafficLights.Patterns.ProtectedCentreTurn);
        bool splitPhasingProtectedLeftSupported = PredefinedPatternsProcessor.IsValidPattern(edgeInfoArray, CustomTrafficLights.Patterns.SplitPhasingProtectedLeft);
        bool extraOptionsSupported = !hasTrainTrack && edgeInfoArray.Length <= 7;
        bool extraOptionsVisible = extraOptionsSupported && patternOnly < (uint)CustomTrafficLights.Patterns.ModDefault;
        bool hasCarLane = PredefinedPatternsProcessor.HasCarLane(edgeInfoArray);
        bool vehicleTurnOptionsVisible = PredefinedPatternsProcessor.IsVehicleTurnOptionVisible(extraOptionsVisible, hasCarLane);
        bool hasExclusivePedestrian = extraOptionsVisible && (selectedPattern & (uint)CustomTrafficLights.Patterns.ExclusivePedestrian) != 0;
        string extraOptionsReason = GetExtraOptionsReason(patternOnly, hasTrainTrack, edgeInfoArray.Length);
        string vehicleTurnOptionsReason = !extraOptionsVisible
            ? extraOptionsReason
            : (hasCarLane ? "Visible" : "No road vehicle lanes");
        string giveWayToOncomingVehiclesReason = !vehicleTurnOptionsVisible
            ? vehicleTurnOptionsReason
            : (patternOnly == (uint)CustomTrafficLights.Patterns.Vanilla ? "Visible" : "Pattern mode");
        bool isEditable = !isTrafficGroupMember;
        string tramStatusLabel = isTrafficGroupMember ? "TramTransitPriorityGroupedUnavailable" : null;
        string busStatusLabel = isTrafficGroupMember ? "BusTransitPriorityGroupedUnavailable" : null;

        int trainTrackCount = 0;
        int trackLaneLeftCount = 0;
        int trackLaneStraightCount = 0;
        int trackLaneRightCount = 0;
        int totalCarLaneCount = 0;
        foreach (var edge in edgeInfoArray)
        {
            trainTrackCount += edge.m_TrainTrackCount;
            trackLaneLeftCount += edge.m_TrackLaneLeftCount;
            trackLaneStraightCount += edge.m_TrackLaneStraightCount;
            trackLaneRightCount += edge.m_TrackLaneRightCount;
            totalCarLaneCount += edge.m_CarLaneLeftCount + edge.m_CarLaneStraightCount
                + edge.m_CarLaneRightCount + edge.m_CarLaneUTurnCount
                + edge.m_PublicCarLaneLeftCount + edge.m_PublicCarLaneStraightCount
                + edge.m_PublicCarLaneRightCount + edge.m_PublicCarLaneUTurnCount;
        }
        int totalTrackLaneCount = trackLaneLeftCount + trackLaneStraightCount + trackLaneRightCount;

        return new SelectedJunctionDiagnosticsSnapshot
        {
            ConnectedEdgeCount = edgeInfoArray.Length,
            HasTrainTrack = hasTrainTrack,
            HasTrackTurnLanes = includeDiagnosticsDetails && HasTrackTurnLanes(edgeInfoArray),
            IsQualifyingFourWay = includeDiagnosticsDetails && IsQualifyingFourWay(edgeInfoArray),
            IsComplexJunction = edgeInfoArray.Length > 7,
            TrainTrackCount = trainTrackCount,
            TrackLaneLeftCount = trackLaneLeftCount,
            TrackLaneStraightCount = trackLaneStraightCount,
            TrackLaneRightCount = trackLaneRightCount,
            TotalTrackLaneCount = totalTrackLaneCount,
            HasCarLane = hasCarLane,
            TotalCarLaneCount = totalCarLaneCount,
            EdgeSummaries = includeDiagnosticsDetails ? GetSelectedJunctionEdgeSummaries(edgeInfoArray) : [],
            SplitPhasingSupported = splitPhasingSupported,
            ProtectedCentreTurnSupported = protectedCentreTurnSupported,
            SplitPhasingProtectedLeftSupported = splitPhasingProtectedLeftSupported,
            AvailablePatternSnapshots = GetAvailablePatternSnapshots(
                splitPhasingSupported,
                protectedCentreTurnSupported,
                splitPhasingProtectedLeftSupported),
            ExtraOptionsSupported = extraOptionsSupported,
            ExtraOptionsVisible = extraOptionsVisible,
            ExtraOptionsReason = extraOptionsReason,
            TurningOnRed = new SelectedJunctionOptionSnapshot(
                "AllowTurningOnRed",
                ((uint)CustomTrafficLights.Patterns.AlwaysGreenKerbsideTurn).ToString(CultureInfo.InvariantCulture),
                vehicleTurnOptionsVisible,
                (selectedPattern & (uint)CustomTrafficLights.Patterns.AlwaysGreenKerbsideTurn) != 0,
                vehicleTurnOptionsReason),
            GiveWayToOncomingVehicles = new SelectedJunctionOptionSnapshot(
                "GiveWayToOncomingVehicles",
                ((uint)CustomTrafficLights.Patterns.CentreTurnGiveWay).ToString(CultureInfo.InvariantCulture),
                vehicleTurnOptionsVisible && patternOnly == (uint)CustomTrafficLights.Patterns.Vanilla,
                (selectedPattern & (uint)CustomTrafficLights.Patterns.CentreTurnGiveWay) != 0,
                giveWayToOncomingVehiclesReason),
            ExclusivePedestrianPhase = new SelectedJunctionOptionSnapshot(
                "ExclusivePedestrianPhase",
                ((uint)CustomTrafficLights.Patterns.ExclusivePedestrian).ToString(CultureInfo.InvariantCulture),
                extraOptionsVisible,
                hasExclusivePedestrian,
                extraOptionsVisible ? "Visible" : extraOptionsReason),
            PedestrianDurationAdjustment = new SelectedJunctionOptionSnapshot(
                "CustomPedestrianDurationMultiplier",
                "CustomPedestrianDurationMultiplier",
                hasExclusivePedestrian,
                false,
                hasExclusivePedestrian ? "Visible" : "Exclusive pedestrian phase disabled"),
            TramTransitPriority = new SelectedJunctionTspControlSnapshot(
                isVisible: HasTramTrack(edgeInfoArray),
                isEditable: isEditable,
                isChecked: tspSettings.m_Enabled && tspSettings.m_AllowTrackRequests,
                statusLabel: tramStatusLabel,
                reason: isTrafficGroupMember ? "Traffic group member" : "Editable"),
            BusTransitPriority = new SelectedJunctionTspControlSnapshot(
                isVisible: hasCarLane,
                isEditable: isEditable,
                isChecked: tspSettings.m_Enabled && tspSettings.m_AllowPublicCarRequests,
                statusLabel: busStatusLabel,
                reason: isTrafficGroupMember ? "Traffic group member" : "Editable"),
        };
    }

    private bool HasTramTrack(NativeArray<NodeUtils.EdgeInfo> edgeInfoArray)
    {
        var trackLaneLookup = GetComponentLookup<TrackLane>(true);
        var prefabRefLookup = GetComponentLookup<Game.Prefabs.PrefabRef>(true);
        var trackLaneDataLookup = GetComponentLookup<Game.Prefabs.TrackLaneData>(true);

        foreach (var edgeInfo in edgeInfoArray)
        {
            if (edgeInfo.m_SubLaneInfoList.IsCreated)
            {
                foreach (var subLaneInfo in edgeInfo.m_SubLaneInfoList)
                {
                    Entity laneEntity = subLaneInfo.m_SubLane;
                    if (laneEntity != Entity.Null && trackLaneLookup.HasComponent(laneEntity))
                    {
                        if (prefabRefLookup.TryGetComponent(laneEntity, out var prefabRef))
                        {
                            if (trackLaneDataLookup.TryGetComponent(prefabRef.m_Prefab, out var trackLaneData))
                            {
                                if ((trackLaneData.m_TrackTypes & Game.Net.TrackTypes.Tram) != 0)
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
        }

        return false;
    }

    private List<SelectedJunctionPatternSnapshot> GetAvailablePatternSnapshots(
        bool splitPhasingSupported,
        bool protectedCentreTurnSupported,
        bool splitPhasingProtectedLeftSupported)
    {
        var availablePatterns = new List<SelectedJunctionPatternSnapshot>
        {
            new("Vanilla", (uint)CustomTrafficLights.Patterns.Vanilla),
        };

        if (splitPhasingSupported)
        {
            availablePatterns.Add(new SelectedJunctionPatternSnapshot("SplitPhasing", (uint)CustomTrafficLights.Patterns.SplitPhasing));
        }

        if (protectedCentreTurnSupported)
        {
            string protectedTurnName = m_CityConfigurationSystem.leftHandTraffic ? "ProtectedRightTurns" : "ProtectedLeftTurns";
            availablePatterns.Add(new SelectedJunctionPatternSnapshot(protectedTurnName, (uint)CustomTrafficLights.Patterns.ProtectedCentreTurn));
        }

        if (splitPhasingProtectedLeftSupported)
        {
            availablePatterns.Add(new SelectedJunctionPatternSnapshot("SplitPhasingProtectedLeft", (uint)CustomTrafficLights.Patterns.SplitPhasingProtectedLeft));
        }

        availablePatterns.Add(new SelectedJunctionPatternSnapshot("CustomPhases", (uint)CustomTrafficLights.Patterns.CustomPhase));
        return availablePatterns;
    }

    private static ArrayList GetSelectedJunctionEdgeSummaries(NativeArray<NodeUtils.EdgeInfo> edgeInfoArray)
    {
        var edges = new ArrayList();
        foreach (NodeUtils.EdgeInfo edgeInfo in edgeInfoArray)
        {
            int carLaneCount =
                edgeInfo.m_CarLaneLeftCount
                + edgeInfo.m_CarLaneRightCount
                + edgeInfo.m_CarLaneStraightCount
                + edgeInfo.m_CarLaneUTurnCount;
            int publicCarLaneCount =
                edgeInfo.m_PublicCarLaneLeftCount
                + edgeInfo.m_PublicCarLaneRightCount
                + edgeInfo.m_PublicCarLaneStraightCount
                + edgeInfo.m_PublicCarLaneUTurnCount;
            int trackLaneCount =
                edgeInfo.m_TrackLaneLeftCount
                + edgeInfo.m_TrackLaneRightCount
                + edgeInfo.m_TrackLaneStraightCount;

            edges.Add(new
            {
                edge = FormatEntity(edgeInfo.m_Edge),
                position = edgeInfo.m_Position.ToString(),
                carLanes = carLaneCount,
                publicCarLanes = publicCarLaneCount,
                trackLanes = trackLaneCount,
                trainTracks = edgeInfo.m_TrainTrackCount,
                bicycleLanes = edgeInfo.m_BicycleLaneCount,
                pedestrianStopLineLanes = edgeInfo.m_PedestrianLaneStopLineCount,
                pedestrianNonStopLineLanes = edgeInfo.m_PedestrianLaneNonStopLineCount,
            });
        }

        return edges;
    }

    private static bool HasTrackTurnLanes(NativeArray<NodeUtils.EdgeInfo> edgeInfoArray)
    {
        foreach (NodeUtils.EdgeInfo edgeInfo in edgeInfoArray)
        {
            if (edgeInfo.m_TrackLaneLeftCount + edgeInfo.m_TrackLaneRightCount > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsQualifyingFourWay(NativeArray<NodeUtils.EdgeInfo> edgeInfoArray)
    {
        int ways = 0;
        foreach (NodeUtils.EdgeInfo edgeInfo in edgeInfoArray)
        {
            if (edgeInfo.m_CarLaneStraightCount + edgeInfo.m_PublicCarLaneStraightCount + edgeInfo.m_TrackLaneStraightCount > 0)
            {
                ways++;
            }
        }

        return ways == 4 && edgeInfoArray.Length == ways;
    }

    private static string GetExtraOptionsReason(uint patternOnly, bool hasTrainTrack, int connectedEdgeCount)
    {
        if (hasTrainTrack)
        {
            return "Train track topology";
        }

        if (connectedEdgeCount > 7)
        {
            return "Too many connected edges";
        }

        if (patternOnly >= (uint)CustomTrafficLights.Patterns.ModDefault)
        {
            return "Pattern mode";
        }

        return "Supported";
    }

    private object GetTspSignalConfigurationTrace(Entity entity)
    {
        CustomTrafficLights customTrafficLights = EntityManager.TryGetComponent(entity, out CustomTrafficLights value)
            ? value
            : new CustomTrafficLights(CustomTrafficLights.Patterns.Vanilla);
        CustomTrafficLights.Patterns pattern = customTrafficLights.GetPattern();
        CustomTrafficLights.Patterns patternOnly = customTrafficLights.GetPatternOnly();
        CustomTrafficLights.TrafficMode mode = customTrafficLights.GetMode();
        CustomTrafficLights.TrafficOptions options = customTrafficLights.GetOptions();

        return new
        {
            pattern = (uint)pattern,
            patternName = pattern.ToString(),
            patternOnly = (uint)patternOnly,
            patternOnlyName = patternOnly.ToString(),
            mode = mode.ToString(),
            options = (uint)options,
            optionsName = options.ToString(),
            exclusivePedestrian = (pattern & CustomTrafficLights.Patterns.ExclusivePedestrian) != 0,
            smartPhaseSelection = (options & CustomTrafficLights.TrafficOptions.SmartPhaseSelection) != 0,
            pedestrianDurationMultiplier = customTrafficLights.m_PedestrianPhaseDurationMultiplier,
            pedestrianPhaseGroupMask = customTrafficLights.m_PedestrianPhaseGroupMask,
        };
    }

    private object GetTspTrafficGroupTrace(Entity entity)
    {
        if (!EntityManager.TryGetComponent(entity, out TrafficGroupMember member))
        {
            return new
            {
                isMember = false,
                isLeader = false,
                role = "Standalone",
                mode = "Standalone",
                groupEntity = FormatEntity(Entity.Null),
                leaderEntity = FormatEntity(Entity.Null),
                isCoordinated = false,
                greenWaveEnabled = false,
                tspSuspended = false,
                cycleLength = 0f,
                signalDelay = 0,
                phaseOffset = 0,
                memberCycleTimer = 0f,
                distanceToLeader = 0f,
                masterPhase = (byte)0,
                masterNextPhase = (byte)0,
                masterState = TrafficLightState.None.ToString(),
                masterTimer = (byte)0,
                masterCustomTimer = 0u,
                masterSignalGroupCount = (byte)0,
            };
        }

        TrafficGroup group = default;
        bool hasGroup = member.m_GroupEntity != Entity.Null
            && EntityManager.TryGetComponent(member.m_GroupEntity, out group);
        return new
        {
            isMember = true,
            isLeader = member.m_IsGroupLeader,
            role = member.m_IsGroupLeader ? "Leader" : "Follower",
            mode = FormatTrafficGroupMode(hasGroup, group),
            groupEntity = FormatEntity(member.m_GroupEntity),
            leaderEntity = FormatEntity(member.m_LeaderEntity),
            isCoordinated = hasGroup && group.m_IsCoordinated,
            greenWaveEnabled = hasGroup && group.m_GreenWaveEnabled,
            tspSuspended = true,
            cycleLength = group.m_CycleLength,
            signalDelay = member.m_SignalDelay,
            phaseOffset = member.m_PhaseOffset,
            memberCycleTimer = member.m_MemberCycleTimer,
            distanceToLeader = member.m_DistanceToLeader,
            masterPhase = group.m_MasterPhase,
            masterNextPhase = group.m_MasterNextPhase,
            masterState = group.m_MasterState.ToString(),
            masterTimer = group.m_MasterTimer,
            masterCustomTimer = group.m_MasterCustomTimer,
            masterSignalGroupCount = group.m_MasterSignalGroupCount,
        };
    }

    private object GetTspTrafficGroupLeaderTrace(Entity entity)
    {
        if (!EntityManager.TryGetComponent(entity, out TrafficGroupMember member))
        {
            return null;
        }

        Entity leaderEntity = member.m_IsGroupLeader ? entity : member.m_LeaderEntity;
        if (leaderEntity == Entity.Null
            || !EntityManager.TryGetComponent(leaderEntity, out TrafficLights leaderLights))
        {
            return new
            {
                entity = FormatEntity(leaderEntity),
                available = false,
            };
        }

        uint? customTimer = EntityManager.TryGetComponent(
            leaderEntity,
            out CustomTrafficLights leaderCustomLights)
            ? leaderCustomLights.m_Timer
            : null;
        return new
        {
            entity = FormatEntity(leaderEntity),
            available = true,
            state = leaderLights.m_State.ToString(),
            currentGroup = leaderLights.m_CurrentSignalGroup,
            nextGroup = leaderLights.m_NextSignalGroup,
            timer = leaderLights.m_Timer,
            customTimer,
            signalGroupCount = leaderLights.m_SignalGroupCount,
            updateFrameIndex = GetUpdateFrameIndex(leaderEntity),
        };
    }

    private uint? GetUpdateFrameIndex(Entity entity)
    {
        return entity != Entity.Null
            && EntityManager.TryGetSharedComponent<UpdateFrame>(entity, out var updateFrame)
                ? updateFrame.m_Index
                : null;
    }

    private static string FormatTrafficGroupMode(bool hasGroup, TrafficGroup group)
    {
        if (!hasGroup)
        {
            return "Missing group";
        }

        if (!group.m_IsCoordinated)
        {
            return "Not coordinated";
        }

        return group.m_GreenWaveEnabled ? "Green wave" : "Lockstep";
    }

    private string FormatTrafficGroupMasterPhase(
        Entity selectedEntity,
        bool hasGroup,
        TrafficGroup group,
        bool hasTrafficLights,
        TrafficLights trafficLights)
    {
        if (!hasGroup)
        {
            return "-";
        }

        string leaderState =
            $"leader G{FormatByteValue(group.m_MasterPhase)} -> G{FormatByteValue(group.m_MasterNextPhase)}";
        if (!EntityManager.TryGetComponent(
                selectedEntity,
                out TrafficGroupPhaseMapping phaseMapping)
            || !phaseMapping.m_Map.IsComplete
            || !phaseMapping.m_Map.TryMapLeaderToMember(
                group.m_MasterPhase,
                out int mappedCurrentPhase)
            || (group.m_MasterNextPhase != 0
                && !phaseMapping.m_Map.TryMapLeaderToMember(
                    group.m_MasterNextPhase,
                    out _)))
        {
            return $"Movement mapping unavailable; follower held; {leaderState}";
        }

        int mappedNextPhase = group.m_MasterNextPhase == 0
            ? 0
            : MapDiagnosticLeaderPhase(phaseMapping, group.m_MasterNextPhase);
        bool identityMapping = phaseMapping.m_Map.LeaderPhaseCount
            == phaseMapping.m_Map.MemberPhaseCount;
        for (int leaderPhase = 1;
             identityMapping && leaderPhase <= phaseMapping.m_Map.LeaderPhaseCount;
             leaderPhase++)
        {
            identityMapping = phaseMapping.m_Map.TryMapLeaderToMember(
                                  leaderPhase,
                                  out int memberPhase)
                              && memberPhase == leaderPhase;
        }

        string mappingKind = identityMapping ? "Identity mapping" : "Movement mapping";
        string actualMemberState = hasTrafficLights
            ? $"; actual member G{FormatByteValue(trafficLights.m_CurrentSignalGroup)} -> G{FormatByteValue(trafficLights.m_NextSignalGroup)}"
            : string.Empty;
        return $"{mappingKind}; leader G{FormatByteValue(group.m_MasterPhase)} -> member G{mappedCurrentPhase}; next leader G{FormatByteValue(group.m_MasterNextPhase)} -> member G{FormatByteValue((byte)mappedNextPhase)}{actualMemberState}; {group.m_MasterState}; timer {group.m_MasterTimer}; custom {group.m_MasterCustomTimer}";
    }

    private static int MapDiagnosticLeaderPhase(
        TrafficGroupPhaseMapping phaseMapping,
        int leaderPhase)
    {
        phaseMapping.m_Map.TryMapLeaderToMember(leaderPhase, out int memberPhase);
        return memberPhase;
    }

    private ArrayList GetTspLaneSignalTrace(
        Entity entity,
        bool hasTrafficLights,
        TrafficLights trafficLights,
        bool hasRuntimeDebug,
        TransitSignalPriorityRuntimeDebugInfo runtimeDebug)
    {
        var lanes = new ArrayList();
        if (!EntityManager.TryGetBuffer<SubLane>(entity, true, out var subLaneBuffer))
        {
            return lanes;
        }

        byte currentGroup = hasTrafficLights ? trafficLights.m_CurrentSignalGroup : (byte)0;
        byte nextGroup = hasTrafficLights ? trafficLights.m_NextSignalGroup : (byte)0;
        byte requestedGroup = hasRuntimeDebug ? runtimeDebug.m_TargetSignalGroup : (byte)0;
        ushort currentMask = ToGroupBit(currentGroup);
        ushort nextMask = ToGroupBit(nextGroup);
        ushort requestedMask = ToGroupBit(requestedGroup);

        foreach (var subLane in subLaneBuffer)
        {
            Entity subLaneEntity = subLane.m_SubLane;
            if (!EntityManager.TryGetComponent(subLaneEntity, out LaneSignal laneSignal))
            {
                continue;
            }

            Entity ownerEntity = Entity.Null;
            if (EntityManager.TryGetComponent(subLaneEntity, out Owner owner))
            {
                ownerEntity = owner.m_Owner;
            }

            ushort yieldGroupMask = 0;
            ushort ignorePriorityGroupMask = 0;
            if (EntityManager.TryGetComponent(subLaneEntity, out ExtraLaneSignal extraLaneSignal))
            {
                yieldGroupMask = extraLaneSignal.m_YieldGroupMask;
                ignorePriorityGroupMask = extraLaneSignal.m_IgnorePriorityGroupMask;
            }

            lanes.Add(new
            {
                lane = FormatEntity(subLaneEntity),
                owner = FormatEntity(ownerEntity),
                groupMask = laneSignal.m_GroupMask,
                yieldGroupMask,
                ignorePriorityGroupMask,
                signal = laneSignal.m_Signal.ToString(),
                defaultSignal = laneSignal.m_Default.ToString(),
                flags = laneSignal.m_Flags.ToString(),
                petitioner = FormatEntity(laneSignal.m_Petitioner),
                blocker = FormatEntity(laneSignal.m_Blocker),
                priority = laneSignal.m_Priority,
                defaultPriority = laneSignal.m_Default,
                currentGroupActive = currentMask != 0 && (laneSignal.m_GroupMask & currentMask) != 0,
                currentGroupYield = currentMask != 0 && (yieldGroupMask & currentMask) != 0,
                nextGroupActive = nextMask != 0 && (laneSignal.m_GroupMask & nextMask) != 0,
                requestedGroupActive = requestedMask != 0 && (laneSignal.m_GroupMask & requestedMask) != 0,
                requestedGroupYield = requestedMask != 0 && (yieldGroupMask & requestedMask) != 0,
            });
        }

        return lanes;
    }

    private static ushort ToGroupBit(byte signalGroup)
    {
        return signalGroup is > 0 and <= 16
            ? (ushort)(1 << (signalGroup - 1))
            : (ushort)0;
    }

    private static void AppendTspDiagnosticsTraceLine(string path, string line)
    {
        lock (TspDiagnosticsTraceFileLock)
        {
            RotateTspDiagnosticsTraceFileIfNeeded(path);
            using FileStream stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            using StreamWriter writer = new StreamWriter(stream);
            writer.WriteLine(line);
        }
    }

    private static void RotateTspDiagnosticsTraceFileIfNeeded(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length < TspDiagnosticsTraceMaxBytes)
        {
            return;
        }

        string directory = file.DirectoryName ?? Application.persistentDataPath;
        string rotatedPath = Path.Combine(
            directory,
            $"{Path.GetFileNameWithoutExtension(file.Name)}.{DateTime.UtcNow:yyyyMMddHHmmss}{file.Extension}");
        File.Move(path, rotatedPath);
        PruneTspDiagnosticsTraceFiles(directory, Path.GetFileNameWithoutExtension(file.Name), file.Extension);
    }

    private static void PruneTspDiagnosticsTraceFiles(string directory, string baseName, string extension)
    {
        DirectoryInfo directoryInfo = new DirectoryInfo(directory);
        if (!directoryInfo.Exists)
        {
            return;
        }

        foreach (FileInfo rotatedFile in directoryInfo
            .GetFiles($"{baseName}.*{extension}")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Skip(TspDiagnosticsTraceMaxRotatedFiles))
        {
            try
            {
                rotatedFile.Delete();
            }
            catch (Exception ex)
            {
                Mod.log.Warn($"Failed to delete old TSP diagnostics trace file {rotatedFile.Name}: {ex.Message}");
            }
        }
    }

    private static void RecordTspDiagnosticsEvent(TspDiagnosticsHistory history, string value)
    {
        history.Sequence++;
        history.Events.Insert(0, new TspDiagnosticsEvent(history.Sequence, value));
        if (history.Events.Count > TspDiagnosticsEventHistoryLimit)
        {
            history.Events.RemoveAt(history.Events.Count - 1);
        }
    }

    private static string FormatByteValue(byte value) => value > 0 ? value.ToString(CultureInfo.InvariantCulture) : "-";

    private static string FormatSelectedJunctionTopology(SelectedJunctionDiagnosticsSnapshot selectedJunction)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "edges={0}, train={1}, track turns={2}, four-way={3}, complex={4}, trainTrackCount={5}, trackLaneStraightCount={6}, trackLaneLeftCount={7}, trackLaneRightCount={8}, totalTrackLaneCount={9}",
            selectedJunction.ConnectedEdgeCount,
            FormatYesNo(selectedJunction.HasTrainTrack),
            FormatYesNo(selectedJunction.HasTrackTurnLanes),
            FormatYesNo(selectedJunction.IsQualifyingFourWay),
            FormatYesNo(selectedJunction.IsComplexJunction),
            selectedJunction.TrainTrackCount,
            selectedJunction.TrackLaneStraightCount,
            selectedJunction.TrackLaneLeftCount,
            selectedJunction.TrackLaneRightCount,
            selectedJunction.TotalTrackLaneCount);
    }

    private static string FormatAvailablePatterns(IEnumerable<SelectedJunctionPatternSnapshot> availablePatterns)
    {
        return string.Join(", ", availablePatterns.Select(pattern => pattern.Name));
    }

    private static string FormatOptionSnapshot(SelectedJunctionOptionSnapshot option)
    {
        return $"{FormatVisible(option.IsVisible)}, checked={FormatYesNo(option.IsChecked)}, {option.Reason}";
    }

    private static string FormatTspControlSnapshot(SelectedJunctionTspControlSnapshot control)
    {
        return $"{FormatVisible(control.IsVisible)}, editable={FormatYesNo(control.IsEditable)}, checked={FormatYesNo(control.IsChecked)}, {control.Reason}";
    }

    private static string FormatVisible(bool isVisible) => isVisible ? "Visible" : "Hidden";

    private static string FormatYesNo(bool value) => value ? "Yes" : "No";

    private static string FormatCurvePosition(TransitSignalPriorityTrackProbeResult probeResult, float value)
    {
        return probeResult == TransitSignalPriorityTrackProbeResult.None || probeResult == TransitSignalPriorityTrackProbeResult.NoTramSamples
            ? "-"
            : value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static string FormatBusCurvePosition(TransitSignalPriorityBusApproachDebugInfo busApproachDebug)
    {
        return busApproachDebug.m_BusProbe == TransitSignalPriorityBusProbeResult.None
            || busApproachDebug.m_BusProbe == TransitSignalPriorityBusProbeResult.NoBusSamples
            ? "-"
            : busApproachDebug.m_BusCurvePosition.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static string FormatFallbackCurvePosition(TransitSignalPriorityRuntimeDebugInfo runtimeDebug)
    {
        return runtimeDebug.m_FallbackIndexHitCount > 0
            ? runtimeDebug.m_FallbackBestCurvePosition.ToString("0.00", CultureInfo.InvariantCulture)
            : "-";
    }

    private static string FormatEntity(Entity entity) => entity != Entity.Null ? $"{entity.Index}:{entity.Version}" : "-";

    private static string FormatSiblingSamples(TransitSignalPriorityRuntimeDebugInfo runtimeDebug)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "S:{0} A:{1} U:{2}",
            runtimeDebug.m_TrackSignaledSiblingSampleCount,
            runtimeDebug.m_TrackApproachSiblingSampleCount,
            runtimeDebug.m_TrackUpstreamSiblingSampleCount);
    }

    private static string FormatMasterLanes(TransitSignalPriorityRuntimeDebugInfo runtimeDebug)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "S:{0} A:{1} U:{2}",
            runtimeDebug.m_TrackSignaledLaneIsMaster ? "Y" : "N",
            runtimeDebug.m_TrackApproachLaneIsMaster ? "Y" : "N",
            runtimeDebug.m_TrackUpstreamLaneIsMaster ? "Y" : "N");
    }

    private static string FormatCandidates(TransitSignalPriorityRuntimeDebugInfo runtimeDebug)
    {
        var candidates = new List<string>();
        if (runtimeDebug.m_HasEarlyCandidate) candidates.Add("Early");
        if (runtimeDebug.m_HasPetitionerCandidate) candidates.Add("Petitioner");
        if (runtimeDebug.m_HadExistingRequest) candidates.Add("Existing");
        return candidates.Count > 0 ? string.Join(", ", candidates) : "None";
    }

    private static string FormatBusLaneChange(TransitSignalPriorityBusApproachDebugInfo busApproachDebug)
    {
        return busApproachDebug.m_BusIsChangingLane
            ? $"Yes ({busApproachDebug.m_BusChangeProgress.ToString("0.00", CultureInfo.InvariantCulture)})"
            : "No";
    }

    private static string GetTspRequestKindName(TransitSignalPriorityRequestKind value) => value switch
    {
        TransitSignalPriorityRequestKind.FreshEarly => "Early",
        TransitSignalPriorityRequestKind.FreshPetitioner => "Petitioner",
        TransitSignalPriorityRequestKind.LatchedExisting => "Latched",
        _ => "None"
    };

    private static string GetApproachLaneRoleName(TransitSignalPriorityApproachLaneRole value) => value switch
    {
        TransitSignalPriorityApproachLaneRole.ApproachLane => "Approach lane",
        TransitSignalPriorityApproachLaneRole.UpstreamLane => "Upstream lane",
        _ => "None"
    };

    private static string GetTspSourceName(byte value) => ((global::TrafficLightsEnhancement.Logic.Tsp.TspSource)value) switch
    {
        global::TrafficLightsEnhancement.Logic.Tsp.TspSource.Track => "Track",
        global::TrafficLightsEnhancement.Logic.Tsp.TspSource.PublicCar => "Bus",
        _ => "None"
    };

    private static bool HasPedestrianDecisionContext(TransitSignalPriorityDecisionTrace decisionTrace)
    {
        return decisionTrace.m_ExclusivePedestrianEnabled
            || decisionTrace.m_ActiveExclusivePedestrianPhase
            || decisionTrace.m_PendingPedestrianFairness;
    }

    private static string GetTrackProbeName(TransitSignalPriorityTrackProbeResult value) => value switch
    {
        TransitSignalPriorityTrackProbeResult.NoTramSamples => "No tram samples",
        TransitSignalPriorityTrackProbeResult.BelowThreshold => "Below threshold",
        TransitSignalPriorityTrackProbeResult.MatchOnApproachLane => "Approach lane match",
        TransitSignalPriorityTrackProbeResult.MatchOnUpstreamLane => "Upstream lane match",
        TransitSignalPriorityTrackProbeResult.MatchOnConnectedApproachLane => "Connected approach match",
        _ => "None"
    };

    private static string GetBusProbeName(TransitSignalPriorityBusProbeResult value) => value switch
    {
        TransitSignalPriorityBusProbeResult.NoBusSamples => "No bus samples",
        TransitSignalPriorityBusProbeResult.MatchOnSignaledLane => "Signaled lane match",
        TransitSignalPriorityBusProbeResult.MatchOnApproachLane => "Approach lane match",
        TransitSignalPriorityBusProbeResult.MatchOnConnectedApproachLane => "Connected approach match",
        _ => "None"
    };

    private static string GetBusDecisionName(TransitSignalPriorityBusDecision value) => value switch
    {
        TransitSignalPriorityBusDecision.NoEligibleSample => "No eligible bus sample",
        TransitSignalPriorityBusDecision.PriorityDisabled => "Bus priority disabled",
        TransitSignalPriorityBusDecision.RequestEmitted => "Request emitted",
        TransitSignalPriorityBusDecision.SuppressedBoarding => "Suppressed: boarding",
        TransitSignalPriorityBusDecision.SuppressedNearSideStop => "Suppressed: near-side stop",
        TransitSignalPriorityBusDecision.SuppressedUnknownStopRelation => "Suppressed: stop relation unknown",
        TransitSignalPriorityBusDecision.SuppressedAmbiguousLaneChange => "Suppressed: lane change ambiguous",
        TransitSignalPriorityBusDecision.SuppressedNoProgress => "Suppressed: no progress",
        _ => "None"
    };

    private static string GetTspDecisionReasonName(byte value) => ((global::TrafficLightsEnhancement.Logic.Tsp.TspSelectionReason)value) switch
    {
        global::TrafficLightsEnhancement.Logic.Tsp.TspSelectionReason.ExtendedCurrentPhase => "Extended current phase",
        global::TrafficLightsEnhancement.Logic.Tsp.TspSelectionReason.SelectedTargetPhase => "Selected target phase",
        global::TrafficLightsEnhancement.Logic.Tsp.TspSelectionReason.DeferredForPedestrianFairness => "Deferred for pedestrian phase",
        global::TrafficLightsEnhancement.Logic.Tsp.TspSelectionReason.DeferredForVehicleFairness => "Deferred for vehicle phase",
        _ => "None"
    };

    protected void CallMainPanelUpdatePosition(string jsonString)
    {
        m_MainPanelPosition = JsonConvert.DeserializeObject<UITypes.ScreenPoint>(jsonString);
        m_MainPanelBinding.Update();
    }

    protected void SavePanel()
    {
        SaveSelectedEntity();
    }

    protected void ExitPanel()
    {
        ChangeSelectedEntity(Entity.Null);
        m_MainPanelBinding.Update();
    }

    protected void ResetLaneDirectionTool()
    {
        if (m_SelectedEntity != Entity.Null)
        {
            EntityManager.RemoveComponent<CommonLibraries.LaneSystem.CustomLaneDirection>(m_SelectedEntity);
            m_MainPanelBinding.Update();
        }
    }

    protected void CallAddCustomPhase(string input)
    {
        if (!m_SelectedEntity.Equals(Entity.Null))
        {
            DynamicBuffer<CustomPhaseData> customPhaseDataBuffer;
            if (!EntityManager.TryGetBuffer(m_SelectedEntity, false, out customPhaseDataBuffer))
            {
                customPhaseDataBuffer = EntityManager.AddBuffer<CustomPhaseData>(m_SelectedEntity);
            }
            customPhaseDataBuffer.Add(new CustomPhaseData());
            UpdateActiveEditingCustomPhaseIndex(customPhaseDataBuffer.Length - 1);
            m_MainPanelBinding.Update();
            UpdateEdgeInfo(m_SelectedEntity);
            UpdateEntity();
        }
    }

    protected void CallRemoveCustomPhase(string input)
    {
        var definition = new { index = 0 };
        var value = JsonConvert.DeserializeAnonymousType(input, definition);
        if (!m_SelectedEntity.Equals(Entity.Null))
        {
            DynamicBuffer<CustomPhaseData> customPhaseDataBuffer;
            if (!EntityManager.TryGetBuffer(m_SelectedEntity, false, out customPhaseDataBuffer))
            {
                customPhaseDataBuffer = EntityManager.AddBuffer<CustomPhaseData>(m_SelectedEntity);
            }
            customPhaseDataBuffer.RemoveAt(value.index);

            DynamicBuffer<EdgeGroupMask> edgeGroupMaskBuffer;
            DynamicBuffer<SubLaneGroupMask> subLaneGroupMaskBuffer;
            if (!EntityManager.TryGetBuffer(m_SelectedEntity, false, out edgeGroupMaskBuffer))
            {
                edgeGroupMaskBuffer = EntityManager.AddBuffer<EdgeGroupMask>(m_SelectedEntity);
            }
            if (!EntityManager.TryGetBuffer(m_SelectedEntity, false, out subLaneGroupMaskBuffer))
            {
                subLaneGroupMaskBuffer = EntityManager.AddBuffer<SubLaneGroupMask>(m_SelectedEntity);
            }
            for (int i = value.index; i < 16; i++)
            {
                CustomPhaseUtils.SwapBit(subLaneGroupMaskBuffer, i, i + 1);
                CustomPhaseUtils.SwapBit(edgeGroupMaskBuffer, i, i + 1);
            }

            if (m_ActiveEditingCustomPhaseIndexBinding.Value >= customPhaseDataBuffer.Length)
            {
                UpdateActiveEditingCustomPhaseIndex(customPhaseDataBuffer.Length - 1);
            }

            m_MainPanelBinding.Update();
            UpdateEdgeInfo(m_SelectedEntity);

            UpdateEntity();
        }
    }
    
    protected void CallSwapCustomPhase(string input)
    {
        var definition = new { index1 = 0, index2 = 0 };
        var value = JsonConvert.DeserializeAnonymousType(input, definition);
        if (!m_SelectedEntity.Equals(Entity.Null))
        {
            DynamicBuffer<CustomPhaseData> customPhaseDataBuffer;
            if (!EntityManager.TryGetBuffer(m_SelectedEntity, false, out customPhaseDataBuffer))
            {
                customPhaseDataBuffer = EntityManager.AddBuffer<CustomPhaseData>(m_SelectedEntity);
            }
            (customPhaseDataBuffer[value.index2], customPhaseDataBuffer[value.index1]) = (customPhaseDataBuffer[value.index1], customPhaseDataBuffer[value.index2]);

            DynamicBuffer<EdgeGroupMask> edgeGroupMaskBuffer;
            if (!EntityManager.TryGetBuffer(m_SelectedEntity, false, out edgeGroupMaskBuffer))
            {
                edgeGroupMaskBuffer = EntityManager.AddBuffer<EdgeGroupMask>(m_SelectedEntity);
            }
            CustomPhaseUtils.SwapBit(edgeGroupMaskBuffer, value.index1, value.index2);

            DynamicBuffer<SubLaneGroupMask> subLaneGroupMaskBuffer;
            if (!EntityManager.TryGetBuffer(m_SelectedEntity, false, out subLaneGroupMaskBuffer))
            {
                subLaneGroupMaskBuffer = EntityManager.AddBuffer<SubLaneGroupMask>(m_SelectedEntity);
            }
            CustomPhaseUtils.SwapBit(subLaneGroupMaskBuffer, value.index1, value.index2);

            UpdateActiveEditingCustomPhaseIndex(value.index2);
            m_MainPanelBinding.Update();
            UpdateEdgeInfo(m_SelectedEntity);

            UpdateEntity();
        }
    }

    protected void CallSetActiveCustomPhaseIndex(string input)
    {
        var definition = new { key = "", value = 0 };
        var result = JsonConvert.DeserializeAnonymousType(input, definition);
        if (result.key == "ActiveEditingCustomPhaseIndex")
        {
            UpdateActiveEditingCustomPhaseIndex(result.value);
            UpdateEntity();
        }
        else if (result.key == "ActiveViewingCustomPhaseIndex")
        {
            UpdateActiveViewingCustomPhaseIndex(result.value);
            RedrawGizmo();
        }
        else if (result.key == "ManualSignalGroup")
        {
            UpdateManualSignalGroup(result.value);
            RedrawGizmo();
        }
        m_MainPanelBinding.Update();
    }

    protected void CallUpdateEdgeGroupMask(string input)
    {
        if (m_SelectedEntity.Equals(Entity.Null))
        {
            return;
        }

        EdgeGroupMask[] groupMaskArray = JsonConvert.DeserializeObject<EdgeGroupMask[]>(input);
        DynamicBuffer<EdgeGroupMask> groupMaskBuffer;
        if (EntityManager.HasBuffer<EdgeGroupMask>(m_SelectedEntity))
        {
            groupMaskBuffer = EntityManager.GetBuffer<EdgeGroupMask>(m_SelectedEntity, false);
        }
        else
        {
            groupMaskBuffer = EntityManager.AddBuffer<EdgeGroupMask>(m_SelectedEntity);
        }

        foreach (var newValue in groupMaskArray)
        {
            int index = CustomPhaseUtils.TryGet(groupMaskBuffer, newValue, out EdgeGroupMask oldValue);
            if (index >= 0)
            {
                groupMaskBuffer[index] = new EdgeGroupMask(oldValue, newValue);
            }
            else
            {
                groupMaskBuffer.Add(new EdgeGroupMask(oldValue, newValue));
            }
        }

        UpdateEdgeInfo(m_SelectedEntity);
        UpdateEntity();
    }

    protected void CallUpdateSubLaneGroupMask(string input)
    {
        if (m_SelectedEntity.Equals(Entity.Null))
        {
            return;
        }

        SubLaneGroupMask[] groupMaskArray = JsonConvert.DeserializeObject<SubLaneGroupMask[]>(input);
        DynamicBuffer<SubLaneGroupMask> groupMaskBuffer;
        if (EntityManager.HasBuffer<SubLaneGroupMask>(m_SelectedEntity))
        {
            groupMaskBuffer = EntityManager.GetBuffer<SubLaneGroupMask>(m_SelectedEntity, false);
        }
        else
        {
            groupMaskBuffer = EntityManager.AddBuffer<SubLaneGroupMask>(m_SelectedEntity);
        }

        foreach (var newValue in groupMaskArray)
        {
            int index = CustomPhaseUtils.TryGet(groupMaskBuffer, newValue, out SubLaneGroupMask oldValue);
            if (index >= 0)
            {
                groupMaskBuffer[index] = new SubLaneGroupMask(oldValue, newValue);
            }
            else
            {
                groupMaskBuffer.Add(new SubLaneGroupMask(oldValue, newValue));
            }
        }

        UpdateEdgeInfo(m_SelectedEntity);
        UpdateEntity();
    }

    protected void CallUpdateCustomPhaseData(string jsonString)
    {
        var input = JsonConvert.DeserializeObject<UITypes.UpdateCustomPhaseData>(jsonString);
        if (!m_SelectedEntity.Equals(Entity.Null))
        {
            if (input.key == "TrafficLightMode")
            {
                if (Convert.ToInt32(input.value) == 1)
                {
                    m_CustomTrafficLights.SetMode(CustomTrafficLights.TrafficMode.FixedTimed);
                }
                else
                {
                    m_CustomTrafficLights.SetMode(CustomTrafficLights.TrafficMode.Dynamic);
                }
                UpdateEntity();
                m_MainPanelBinding.Update();
                return;
            }

            if (input.key == "SmartPhaseSelection")
            {
                bool isEnabled = input.value.ToString().ToLower() == "true";
                var currentOptions = m_CustomTrafficLights.GetOptions();
                if (isEnabled)
                {
                    m_CustomTrafficLights.SetOptions(currentOptions | CustomTrafficLights.TrafficOptions.SmartPhaseSelection);
                }
                else
                {
                    m_CustomTrafficLights.SetOptions(currentOptions & ~CustomTrafficLights.TrafficOptions.SmartPhaseSelection);
                }
                UpdateEntity();
                m_MainPanelBinding.Update();
                return;
            }

            DynamicBuffer<CustomPhaseData> customPhaseDataBuffer;
            if (!EntityManager.TryGetBuffer(m_SelectedEntity, false, out customPhaseDataBuffer))
            {
                customPhaseDataBuffer = EntityManager.AddBuffer<CustomPhaseData>(m_SelectedEntity);
            }

            int index = input.index >= 0 ? input.index : m_ActiveEditingCustomPhaseIndexBinding.Value;
            if (index < 0 || index >= customPhaseDataBuffer.Length)
            {
                return;
            }
            var newValue = customPhaseDataBuffer[index];

            CustomPhaseDataUpdate.TryApply(input.key, input.value, ref newValue);
            
            customPhaseDataBuffer[index] = newValue;

            if (input.key == "MaximumDuration" || input.key == "MinimumDuration")
            {
                if (EntityManager.HasComponent<TrafficGroupMember>(m_SelectedEntity))
                {
                    var member = EntityManager.GetComponentData<TrafficGroupMember>(m_SelectedEntity);
                    if (member.m_GroupEntity != Entity.Null)
                    {
                        var trafficGroupSystem = World.GetOrCreateSystemManaged<TrafficGroupSystem>();
                        trafficGroupSystem.RecalculateGroupCycleLength(member.m_GroupEntity);
                    }
                }
            }

            m_MainPanelBinding.Update();
            UpdateEdgeInfo(m_SelectedEntity);
            UpdateEntity(addUpdated: false);
        }
    }

    protected void CallUpdateSignalDelay(string jsonString)
    {
        var keyDefinition = new { key = "" };
        var parsedKey = JsonConvert.DeserializeAnonymousType(jsonString, keyDefinition);
        
        var definition = new { edgeIndex = 0, edgeVersion = 0, phaseIndex = 0, laneType = "", direction = "", openDelay = 0, closeDelay = 0 };
        var input = JsonConvert.DeserializeAnonymousType(parsedKey.key, definition);
        
        if (m_SelectedEntity.Equals(Entity.Null))
        {
            return;
        }

        DynamicBuffer<EdgeGroupMask> edgeGroupMaskBuffer;
        if (!EntityManager.TryGetBuffer(m_SelectedEntity, false, out edgeGroupMaskBuffer))
        {
            return;
        }

        for (int i = 0; i < edgeGroupMaskBuffer.Length; i++)
        {
            var edgeMask = edgeGroupMaskBuffer[i];
            if (edgeMask.m_Edge.Index == input.edgeIndex && edgeMask.m_Edge.Version == input.edgeVersion)
            {
                
                if (input.laneType == "carLane")
                {
                    if (input.direction == "left") { edgeMask.m_Car.m_Left.m_OpenDelay = (short)input.openDelay; edgeMask.m_Car.m_Left.m_CloseDelay = (short)input.closeDelay; }
                    else if (input.direction == "straight") { edgeMask.m_Car.m_Straight.m_OpenDelay = (short)input.openDelay; edgeMask.m_Car.m_Straight.m_CloseDelay = (short)input.closeDelay; }
                    else if (input.direction == "right") { edgeMask.m_Car.m_Right.m_OpenDelay = (short)input.openDelay; edgeMask.m_Car.m_Right.m_CloseDelay = (short)input.closeDelay; }
                    else if (input.direction == "uTurn") { edgeMask.m_Car.m_UTurn.m_OpenDelay = (short)input.openDelay; edgeMask.m_Car.m_UTurn.m_CloseDelay = (short)input.closeDelay; }
                }
                else if (input.laneType == "publicCarLane")
                {
                    if (input.direction == "left") { edgeMask.m_PublicCar.m_Left.m_OpenDelay = (short)input.openDelay; edgeMask.m_PublicCar.m_Left.m_CloseDelay = (short)input.closeDelay; }
                    else if (input.direction == "straight") { edgeMask.m_PublicCar.m_Straight.m_OpenDelay = (short)input.openDelay; edgeMask.m_PublicCar.m_Straight.m_CloseDelay = (short)input.closeDelay; }
                    else if (input.direction == "right") { edgeMask.m_PublicCar.m_Right.m_OpenDelay = (short)input.openDelay; edgeMask.m_PublicCar.m_Right.m_CloseDelay = (short)input.closeDelay; }
                    else if (input.direction == "uTurn") { edgeMask.m_PublicCar.m_UTurn.m_OpenDelay = (short)input.openDelay; edgeMask.m_PublicCar.m_UTurn.m_CloseDelay = (short)input.closeDelay; }
                }
                else if (input.laneType == "trackLane")
                {
                    if (input.direction == "left") { edgeMask.m_Track.m_Left.m_OpenDelay = (short)input.openDelay; edgeMask.m_Track.m_Left.m_CloseDelay = (short)input.closeDelay; }
                    else if (input.direction == "straight") { edgeMask.m_Track.m_Straight.m_OpenDelay = (short)input.openDelay; edgeMask.m_Track.m_Straight.m_CloseDelay = (short)input.closeDelay; }
                    else if (input.direction == "right") { edgeMask.m_Track.m_Right.m_OpenDelay = (short)input.openDelay; edgeMask.m_Track.m_Right.m_CloseDelay = (short)input.closeDelay; }
                }
                else if (input.laneType == "pedestrianLane" || input.laneType == "pedestrianLaneStopLine" || input.laneType == "pedestrianLaneNonStopLine")
                {
                    edgeMask.m_Pedestrian.m_OpenDelay = (short)input.openDelay;
                    edgeMask.m_Pedestrian.m_CloseDelay = (short)input.closeDelay;
                }
                else if (input.laneType == "bicycleLane")
                {
                    edgeMask.m_Bicycle.m_OpenDelay = (short)input.openDelay;
                    edgeMask.m_Bicycle.m_CloseDelay = (short)input.closeDelay;
                }

                edgeGroupMaskBuffer[i] = edgeMask;
                break;
            }
        }

        m_MainPanelBinding.Update();
        UpdateEdgeInfo(m_SelectedEntity);
        UpdateEntity(addUpdated: false);
    }

    protected void CallUpdateEdgeDelay(string jsonString)
    {
        var keyDefinition = new { key = "", value = 0 };
        var parsedKey = JsonConvert.DeserializeAnonymousType(jsonString, keyDefinition);
        
        var definition = new { edgeIndex = 0, edgeVersion = 0, phaseIndex = 0, field = "" };
        var input = JsonConvert.DeserializeAnonymousType(parsedKey.key, definition);
        
        if (m_SelectedEntity.Equals(Entity.Null))
        {
            return;
        }

        DynamicBuffer<EdgeGroupMask> edgeGroupMaskBuffer;
        if (!EntityManager.TryGetBuffer(m_SelectedEntity, false, out edgeGroupMaskBuffer))
        {
            return;
        }

        for (int i = 0; i < edgeGroupMaskBuffer.Length; i++)
        {
            var edgeMask = edgeGroupMaskBuffer[i];
            if (edgeMask.m_Edge.Index == input.edgeIndex && edgeMask.m_Edge.Version == input.edgeVersion)
            {
                if (input.field == "openDelay")
                {
                    edgeMask.m_OpenDelay = (short)parsedKey.value;
                }
                else if (input.field == "closeDelay")
                {
                    edgeMask.m_CloseDelay = (short)parsedKey.value;
                }
                edgeGroupMaskBuffer[i] = edgeMask;
                break;
            }
        }

        m_MainPanelBinding.Update();
        UpdateEdgeInfo(m_SelectedEntity);
        UpdateEntity(addUpdated: false);
    }

    protected void CallApplyPhaseTemplate(string jsonString)
    {
        var input = JsonConvert.DeserializeAnonymousType(jsonString, new { templateId = 0 });
        if (m_SelectedEntity.Equals(Entity.Null))
        {
            return;
        }

        if (!EntityManager.TryGetBuffer<CustomPhaseData>(m_SelectedEntity, false, out var phaseBuffer))
        {
            return;
        }

        PhaseTemplates.ApplyTemplate(phaseBuffer, (PhaseTemplate)input.templateId);

        m_MainPanelBinding.Update();
        UpdateEntity(addUpdated: false);
    }

    protected void CallKeyPress(string value)
    {
        var definition = new { ctrlKey = false, key = "" };
        var keyPressEvent = JsonConvert.DeserializeAnonymousType(value, definition);
        if (keyPressEvent.ctrlKey && keyPressEvent.key == "S")
        {
            if (!m_SelectedEntity.Equals(Entity.Null))
            {
                SaveSelectedEntity();
            }
        }
    }

    protected void CallAddWorldPosition(string input)
    {
        UITypes.WorldPosition[] posArray = JsonConvert.DeserializeObject<UITypes.WorldPosition[]>(input);
        foreach (var pos in posArray)
        {
            m_WorldPositionList.Add(pos);
        }
        m_CameraPosition = float.MaxValue; 
    }

    protected void CallRemoveWorldPosition(string input)
    {
        UITypes.WorldPosition[] posArray = JsonConvert.DeserializeObject<UITypes.WorldPosition[]>(input);
        foreach (var pos in posArray)
        {
            m_WorldPositionList.Remove(pos);
        }
        m_CameraPosition = float.MaxValue; 
    }

    protected void CallOpenBrowser(string jsonString)
    {
        var keyDefinition = new { key = "", value = "" };
        var parsedKey = JsonConvert.DeserializeAnonymousType(jsonString, keyDefinition);
        System.Diagnostics.Process.Start(parsedKey.value);
    }

    protected void UpdateActiveEditingCustomPhaseIndex(int index)
    {
        m_ActiveEditingCustomPhaseIndexBinding.Value = index;
        m_ActiveEditingCustomPhaseIndexBinding.ForceUpdate();
        if (index >= 0)
        {
            m_ActiveViewingCustomPhaseIndexBinding.Value = -1;
            m_ActiveViewingCustomPhaseIndexBinding.ForceUpdate();
        }
    }

    protected void UpdateActiveViewingCustomPhaseIndex(int index)
    {
        m_ActiveViewingCustomPhaseIndexBinding.Value = index;
        m_ActiveViewingCustomPhaseIndexBinding.ForceUpdate();
        if (index >= 0)
        {
            m_ActiveEditingCustomPhaseIndexBinding.Value = -1;
            m_ActiveEditingCustomPhaseIndexBinding.ForceUpdate();
        }
    }

    protected void UpdateManualSignalGroup(int group)
    {
        if (m_SelectedEntity != Entity.Null)
        {
            m_CustomTrafficLights.m_ManualSignalGroup = (byte)group;
            if (group > 0 && EntityManager.TryGetComponent<TrafficLights>(m_SelectedEntity, out var trafficLights))
            {
                trafficLights.m_NextSignalGroup = (byte)group;
                EntityManager.SetComponentData(m_SelectedEntity, trafficLights);
            }
            UpdateEntity(addUpdated: false);
        }
        if (group > 0)
        {
            m_ActiveViewingCustomPhaseIndexBinding.Value = -1;
            m_ActiveViewingCustomPhaseIndexBinding.ForceUpdate();
            m_ActiveEditingCustomPhaseIndexBinding.Value = -1;
            m_ActiveEditingCustomPhaseIndexBinding.ForceUpdate();
        }
    }

    protected void CallCreateTrafficGroup(string input)
    {
        var trafficGroupSystem = World.GetOrCreateSystemManaged<TrafficGroupSystem>();
        Entity groupEntity = trafficGroupSystem.CreateGroup(null); 
        
        if (m_SelectedEntity != Entity.Null)
        {
            trafficGroupSystem.AddJunctionToGroup(groupEntity, m_SelectedEntity);
        }
        
        m_MainPanelBinding.Update();
    }

    protected void CallAddJunctionToGroup(string input)
    {
        var definition = new { groupIndex = 0, groupVersion = 0 };
        var value = JsonConvert.DeserializeAnonymousType(input, definition);
        Entity groupEntity = new Entity { Index = value.groupIndex, Version = value.groupVersion };
        
        if (m_SelectedEntity != Entity.Null && groupEntity != Entity.Null)
        {
            var trafficGroupSystem = World.GetOrCreateSystemManaged<TrafficGroupSystem>();
            trafficGroupSystem.AddJunctionToGroup(groupEntity, m_SelectedEntity);
            m_MainPanelBinding.Update();
        }
    }

    protected void CallRemoveJunctionFromGroup(string input)
    {
        if (m_SelectedEntity != Entity.Null)
        {
            var trafficGroupSystem = World.GetOrCreateSystemManaged<TrafficGroupSystem>();
            trafficGroupSystem.RemoveJunctionFromGroup(m_SelectedEntity);
            m_MainPanelBinding.Update();
        }
    }

    protected void CallDeleteTrafficGroup(string input)
    {
        var definition = new { groupIndex = 0, groupVersion = 0 };
        var value = JsonConvert.DeserializeAnonymousType(input, definition);
        Entity groupEntity = new Entity { Index = value.groupIndex, Version = value.groupVersion };
        
        if (groupEntity != Entity.Null)
        {
            var trafficGroupSystem = World.GetOrCreateSystemManaged<TrafficGroupSystem>();
            trafficGroupSystem.DeleteGroup(groupEntity);
            m_MainPanelBinding.Update();
        }
    }

    protected void CallSetTrafficGroupName(string input)
    {
        var definition = new { groupIndex = 0, groupVersion = 0, name = "" };
        var value = JsonConvert.DeserializeAnonymousType(input, definition);
        Entity groupEntity = new Entity { Index = value.groupIndex, Version = value.groupVersion };
        
        if (groupEntity != Entity.Null)
        {
            var trafficGroupSystem = World.GetOrCreateSystemManaged<TrafficGroupSystem>();
            trafficGroupSystem.SetGroupName(groupEntity, value.name);
            m_MainPanelBinding.Update();
        }
    }

    protected void CallSetGreenWaveEnabled(string input)
    {
        var definition = new { groupIndex = 0, groupVersion = 0, enabled = false, key = "", value = "", isChecked = false };
        var data = JsonConvert.DeserializeAnonymousType(input, definition);
        
        Entity groupEntity;
        bool enabled;
        
        if (data.key == "GreenWaveEnabled")
        {
            if (m_SelectedEntity == Entity.Null || !EntityManager.HasComponent<TrafficGroupMember>(m_SelectedEntity))
            {
                return;
            }
            var member = EntityManager.GetComponentData<TrafficGroupMember>(m_SelectedEntity);
            groupEntity = member.m_GroupEntity;
            
            if (EntityManager.HasComponent<TrafficGroup>(groupEntity))
            {
                var currentGroup = EntityManager.GetComponentData<TrafficGroup>(groupEntity);
                enabled = !currentGroup.m_GreenWaveEnabled;
            }
            else
            {
                enabled = false;
            }
        }
        else
        {
            groupEntity = new Entity { Index = data.groupIndex, Version = data.groupVersion };
            enabled = data.enabled;
        }
        
        if (groupEntity != Entity.Null)
        {
            var trafficGroupSystem = World.GetOrCreateSystemManaged<TrafficGroupSystem>();
            trafficGroupSystem.SetGreenWaveEnabled(groupEntity, enabled);
            m_MainPanelBinding.Update();
        }
    }

    protected void CallSetGreenWaveSpeed(string input)
    {
        var definition = new { groupIndex = 0, groupVersion = 0, speed = 50f, key = "", value = 0f };
        var data = JsonConvert.DeserializeAnonymousType(input, definition);
        
        Entity groupEntity;
        float speed;
        
        if (data.key == "GreenWaveSpeed")
        {
            speed = (float)data.value;
            if (m_SelectedEntity == Entity.Null || !EntityManager.HasComponent<TrafficGroupMember>(m_SelectedEntity))
            {
                return;
            }
            var member = EntityManager.GetComponentData<TrafficGroupMember>(m_SelectedEntity);
            groupEntity = member.m_GroupEntity;
        }
        else
        {
            groupEntity = new Entity { Index = data.groupIndex, Version = data.groupVersion };
            speed = data.speed;
        }
        
        if (groupEntity != Entity.Null)
        {
            var trafficGroupSystem = World.GetOrCreateSystemManaged<TrafficGroupSystem>();
            trafficGroupSystem.SetGreenWaveSpeed(groupEntity, speed);
            m_MainPanelBinding.Update();
        }
    }

    protected void CallSetGreenWaveOffset(string input)
    {
        var definition = new { groupIndex = 0, groupVersion = 0, offset = 0f, key = "", value = 0f };
        var data = JsonConvert.DeserializeAnonymousType(input, definition);
        
        Entity groupEntity;
        float offset;
        
        if (data.key == "GreenWaveOffset")
        {
            offset = (float)data.value;
            if (m_SelectedEntity == Entity.Null || !EntityManager.HasComponent<TrafficGroupMember>(m_SelectedEntity))
            {
                return;
            }
            var member = EntityManager.GetComponentData<TrafficGroupMember>(m_SelectedEntity);
            groupEntity = member.m_GroupEntity;
        }
        else
        {
            groupEntity = new Entity { Index = data.groupIndex, Version = data.groupVersion };
            offset = data.offset;
        }
        
        if (groupEntity != Entity.Null)
        {
            var trafficGroupSystem = World.GetOrCreateSystemManaged<TrafficGroupSystem>();
            trafficGroupSystem.SetGreenWaveOffset(groupEntity, offset);
            m_MainPanelBinding.Update();
        }
    }

    protected void CallSetTrafficGroupSignalDelay(string input)
    {
        var definition = new { groupIndex = 0, groupVersion = 0, memberIndex = 0, memberVersion = 0, signalDelay = 0, key = "", value = 0 };
        var data = JsonConvert.DeserializeAnonymousType(input, definition);
        
        Entity groupEntity;
        Entity memberEntity;
        int signalDelay;
        
        if (data.key == "SignalDelay")
        {
            signalDelay = (int)data.value;
            if (m_SelectedEntity == Entity.Null || !EntityManager.HasComponent<TrafficGroupMember>(m_SelectedEntity))
            {
                return;
            }
            var member = EntityManager.GetComponentData<TrafficGroupMember>(m_SelectedEntity);
            groupEntity = member.m_GroupEntity;
            memberEntity = m_SelectedEntity;
        }
        else
        {
            groupEntity = new Entity { Index = data.groupIndex, Version = data.groupVersion };
            memberEntity = new Entity { Index = data.memberIndex, Version = data.memberVersion };
            signalDelay = data.signalDelay;
        }
        
        if (groupEntity != Entity.Null && memberEntity != Entity.Null)
        {
            var trafficGroupSystem = World.GetOrCreateSystemManaged<TrafficGroupSystem>();
            trafficGroupSystem.SetSignalDelay(groupEntity, memberEntity, signalDelay);
            m_MainPanelBinding.Update();
        }
    }

    protected void CallCalculateSignalDelays(string input)
    {
        var definition = new { groupIndex = 0, groupVersion = 0 };
        var data = JsonConvert.DeserializeAnonymousType(input, definition);
        
        Entity groupEntity = new Entity { Index = data.groupIndex, Version = data.groupVersion };
        
        if (groupEntity != Entity.Null)
        {
            var trafficGroupSystem = World.GetOrCreateSystemManaged<TrafficGroupSystem>();
            trafficGroupSystem.CalculateSignalDelays(groupEntity);
            m_MainPanelBinding.Update();
        }
    }

    protected void CallSetCoordinated(string input)
    {
        var definition = new { groupIndex = 0, groupVersion = 0, coordinated = false, key = "", value = "", isChecked = false };
        var data = JsonConvert.DeserializeAnonymousType(input, definition);
        
        Entity groupEntity;
        bool coordinated;
        
        if (data.key == "Coordinated")
        {
            if (m_SelectedEntity == Entity.Null || !EntityManager.HasComponent<TrafficGroupMember>(m_SelectedEntity))
            {
                return;
            }
            var member = EntityManager.GetComponentData<TrafficGroupMember>(m_SelectedEntity);
            groupEntity = member.m_GroupEntity;
            
            if (EntityManager.HasComponent<TrafficGroup>(groupEntity))
            {
                var currentGroup = EntityManager.GetComponentData<TrafficGroup>(groupEntity);
                coordinated = !currentGroup.m_IsCoordinated;
            }
            else
            {
                coordinated = false;
            }
        }
        else
        {
            groupEntity = new Entity { Index = data.groupIndex, Version = data.groupVersion };
            coordinated = data.coordinated;
        }
        
        if (groupEntity != Entity.Null)
        {
            var trafficGroupSystem = World.GetOrCreateSystemManaged<TrafficGroupSystem>();
            trafficGroupSystem.SetCoordinated(groupEntity, coordinated);
            m_MainPanelBinding.Update();
        }
    }

    protected void CallSetCycleLength(string input)
    {
        var definition = new { key = "", value = 0f };
        var data = JsonConvert.DeserializeAnonymousType(input, definition);
        
        if (m_SelectedEntity == Entity.Null || !EntityManager.HasComponent<TrafficGroupMember>(m_SelectedEntity))
        {
            return;
        }
        
        var member = EntityManager.GetComponentData<TrafficGroupMember>(m_SelectedEntity);
        Entity groupEntity = member.m_GroupEntity;
        
        if (groupEntity != Entity.Null && EntityManager.HasComponent<TrafficGroup>(groupEntity))
        {
            var group = EntityManager.GetComponentData<TrafficGroup>(groupEntity);
            group.m_CycleLength = (float)data.value;
            EntityManager.SetComponentData(groupEntity, group);
            m_MainPanelBinding.Update();
        }
    }

    protected void CallGetGroupMembers(string input)
    {
        var definition = new { groupIndex = 0, groupVersion = 0 };
        var value = JsonConvert.DeserializeAnonymousType(input, definition);
        Entity groupEntity = new Entity { Index = value.groupIndex, Version = value.groupVersion };
        
        if (groupEntity != Entity.Null)
        {
            var trafficGroupSystem = World.GetOrCreateSystemManaged<TrafficGroupSystem>();
            var members = trafficGroupSystem.GetGroupMembers(groupEntity);
            
            var memberList = new ArrayList();
            foreach (var memberEntity in members)
            {
                if (EntityManager.HasComponent<TrafficGroupMember>(memberEntity))
                {
                    var memberData = EntityManager.GetComponentData<TrafficGroupMember>(memberEntity);
                    var isLeader = memberData.m_IsGroupLeader;
                    var distanceToLeader = memberData.m_DistanceToLeader;
                    var phaseOffset = memberData.m_PhaseOffset;
                    var signalDelay = memberData.m_SignalDelay;
                    
                    memberList.Add(new {
                        index = memberEntity.Index,
                        version = memberEntity.Version,
                        isLeader = isLeader,
                        distanceToLeader = distanceToLeader,
                        phaseOffset = phaseOffset,
                        signalDelay = signalDelay
                    });
                }
            }
            
            members.Dispose();
            
            var result = JsonConvert.SerializeObject(new { members = memberList });
        
        }
    }

    protected void CallSelectJunction(string input)
    {
        
        var selectionData = new { index = 0, version = 0, stayOnTrafficGroups = false };
        var parsedData = JsonConvert.DeserializeAnonymousType(input, selectionData);
        
        if (parsedData != null)
        {
            Entity targetEntity = new Entity
            {
                Index = parsedData.index,
                Version = parsedData.version
            };
            
            
            if (EntityManager.Exists(targetEntity) && EntityManager.HasComponent<Game.Net.TrafficLights>(targetEntity))
            {
                
                if (m_SelectedEntity != Entity.Null && m_SelectedEntity != targetEntity)
                {
                    SaveSelectedEntity();
                }
                
                ChangeSelectedEntity(targetEntity);
                
                if (parsedData.stayOnTrafficGroups)
                {
                    SetMainPanelState(MainPanelState.TrafficGroups);
                }
                
                m_MainPanelBinding.Update();
            }
        }
    }

    protected void CallEnterAddMemberMode(string input)
    {
        
        var groupData = new { groupIndex = 0, groupVersion = 0 };
        var parsedData = JsonConvert.DeserializeAnonymousType(input, groupData);
        
        if (parsedData != null)
        {
            Entity targetGroup = new Entity
            {
                Index = parsedData.groupIndex,
                Version = parsedData.groupVersion
            };
            
            if (EntityManager.Exists(targetGroup))
            {
                m_IsAddingMember = true;
                m_TargetGroupForMember = targetGroup;
                SetMainPanelState(MainPanelState.Empty);
                m_AddMemberStateBinding?.Update();
            }
        }
    }

    protected void ExitAddMemberMode()
    {
        m_IsAddingMember = false;
        m_TargetGroupForMember = Entity.Null;
        SetMainPanelState(MainPanelState.TrafficGroups);
        m_AddMemberStateBinding?.Update();
    }

    protected void FinishAddMemberMode()
    {
        var targetGroup = m_TargetGroupForMember;
        m_IsAddingMember = false;
        m_TargetGroupForMember = Entity.Null;
        
        
        if (targetGroup != Entity.Null)
        {
            var trafficGroupSystem = World.GetOrCreateSystemManaged<TrafficGroupSystem>();
            var members = trafficGroupSystem.GetGroupMembers(targetGroup);
            Entity firstMember = Entity.Null;
            
            foreach (var memberEntity in members)
            {
                if (EntityManager.HasComponent<TrafficGroupMember>(memberEntity))
                {
                    var memberData = EntityManager.GetComponentData<TrafficGroupMember>(memberEntity);
                    if (memberData.m_IsGroupLeader)
                    {
                        firstMember = memberEntity;
                        break;
                    }
                    if (firstMember == Entity.Null)
                    {
                        firstMember = memberEntity;
                    }
                }
            }
            members.Dispose();
            
            if (firstMember != Entity.Null)
            {
                m_SelectedEntity = firstMember;
                UpdateEdgeInfo(firstMember);
                
                if (EntityManager.HasComponent<CustomTrafficLights>(firstMember))
                {
                    m_CustomTrafficLights = EntityManager.GetComponentData<CustomTrafficLights>(firstMember);
                }
                else
                {
                    m_CustomTrafficLights = new CustomTrafficLights(CustomTrafficLights.Patterns.Vanilla);
                }
            }
        }
        
        SetMainPanelState(MainPanelState.TrafficGroups);
        m_AddMemberStateBinding?.Update();
    }

    protected string GetAddMemberState()
    {
        string groupName = "";
        int memberCount = 0;
        var membersList = new ArrayList();
        
        if (m_IsAddingMember && !m_TargetGroupForMember.Equals(Entity.Null))
        {
            var trafficGroupSystem = World.GetOrCreateSystemManaged<TrafficGroupSystem>();
            groupName = trafficGroupSystem.GetGroupName(m_TargetGroupForMember);
            memberCount = trafficGroupSystem.GetGroupMemberCount(m_TargetGroupForMember);
            
            var members = trafficGroupSystem.GetGroupMembers(m_TargetGroupForMember);
            foreach (var memberEntity in members)
            {
                bool isLeader = false;
                if (EntityManager.HasComponent<TrafficGroupMember>(memberEntity))
                {
                    var memberData = EntityManager.GetComponentData<TrafficGroupMember>(memberEntity);
                    isLeader = memberData.m_IsGroupLeader;
                }
                membersList.Add(new {
                    index = memberEntity.Index,
                    version = memberEntity.Version,
                    isLeader = isLeader
                });
            }
            members.Dispose();
        }
        
        var result = new
        {
            isAddingMember = m_IsAddingMember,
            targetGroupIndex = m_TargetGroupForMember.Index,
            targetGroupVersion = m_TargetGroupForMember.Version,
            targetGroupName = groupName,
            memberCount = memberCount,
            members = membersList
        };
        
        return JsonConvert.SerializeObject(result);
    }

    protected void CallEnterSelectMemberMode(string input)
    {
        var groupData = new { groupIndex = 0, groupVersion = 0 };
        var parsedData = JsonConvert.DeserializeAnonymousType(input, groupData);
        
        if (parsedData != null)
        {
            Entity targetGroup = new Entity
            {
                Index = parsedData.groupIndex,
                Version = parsedData.groupVersion
            };
            
            if (EntityManager.Exists(targetGroup))
            {
                EnterSelectMemberMode(targetGroup);
            }
        }
    }

    protected void CallExitSelectMemberMode(string input)
    {
        ExitSelectMemberMode();
    }

    protected string GetSelectMemberState()
    {
        string groupName = "";
        int memberCount = 0;
        var membersList = new ArrayList();
        
        if (m_IsSelectingGroupMember && !m_TargetGroupForSelection.Equals(Entity.Null))
        {
            var trafficGroupSystem = World.GetOrCreateSystemManaged<TrafficGroupSystem>();
            groupName = trafficGroupSystem.GetGroupName(m_TargetGroupForSelection);
            memberCount = trafficGroupSystem.GetGroupMemberCount(m_TargetGroupForSelection);
            
            var members = trafficGroupSystem.GetGroupMembers(m_TargetGroupForSelection);
            foreach (var memberEntity in members)
            {
                bool isLeader = false;
                if (EntityManager.HasComponent<TrafficGroupMember>(memberEntity))
                {
                    var memberData = EntityManager.GetComponentData<TrafficGroupMember>(memberEntity);
                    isLeader = memberData.m_IsGroupLeader;
                }
                membersList.Add(new {
                    index = memberEntity.Index,
                    version = memberEntity.Version,
                    isLeader = isLeader
                });
            }
            members.Dispose();
        }
        
        var result = new
        {
            isSelectingMember = m_IsSelectingGroupMember,
            targetGroupIndex = m_TargetGroupForSelection.Index,
            targetGroupVersion = m_TargetGroupForSelection.Version,
            targetGroupName = groupName,
            memberCount = memberCount,
            members = membersList
        };
        
        return JsonConvert.SerializeObject(result);
    }

    protected void CallSetSignalDelay(string jsonString)
    {
        var input = JsonConvert.DeserializeObject<UITypes.SetSignalDelayData>(jsonString);
        if (!m_SelectedEntity.Equals(Entity.Null))
        {
            Entity edgeEntity = new Entity { Index = input.edgeIndex, Version = input.edgeVersion };

            if (!EntityManager.Exists(edgeEntity))
            {
                return;
            }
            
            SignalDelaySystem.SetSignalDelay(EntityManager, m_SelectedEntity, edgeEntity, (short)input.openDelay, (short)input.closeDelay, input.isEnabled);
            UpdateEntity();
            
            if (m_SignalDelayDataBinding != null)
            {
                m_SignalDelayDataBinding.Update();
            }
        }
    }

    protected void CallRemoveSignalDelay(string jsonString)
    {
        var input = JsonConvert.DeserializeObject<UITypes.RemoveSignalDelayData>(jsonString);
        if (!m_SelectedEntity.Equals(Entity.Null))
        {
            Entity edgeEntity = new Entity { Index = input.edgeIndex, Version = input.edgeVersion };

            if (!EntityManager.Exists(edgeEntity))
            {
                return;
            }
            SignalDelaySystem.RemoveSignalDelay(EntityManager, m_SelectedEntity, edgeEntity);
            UpdateEntity();
            
            if (m_SignalDelayDataBinding != null)
            {
                m_SignalDelayDataBinding.Update();
            }
        }
    }

    

    protected string GetSignalDelayData()
    {
        var result = new Dictionary<string, object>();
        
        if (m_SelectedEntity.Equals(Entity.Null))
        {
            return JsonConvert.SerializeObject(result);
        }
        
        if (EntityManager.TryGetBuffer(m_SelectedEntity, false, out DynamicBuffer<SignalDelayData> signalDelayBuffer))
        {
            for (int i = 0; i < signalDelayBuffer.Length; i++)
            {
                var delayData = signalDelayBuffer[i];
                var key = $"{delayData.m_Edge.Index}_{delayData.m_Edge.Version}";
                
                result[key] = new
                {
                    edgeIndex = delayData.m_Edge.Index,
                    edgeVersion = delayData.m_Edge.Version,
                    openDelay = delayData.m_OpenDelay,
                    closeDelay = delayData.m_CloseDelay,
                    isEnabled = delayData.m_IsEnabled
                };
            }
        }
        

        var jsonResult = JsonConvert.SerializeObject(result);
        return jsonResult;
    }

    protected void CallForceSyncToLeader(string input)
    {
        Entity groupEntity = Entity.Null;
        
        if (m_SelectedEntity != Entity.Null && EntityManager.HasComponent<TrafficGroupMember>(m_SelectedEntity))
        {
            var member = EntityManager.GetComponentData<TrafficGroupMember>(m_SelectedEntity);
            groupEntity = member.m_GroupEntity;
        }
        
        if (groupEntity == Entity.Null && !string.IsNullOrEmpty(input))
        {
            var definition = new { groupIndex = 0, groupVersion = 0 };
            var data = JsonConvert.DeserializeAnonymousType(input, definition);
            if (data != null)
            {
                groupEntity = new Entity { Index = data.groupIndex, Version = data.groupVersion };
            }
        }
        
        if (groupEntity != Entity.Null)
        {
            var trafficGroupSystem = World.GetOrCreateSystemManaged<TrafficGroupSystem>();
            trafficGroupSystem.ForceSyncToLeader(groupEntity);
            m_MainPanelBinding.Update();
        }
    }

    protected void CallRecalculateCycleLength(string input)
    {
        Entity groupEntity = Entity.Null;
        
        if (m_SelectedEntity != Entity.Null && EntityManager.HasComponent<TrafficGroupMember>(m_SelectedEntity))
        {
            var member = EntityManager.GetComponentData<TrafficGroupMember>(m_SelectedEntity);
            groupEntity = member.m_GroupEntity;
        }
        
        if (groupEntity == Entity.Null && !string.IsNullOrEmpty(input))
        {
            var definition = new { groupIndex = 0, groupVersion = 0 };
            var data = JsonConvert.DeserializeAnonymousType(input, definition);
            if (data != null)
            {
                groupEntity = new Entity { Index = data.groupIndex, Version = data.groupVersion };
            }
        }
        
        if (groupEntity != Entity.Null)
        {
            var trafficGroupSystem = World.GetOrCreateSystemManaged<TrafficGroupSystem>();
            trafficGroupSystem.RecalculateGroupCycleLength(groupEntity);
            m_MainPanelBinding.Update();
        }
    }

    protected void CallJoinGroups(string input)
    {
        var definition = new { targetGroupIndex = 0, targetGroupVersion = 0, sourceGroupIndex = 0, sourceGroupVersion = 0 };
        var data = JsonConvert.DeserializeAnonymousType(input, definition);
        
        if (data == null)
        {
            return;
        }
        
        Entity targetGroup = new Entity { Index = data.targetGroupIndex, Version = data.targetGroupVersion };
        Entity sourceGroup = new Entity { Index = data.sourceGroupIndex, Version = data.sourceGroupVersion };
        
        if (targetGroup != Entity.Null && sourceGroup != Entity.Null)
        {
            var trafficGroupSystem = World.GetOrCreateSystemManaged<TrafficGroupSystem>();
            trafficGroupSystem.JoinGroups(targetGroup, sourceGroup);
            m_MainPanelBinding.Update();
        }
    }

    protected void CallSetGroupLeader(string input)
    {
        var definition = new { groupIndex = 0, groupVersion = 0, leaderIndex = 0, leaderVersion = 0 };
        var data = JsonConvert.DeserializeAnonymousType(input, definition);
        
        if (data == null)
        {
            return;
        }
        
        Entity groupEntity = new Entity { Index = data.groupIndex, Version = data.groupVersion };
        Entity leaderEntity = new Entity { Index = data.leaderIndex, Version = data.leaderVersion };
        
        if (leaderEntity == Entity.Null && m_SelectedEntity != Entity.Null)
        {
            leaderEntity = m_SelectedEntity;
            
            if (groupEntity == Entity.Null && EntityManager.HasComponent<TrafficGroupMember>(m_SelectedEntity))
            {
                var member = EntityManager.GetComponentData<TrafficGroupMember>(m_SelectedEntity);
                groupEntity = member.m_GroupEntity;
            }
        }
        
        if (groupEntity != Entity.Null && leaderEntity != Entity.Null)
        {
            var trafficGroupSystem = World.GetOrCreateSystemManaged<TrafficGroupSystem>();
            trafficGroupSystem.SetGroupLeader(groupEntity, leaderEntity);
            m_MainPanelBinding.Update();
        }
    }

    protected void CallSkipStep(string input)
    {
        Entity groupEntity = Entity.Null;
        
        if (m_SelectedEntity != Entity.Null && EntityManager.HasComponent<TrafficGroupMember>(m_SelectedEntity))
        {
            var member = EntityManager.GetComponentData<TrafficGroupMember>(m_SelectedEntity);
            groupEntity = member.m_GroupEntity;
        }
        
        if (groupEntity == Entity.Null && !string.IsNullOrEmpty(input))
        {
            var definition = new { groupIndex = 0, groupVersion = 0 };
            var data = JsonConvert.DeserializeAnonymousType(input, definition);
            if (data != null)
            {
                groupEntity = new Entity { Index = data.groupIndex, Version = data.groupVersion };
            }
        }
        
        if (groupEntity != Entity.Null)
        {
            var trafficGroupSystem = World.GetOrCreateSystemManaged<TrafficGroupSystem>();
            trafficGroupSystem.SkipStep(groupEntity);
            m_MainPanelBinding.Update();
        }
    }

    protected void CallMatchPhaseDurationsToLeader(string input)
    {
        Entity groupEntity = Entity.Null;
        
        if (m_SelectedEntity != Entity.Null && EntityManager.HasComponent<TrafficGroupMember>(m_SelectedEntity))
        {
            var member = EntityManager.GetComponentData<TrafficGroupMember>(m_SelectedEntity);
            groupEntity = member.m_GroupEntity;
        }
        
        if (groupEntity == Entity.Null && !string.IsNullOrEmpty(input))
        {
            var definition = new { groupIndex = 0, groupVersion = 0 };
            var data = JsonConvert.DeserializeAnonymousType(input, definition);
            if (data != null)
            {
                groupEntity = new Entity { Index = data.groupIndex, Version = data.groupVersion };
            }
        }
        
        if (groupEntity != Entity.Null)
        {
            var trafficGroupSystem = World.GetOrCreateSystemManaged<TrafficGroupSystem>();
            trafficGroupSystem.MatchPhaseDurationsToLeader(groupEntity);
            m_MainPanelBinding.Update();
        }
    }
    
    

    

    

    protected void CallApplyBestPhase(string input)
    {
        Entity groupEntity = Entity.Null;
        
        if (m_SelectedEntity != Entity.Null && EntityManager.HasComponent<TrafficGroupMember>(m_SelectedEntity))
        {
            var member = EntityManager.GetComponentData<TrafficGroupMember>(m_SelectedEntity);
            groupEntity = member.m_GroupEntity;
        }
        
        if (groupEntity == Entity.Null && !string.IsNullOrEmpty(input))
        {
            var definition = new { groupIndex = 0, groupVersion = 0 };
            var data = JsonConvert.DeserializeAnonymousType(input, definition);
            if (data != null)
            {
                groupEntity = new Entity { Index = data.groupIndex, Version = data.groupVersion };
            }
        }
        
        if (groupEntity != Entity.Null)
        {
            var trafficGroupSystem = World.GetOrCreateSystemManaged<TrafficGroupSystem>();
            trafficGroupSystem.ApplyBestPhaseToGroup(groupEntity);
            m_MainPanelBinding.Update();
        }
    }

    protected void CallHousekeepingGroup(string input)
    {
        Entity groupEntity = Entity.Null;
        
        if (m_SelectedEntity != Entity.Null && EntityManager.HasComponent<TrafficGroupMember>(m_SelectedEntity))
        {
            var member = EntityManager.GetComponentData<TrafficGroupMember>(m_SelectedEntity);
            groupEntity = member.m_GroupEntity;
        }
        
        if (groupEntity == Entity.Null && !string.IsNullOrEmpty(input))
        {
            var definition = new { groupIndex = 0, groupVersion = 0, all = false };
            var data = JsonConvert.DeserializeAnonymousType(input, definition);
            if (data != null)
            {
                if (data.all)
                {
                    var trafficGroupSystem = World.GetOrCreateSystemManaged<TrafficGroupSystem>();
                    trafficGroupSystem.HousekeepingAllGroups();
                    m_MainPanelBinding.Update();
                    return;
                }
                groupEntity = new Entity { Index = data.groupIndex, Version = data.groupVersion };
            }
        }
        
        if (groupEntity != Entity.Null)
        {
            var trafficGroupSystem = World.GetOrCreateSystemManaged<TrafficGroupSystem>();
            trafficGroupSystem.HousekeepingGroup(groupEntity);
            m_MainPanelBinding.Update();
        }
    }

    protected void CallUpdateMemberPhaseData(string jsonString)
    {
        var definition = new { 
            junctionIndex = 0, 
            junctionVersion = 0, 
            phaseIndex = -1, 
            key = "", 
            value = 0.0 
        };
        var input = JsonConvert.DeserializeAnonymousType(jsonString, definition);
        
        if (input == null)
        {
            return;
        }
        
        Entity junctionEntity = new Entity { Index = input.junctionIndex, Version = input.junctionVersion };
        
        if (junctionEntity == Entity.Null || !EntityManager.Exists(junctionEntity))
        {
            return;
        }

        DynamicBuffer<CustomPhaseData> customPhaseDataBuffer;
        if (!EntityManager.TryGetBuffer(junctionEntity, false, out customPhaseDataBuffer))
        {
            return;
        }
        
        int index = input.phaseIndex;
        if (index < 0 || index >= customPhaseDataBuffer.Length)
        {
            return;
        }
        
        var newValue = customPhaseDataBuffer[index];
        
        CustomPhaseDataUpdate.TryApply(input.key, input.value, ref newValue);
        
        customPhaseDataBuffer[index] = newValue;
        
        if (input.key == "MaximumDuration" || input.key == "MinimumDuration")
        {
            if (EntityManager.HasComponent<TrafficGroupMember>(junctionEntity))
            {
                var member = EntityManager.GetComponentData<TrafficGroupMember>(junctionEntity);
                if (member.m_GroupEntity != Entity.Null)
                {
                    var trafficGroupSystem = World.GetOrCreateSystemManaged<TrafficGroupSystem>();
                    trafficGroupSystem.RecalculateGroupCycleLength(member.m_GroupEntity);
                }
            }
        }
        
        m_MainPanelBinding.Update();
    }

    protected void CallUpdateEdgeGroupMaskForJunction(string input)
    {
        var definition = new { 
            junctionIndex = 0, 
            junctionVersion = 0, 
            edgeGroupMasks = new EdgeGroupMask[0]
        };
        var parsed = JsonConvert.DeserializeAnonymousType(input, definition);
        
        if (parsed == null || parsed.edgeGroupMasks == null)
        {
            return;
        }

        Entity junctionEntity = new Entity { Index = parsed.junctionIndex, Version = parsed.junctionVersion };
        
        if (junctionEntity == Entity.Null || !EntityManager.Exists(junctionEntity))
        {
            return;
        }

        DynamicBuffer<EdgeGroupMask> groupMaskBuffer;
        if (EntityManager.HasBuffer<EdgeGroupMask>(junctionEntity))
        {
            groupMaskBuffer = EntityManager.GetBuffer<EdgeGroupMask>(junctionEntity, false);
        }
        else
        {
            groupMaskBuffer = EntityManager.AddBuffer<EdgeGroupMask>(junctionEntity);
        }

        foreach (var newValue in parsed.edgeGroupMasks)
        {
            int index = CustomPhaseUtils.TryGet(groupMaskBuffer, newValue, out EdgeGroupMask oldValue);
            if (index >= 0)
            {
                groupMaskBuffer[index] = new EdgeGroupMask(oldValue, newValue);
            }
            else
            {
                groupMaskBuffer.Add(new EdgeGroupMask(oldValue, newValue));
            }
        }

        UpdateEdgeInfo(junctionEntity);
        EntityManager.AddComponentData(junctionEntity, default(Game.Common.Updated));
        RedrawGizmo();
        m_MainPanelBinding.Update();
    }

    protected void CallUpdateMemberPattern(string jsonString)
    {
        var definition = new { 
            junctionIndex = 0, 
            junctionVersion = 0, 
            patternValue = 0u,
            navigateToCustomPhase = false
        };
        var input = JsonConvert.DeserializeAnonymousType(jsonString, definition);
        
        if (input == null)
        {
            return;
        }
        
        Entity junctionEntity = new Entity { Index = input.junctionIndex, Version = input.junctionVersion };
        
        if (junctionEntity == Entity.Null || !EntityManager.Exists(junctionEntity))
        {
            return;
        }
        
        if (!EntityManager.HasComponent<CustomTrafficLights>(junctionEntity))
        {
            return;
        }
        
		if (EntityManager.HasComponent<TrafficGroupMember>(junctionEntity))
		{
			input = new
			{
				junctionIndex = input.junctionIndex,
				junctionVersion = input.junctionVersion,
				patternValue = (uint)CustomTrafficLights.Patterns.CustomPhase,
				navigateToCustomPhase = input.navigateToCustomPhase
			};
		}
        
        var customTrafficLights = EntityManager.GetComponentData<CustomTrafficLights>(junctionEntity);
        customTrafficLights.SetPattern((CustomTrafficLights.Patterns)input.patternValue);
        
        if (customTrafficLights.GetPatternOnly() != CustomTrafficLights.Patterns.Vanilla)
        {
            var currentPattern = customTrafficLights.GetPattern();
            currentPattern = currentPattern & ~CustomTrafficLights.Patterns.CentreTurnGiveWay;
            customTrafficLights.SetPattern(currentPattern);
        }
        
        if (customTrafficLights.GetPatternOnly() == CustomTrafficLights.Patterns.CustomPhase)
        {
            customTrafficLights.SetMode(CustomTrafficLights.TrafficMode.Dynamic);

            DynamicBuffer<CustomPhaseData> customPhaseDataBuffer;
            if (!EntityManager.TryGetBuffer(junctionEntity, false, out customPhaseDataBuffer))
            {
                customPhaseDataBuffer = EntityManager.AddBuffer<CustomPhaseData>(junctionEntity);
            }
            if (!EntityManager.HasBuffer<EdgeGroupMask>(junctionEntity))
            {
                EntityManager.AddComponent<EdgeGroupMask>(junctionEntity);
            }
            if (!EntityManager.HasBuffer<SubLaneGroupMask>(junctionEntity))
            {
                EntityManager.AddComponent<SubLaneGroupMask>(junctionEntity);
            }
            if (customPhaseDataBuffer.Length == 0
                && !EntityManager.HasComponent<TrafficGroupMember>(junctionEntity))
            {
                customPhaseDataBuffer.Add(new CustomPhaseData());
            }
            customTrafficLights.SetPattern(CustomTrafficLights.Patterns.CustomPhase);
        }
        if (customTrafficLights.GetMode() == CustomTrafficLights.TrafficMode.FixedTimed)
        {
            if (!EntityManager.HasBuffer<CustomPhaseData>(junctionEntity))
            {
                EntityManager.AddComponent<CustomPhaseData>(junctionEntity);
            }
            if (!EntityManager.HasBuffer<EdgeGroupMask>(junctionEntity))
            {
                EntityManager.AddComponent<EdgeGroupMask>(junctionEntity);
            }
            if (!EntityManager.HasBuffer<SubLaneGroupMask>(junctionEntity))
            {
                EntityManager.AddComponent<SubLaneGroupMask>(junctionEntity);
            }
            customTrafficLights.SetMode(CustomTrafficLights.TrafficMode.FixedTimed);
        }
        
        EntityManager.SetComponentData(junctionEntity, customTrafficLights);
        if (EntityManager.HasComponent<TrafficGroupMember>(junctionEntity))
        {
            var member =
                EntityManager.GetComponentData<TrafficGroupMember>(junctionEntity);
            var trafficGroupSystem =
                World.GetOrCreateSystemManaged<TrafficGroupSystem>();
            trafficGroupSystem.EnsureMemberCustomPhaseSetup(member.m_GroupEntity, junctionEntity);
            customTrafficLights =
                EntityManager.GetComponentData<CustomTrafficLights>(junctionEntity);
        }
        if (!EntityManager.HasComponent<Game.Common.Updated>(junctionEntity))
        {
            EntityManager.AddComponentData(
                junctionEntity,
                default(Game.Common.Updated));
        }
        
        
        if (junctionEntity == m_SelectedEntity)
        {
            m_CustomTrafficLights = customTrafficLights;
            UpdateEntity();
        }
        
        
        if (input.navigateToCustomPhase && 
            (customTrafficLights.GetMode() == CustomTrafficLights.TrafficMode.Dynamic || 
             customTrafficLights.GetMode() == CustomTrafficLights.TrafficMode.FixedTimed))
        {
            
            if (junctionEntity != m_SelectedEntity)
            {
                if (m_SelectedEntity != Entity.Null)
                {
                    SaveSelectedEntity();
                }
                m_SelectedEntity = junctionEntity;
                m_CustomTrafficLights = customTrafficLights;
            }
            UpdateEdgeInfo(junctionEntity);
            UpdateActiveEditingCustomPhaseIndex(0);
            SetMainPanelState(MainPanelState.CustomPhase);
        }
        else
        {
            m_MainPanelBinding.Update();
        }
    }

    public void NavigateTo(Entity entity)
    {
        var cameraUpdateSystem = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
        if (cameraUpdateSystem.orbitCameraController != null && entity != Entity.Null)
        {
            cameraUpdateSystem.orbitCameraController.followedEntity = entity;
            cameraUpdateSystem.orbitCameraController.TryMatchPosition(cameraUpdateSystem.activeCameraController);
            cameraUpdateSystem.activeCameraController = cameraUpdateSystem.orbitCameraController;
        }
    }

    protected void CallHighlightEdge(string jsonString)
    {
        var definition = new { edgeIndex = 0, edgeVersion = 0 };
        var input = JsonConvert.DeserializeAnonymousType(jsonString, definition);
        
        if (input.edgeIndex < 0)
        {
            m_HighlightedEdge = Entity.Null;
        }
        else
        {
            m_HighlightedEdge = new Entity { Index = input.edgeIndex, Version = input.edgeVersion };
        }
        
        RedrawGizmo();
    }

    

    

    

    

    protected string GetUserPresets()
    {
        if (Mod.m_Setting == null)
        {
            return "[]";
        }
        return Mod.m_Setting.GetUserPresetsJson();
    }

    protected void CallSaveUserPreset(string jsonString)
    {
        var input = JsonConvert.DeserializeAnonymousType(jsonString, new { name = "" });
        if (string.IsNullOrWhiteSpace(input?.name))
        {
            return;
        }

        if (m_SelectedEntity == Entity.Null)
        {
            return;
        }

        if (!EntityManager.TryGetBuffer<CustomPhaseData>(m_SelectedEntity, true, out var phaseBuffer) || phaseBuffer.Length == 0)
        {
            return;
        }

        var phase = phaseBuffer[0];
        Mod.m_Setting.SaveUserPreset(input.name, phase);
        m_UserPresetsBinding?.Update();
    }

    protected void CallDeleteUserPreset(string jsonString)
    {
        var input = JsonConvert.DeserializeAnonymousType(jsonString, new { presetId = "" });
        if (string.IsNullOrWhiteSpace(input?.presetId))
        {
            return;
        }

        Mod.m_Setting.DeleteUserPreset(input.presetId);
        m_UserPresetsBinding?.Update();
    }

    protected void CallApplyUserPreset(string jsonString)
    {
        var input = JsonConvert.DeserializeAnonymousType(jsonString, new { presetId = "" });
        if (string.IsNullOrWhiteSpace(input?.presetId))
        {
            return;
        }

        if (m_SelectedEntity == Entity.Null)
        {
            return;
        }

        var preset = Mod.m_Setting.GetUserPreset(input.presetId);
        if (preset == null)
        {
            return;
        }

        if (!EntityManager.TryGetBuffer<CustomPhaseData>(m_SelectedEntity, false, out var phaseBuffer))
        {
            return;
        }

        for (int i = 0; i < phaseBuffer.Length; i++)
        {
            var phase = phaseBuffer[i];
            phase.m_MinimumDuration = preset.MinDuration;
            phase.m_MaximumDuration = preset.MaxDuration;
            phase.m_TargetDurationMultiplier = preset.TargetDurationMultiplier;
            phase.m_IntervalExponent = preset.IntervalExponent;
            phase.m_WaitFlowBalance = preset.WaitFlowBalance;
            phase.m_ChangeMetric = (CustomPhaseData.StepChangeMetric)preset.ChangeMetric;
            phaseBuffer[i] = phase;
        }

        m_MainPanelBinding?.Update();
        UpdateEntity(addUpdated: false);
    }

    protected void CallUpdateUserPreset(string jsonString)
    {
        var input = JsonConvert.DeserializeAnonymousType(jsonString, new { presetId = "", name = "" });
        if (string.IsNullOrWhiteSpace(input?.presetId) || string.IsNullOrWhiteSpace(input?.name))
        {
            return;
        }

        Mod.m_Setting.UpdateUserPresetName(input.presetId, input.name);
        m_UserPresetsBinding?.Update();
    }

    private void CallNavigateToEntity(string jsonString)
    {
        var input = JsonConvert.DeserializeAnonymousType(jsonString, new { index = 0, version = 0 });
        if (input == null)
        {
            return;
        }
        var entity = new Entity { Index = input.index, Version = input.version };
        NavigateTo(entity);
    }

    private void RemoveAffectedEntity(int index)
    {
        if (index < 0)
        {
            m_AffectedIntersections.Clear();
        }
        else if (index < m_AffectedIntersections.Count)
        {
            m_AffectedIntersections.RemoveAt(index);
        }
        m_AffectedEntitiesBinding?.TriggerUpdate();
    }

    public void AddToAffectedIntersections(Entity entity)
    {
        if (!m_AffectedIntersections.Contains(entity))
        {
            m_AffectedIntersections.Add(entity);
            m_AffectedEntitiesBinding?.TriggerUpdate();
        }
    }
}
