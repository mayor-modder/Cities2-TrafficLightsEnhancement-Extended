#region Assembly Game, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null


#endregion

using System.Runtime.CompilerServices;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Tools;
using Game.Vehicles;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Internal;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Scripting;

using C2VM.TrafficLightsEnhancement.Components;
using Game;
using Game.Simulation;
using TrafficLightsEnhancement.Logic.TrafficGroups;
using TrafficLightsEnhancement.Logic.Tsp;
using TspRuntime = C2VM.TrafficLightsEnhancement.Systems.TrafficLightSystems.Simulation.TransitSignalPriorityRuntime;

namespace C2VM.TrafficLightsEnhancement.Systems.TrafficLightSystems.Simulation;

public enum TrafficLightUpdatePass
{
    CollectGroupedBaseDemand,
    UpdateLeadersAndIndependent,
    SynchronizeGroupedBaseFollowers
}

public readonly struct TrafficGroupMasterSignalState
{
    public TrafficGroupMasterSignalState(
        Game.Net.TrafficLightState state,
        byte currentSignalGroup,
        byte nextSignalGroup,
        byte timer,
        uint customTimer,
        byte signalGroupCount)
    {
        State = state;
        CurrentSignalGroup = currentSignalGroup;
        NextSignalGroup = nextSignalGroup;
        Timer = timer;
        CustomTimer = customTimer;
        SignalGroupCount = signalGroupCount;
    }

    public Game.Net.TrafficLightState State { get; }
    public byte CurrentSignalGroup { get; }
    public byte NextSignalGroup { get; }
    public byte Timer { get; }
    public uint CustomTimer { get; }
    public byte SignalGroupCount { get; }
}

[CompilerGenerated]
public partial class PatchedTrafficLightSystem : GameSystemBase
{
    [BurstCompile]
    public struct UpdateTrafficLightsJob : IJobChunk
    {
        [ReadOnly]
        public EntityTypeHandle m_EntityType;

        [ReadOnly]
        public BufferTypeHandle<Game.Net.SubLane> m_SubLaneType;

        [ReadOnly]
        public BufferTypeHandle<ConnectedEdge> m_ConnectedEdgeType;

        [ReadOnly]
        public BufferTypeHandle<Game.Objects.SubObject> m_SubObjectType;

        public ComponentTypeHandle<TrafficLights> m_TrafficLightsType;

        [ReadOnly]
        public ComponentLookup<Owner> m_OwnerData;

        [ReadOnly]
        public ComponentLookup<Node> m_NodeData;

        [ReadOnly]
        public ComponentLookup<Edge> m_EdgeData;

        [ReadOnly]
        public ComponentLookup<Curve> m_CurveData;

        [ReadOnly]
        public ComponentLookup<Lane> m_LaneData;

        [ReadOnly]
        public ComponentLookup<LaneReservation> m_LaneReservationData;

        [ReadOnly]
        public ComponentLookup<Transform> m_TransformData;

        [ReadOnly]
        public ComponentLookup<PrefabRef> m_PrefabRefData;

        [ReadOnly]
        public ComponentLookup<CarLaneData> m_PrefabCarLaneData;

        [ReadOnly]
        public ComponentLookup<MoveableBridgeData> m_PrefabMoveableBridgeData;

        [ReadOnly]
        public ComponentLookup<ObjectGeometryData> m_PrefabObjectGeometryData;

        [ReadOnly]
        public BufferLookup<LaneObject> m_LaneObjects;

        [ReadOnly]
        public BufferLookup<Game.Net.SubNet> m_SubNets;

        [ReadOnly]
        public BufferLookup<Game.Net.SubLane> m_SubLanes;

        [ReadOnly]
        public BufferLookup<ConnectedEdge> m_ConnectedEdges;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<LaneSignal> m_LaneSignalData;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<TrafficLight> m_TrafficLightData;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<PointOfInterest> m_PointOfInterestData;

        [ReadOnly]
        public NativeParallelHashMap<Entity, float>.ReadOnly m_TramApproachIndex;

        [ReadOnly]
        public int m_TramApproachIndexLaneCount;

        [ReadOnly]
        public NativeParallelHashMap<Entity, BusApproachSample>.ReadOnly m_BusApproachIndex;

        [ReadOnly]
        public int m_BusApproachIndexLaneCount;

        [ReadOnly]
        public bool m_TransitSignalPriorityDiagnosticsEnabled;

        public EntityCommandBuffer.ParallelWriter m_CommandBuffer;

        public ExtraTypeHandle m_ExtraTypeHandle;

        public ExtraData m_ExtraData;

        public TrafficLightUpdatePass m_Pass;

        [ReadOnly]
        public uint m_UpdateFrameIndex;

        [ReadOnly]
        public uint m_SimulationFrame;

        public NativeParallelHashMap<Entity, VanillaTrafficGroupDemand> m_LocalGroupedDemand;

        public NativeParallelMultiHashMap<Entity, VanillaTrafficGroupDemand> m_GroupedDemand;

        public NativeParallelHashMap<Entity, TrafficGroupMasterSignalState> m_SameTickMasterState;

        private readonly struct VanillaDemandSource
        {
            public VanillaDemandSource(VanillaTrafficGroupDemand demand)
            {
                UseCollectedDemand = true;
                Demand = demand;
            }

            public bool UseCollectedDemand { get; }

            public VanillaTrafficGroupDemand Demand { get; }
        }

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            NativeArray<Entity> nativeArray = chunk.GetNativeArray(m_EntityType);
            NativeArray<TrafficLights> nativeArray2 = chunk.GetNativeArray(ref m_TrafficLightsType);
            BufferAccessor<Game.Net.SubLane> bufferAccessor = chunk.GetBufferAccessor(ref m_SubLaneType);
            BufferAccessor<ConnectedEdge> bufferAccessor2 = chunk.GetBufferAccessor(ref m_ConnectedEdgeType);
            BufferAccessor<Game.Objects.SubObject> bufferAccessor3 = chunk.GetBufferAccessor(ref m_SubObjectType);
            NativeList<Entity> laneSignals = new NativeList<Entity>(30, Allocator.Temp);

            NativeArray<CustomTrafficLights> customTrafficLightsArray = chunk.GetNativeArray(ref m_ExtraTypeHandle.m_CustomTrafficLights);
            BufferAccessor<CustomPhaseData> customPhaseDataBufferAccessor = chunk.GetBufferAccessor(ref m_ExtraTypeHandle.m_CustomPhaseData);

            for (int i = 0; i < nativeArray2.Length; i++)
            {
                TrafficLights trafficLights = nativeArray2[i];
                DynamicBuffer<Game.Net.SubLane> subLanes = bufferAccessor[i];
                DynamicBuffer<Game.Objects.SubObject> subObjects = bufferAccessor3[i];
                if ((trafficLights.m_Flags & TrafficLightFlags.IsSubNode) != 0)
                {
                    continue;
                }

                Entity entity = default(Entity);
                MoveableBridgeData moveableBridgeData = default(MoveableBridgeData);
                FillLaneSignals(subLanes, laneSignals);
                if ((trafficLights.m_Flags & TrafficLightFlags.MoveableBridge) != 0)
                {
                    FindMoveableBridge(subObjects, out entity, out moveableBridgeData);
                    FillLaneSignals(nativeArray[i], bufferAccessor2[i], laneSignals);
                }

                CustomTrafficLights customTrafficLights = i < customTrafficLightsArray.Length ? customTrafficLightsArray[i] : new CustomTrafficLights();
                Entity currentEntity = nativeArray[i];
                bool usesCustomPhase = customTrafficLights.GetPatternOnly() == CustomTrafficLights.Patterns.CustomPhase
                    && i < customPhaseDataBufferAccessor.Length
                    && (trafficLights.m_Flags & TrafficLightFlags.MoveableBridge) == 0;
                bool isCoordinatedMember = TryGetCoordinatedMember(
                    currentEntity,
                    trafficLights,
                    out Entity groupEntity,
                    out TrafficGroupMember groupMember,
                    out TrafficGroup trafficGroup,
                    out bool hasValidCoordinationInputs);
                bool isCoordinatedBaseMember = isCoordinatedMember && !usesCustomPhase;
                bool isActiveCoordinatedGroup = isCoordinatedMember
                    && IsActiveCoordinatedGroup(groupEntity);
                TrafficGroupMember rawGroupMember = default;
                TrafficGroup rawTrafficGroup = default;
                bool hasRawGroupMember =
                    m_ExtraTypeHandle.m_TrafficGroupMember.TryGetComponent(
                        currentEntity,
                        out rawGroupMember);
                bool hasRawTrafficGroup = false;
                if (hasRawGroupMember && rawGroupMember.m_GroupEntity != Entity.Null)
                {
                    hasRawTrafficGroup =
                        m_ExtraTypeHandle.m_TrafficGroup.TryGetComponent(
                            rawGroupMember.m_GroupEntity,
                            out rawTrafficGroup);
                }
                bool isExpectedLockstepFollower = hasRawTrafficGroup
                    && rawTrafficGroup.m_IsCoordinated
                    && !rawGroupMember.m_IsGroupLeader;
                TrafficGroupLockstepDebugState lockstepDebug = default;
                bool hasLockstepDebug = m_TransitSignalPriorityDiagnosticsEnabled
                    && m_ExtraTypeHandle.m_TrafficGroupLockstepDebugState.TryGetComponent(
                        currentEntity,
                        out lockstepDebug);
                if (hasLockstepDebug)
                {
                    lockstepDebug.MemberUpdateFrame = m_UpdateFrameIndex;
                    lockstepDebug.IsCoordinated =
                        hasRawTrafficGroup && rawTrafficGroup.m_IsCoordinated;
                    lockstepDebug.IsGreenWave =
                        hasRawTrafficGroup && rawTrafficGroup.m_GreenWaveEnabled;
                    lockstepDebug.HasCompleteMapping = HasCompletePhaseMapping(
                        currentEntity,
                        trafficLights.m_SignalGroupCount);
                    if (hasRawTrafficGroup
                        && m_ExtraTypeHandle.m_TrafficGroupRuntimeData.TryGetComponent(
                            rawGroupMember.m_GroupEntity,
                            out TrafficGroupRuntimeData runtimeData))
                    {
                        lockstepDebug.LeaderUpdateFrame =
                            runtimeData.m_LeaderUpdateFrameIndex;
                    }
                }

                if (m_Pass == TrafficLightUpdatePass.CollectGroupedBaseDemand)
                {
                    if (hasLockstepDebug && isActiveCoordinatedGroup)
                    {
                        lockstepDebug.SimulationFrame = m_SimulationFrame;
                        lockstepDebug.PassFlags &=
                            ~(TrafficGroupLockstepPassFlags.CollectionVisited
                                | TrafficGroupLockstepPassFlags.SynchronizationVisited
                                | TrafficGroupLockstepPassFlags.SynchronizationApplied);
                        lockstepDebug.PassFlags |=
                            TrafficGroupLockstepPassFlags.CollectionVisited;
                        lockstepDebug.SyncDisposition =
                            TrafficGroupLockstepSyncDisposition.None;
                        m_ExtraTypeHandle.m_TrafficGroupLockstepDebugState[
                            currentEntity] = lockstepDebug;
                    }

                    if (isCoordinatedBaseMember
                        && hasValidCoordinationInputs
                        && isActiveCoordinatedGroup)
                    {
                        CollectAndResetGroupedBaseDemand(
                            currentEntity,
                            groupEntity,
                            laneSignals,
                            trafficLights);
                    }

                    laneSignals.Clear();
                    continue;
                }

                if (m_Pass == TrafficLightUpdatePass.SynchronizeGroupedBaseFollowers)
                {
                    if (!isCoordinatedMember)
                    {
                        if (hasLockstepDebug && isExpectedLockstepFollower)
                        {
                            lockstepDebug.SimulationFrame = m_SimulationFrame;
                            lockstepDebug.PassFlags |=
                                TrafficGroupLockstepPassFlags.SynchronizationVisited;
                            lockstepDebug.SyncDisposition =
                                TrafficGroupLockstepSyncDisposition.NotLockstep;
                            m_ExtraTypeHandle.m_TrafficGroupLockstepDebugState[
                                currentEntity] = lockstepDebug;
                        }
                        laneSignals.Clear();
                        continue;
                    }

                    if (groupMember.m_IsGroupLeader)
                    {
                        laneSignals.Clear();
                        continue;
                    }

                    if (!hasValidCoordinationInputs)
                    {
                        if (hasLockstepDebug)
                        {
                            lockstepDebug.SimulationFrame = m_SimulationFrame;
                            lockstepDebug.PassFlags |=
                                TrafficGroupLockstepPassFlags.SynchronizationVisited;
                            lockstepDebug.SyncDisposition =
                                TrafficGroupLockstepSyncDisposition.InvalidMaster;
                            m_ExtraTypeHandle.m_TrafficGroupLockstepDebugState[
                                currentEntity] = lockstepDebug;
                        }
                        laneSignals.Clear();
                        continue;
                    }

                    if (!isActiveCoordinatedGroup)
                    {
                        if (hasLockstepDebug
                            && (lockstepDebug.PassFlags
                                & TrafficGroupLockstepPassFlags.SynchronizationVisited) == 0)
                        {
                            lockstepDebug.SimulationFrame = m_SimulationFrame;
                            lockstepDebug.PassFlags |=
                                TrafficGroupLockstepPassFlags.SynchronizationVisited;
                            lockstepDebug.SyncDisposition =
                                TrafficGroupLockstepSyncDisposition.InactiveGroup;
                            m_ExtraTypeHandle.m_TrafficGroupLockstepDebugState[
                                currentEntity] = lockstepDebug;
                        }
                        laneSignals.Clear();
                        continue;
                    }

                    if (hasLockstepDebug)
                    {
                        lockstepDebug.SimulationFrame = m_SimulationFrame;
                        lockstepDebug.PassFlags |=
                            TrafficGroupLockstepPassFlags.SynchronizationVisited;
                    }

                    bool canSynchronizeFollower = usesCustomPhase
                        || m_LocalGroupedDemand.TryGetValue(currentEntity, out _);
                    if (!canSynchronizeFollower)
                    {
                        if (hasLockstepDebug)
                        {
                            lockstepDebug.SyncDisposition =
                                TrafficGroupLockstepSyncDisposition.MissingLocalDemand;
                            m_ExtraTypeHandle.m_TrafficGroupLockstepDebugState[
                                currentEntity] = lockstepDebug;
                        }
                        laneSignals.Clear();
                        continue;
                    }

                    if (!m_SameTickMasterState.TryGetValue(groupEntity, out var masterState))
                    {
                        if (hasLockstepDebug)
                        {
                            lockstepDebug.SyncDisposition =
                                TrafficGroupLockstepSyncDisposition.MissingMaster;
                            m_ExtraTypeHandle.m_TrafficGroupLockstepDebugState[
                                currentEntity] = lockstepDebug;
                        }
                        laneSignals.Clear();
                        continue;
                    }

                    if (!IsValidMasterState(masterState))
                    {
                        if (hasLockstepDebug)
                        {
                            lockstepDebug.SyncDisposition =
                                TrafficGroupLockstepSyncDisposition.InvalidMaster;
                            m_ExtraTypeHandle.m_TrafficGroupLockstepDebugState[
                                currentEntity] = lockstepDebug;
                        }
                        laneSignals.Clear();
                        continue;
                    }

                    TrafficGroupLockstepSyncDisposition mappingDisposition =
                        TryBuildMappedMasterSnapshot(
                            currentEntity,
                            trafficLights,
                            masterState,
                            out TrafficGroupLockstepControllerSnapshot mappedMaster,
                            out byte mappedCurrentGroup,
                            out byte mappedNextGroup);
                    if (mappingDisposition != TrafficGroupLockstepSyncDisposition.Applied)
                    {
                        if (hasLockstepDebug)
                        {
                            lockstepDebug.SyncDisposition = mappingDisposition;
                            m_ExtraTypeHandle.m_TrafficGroupLockstepDebugState[
                                currentEntity] = lockstepDebug;
                        }
                        laneSignals.Clear();
                        continue;
                    }

                    if (hasLockstepDebug)
                    {
                        lockstepDebug.Before =
                            TrafficGroupLockstepRuntimeDiagnostics.Snapshot(
                                trafficLights,
                                customTrafficLights);
                        lockstepDebug.Master = mappedMaster;
                        lockstepDebug.MappedCurrentGroup = mappedCurrentGroup;
                        lockstepDebug.MappedNextGroup = mappedNextGroup;
                        lockstepDebug.LaneHashBefore =
                            TrafficGroupLockstepRuntimeDiagnostics.HashLaneSignals(
                                laneSignals,
                                m_LaneSignalData,
                                m_ExtraTypeHandle.m_ExtraLaneSignal);
                        lockstepDebug.RenderedHashBefore =
                            TrafficGroupLockstepRuntimeDiagnostics.HashRenderedLights(
                                subObjects,
                                m_TrafficLightData);
                    }

                    CustomStateMachine.SyncSignalGroupWithLeader(
                        this,
                        currentEntity,
                        groupEntity,
                        masterState,
                        ref trafficLights,
                        ref customTrafficLights);
                    UpdateLaneSignals(laneSignals, trafficLights, resetPriority: false);
                    UpdateTrafficLightObjects(subObjects, trafficLights);

                    if (hasLockstepDebug)
                    {
                        lockstepDebug.After =
                            TrafficGroupLockstepRuntimeDiagnostics.Snapshot(
                                trafficLights,
                                customTrafficLights);
                        lockstepDebug.LaneHashAfter =
                            TrafficGroupLockstepRuntimeDiagnostics.HashLaneSignals(
                                laneSignals,
                                m_LaneSignalData,
                                m_ExtraTypeHandle.m_ExtraLaneSignal);
                        lockstepDebug.RenderedHashAfter =
                            TrafficGroupLockstepRuntimeDiagnostics.HashRenderedLights(
                                subObjects,
                                m_TrafficLightData);
                        lockstepDebug.LaneCount = laneSignals.Length;
                        lockstepDebug.RenderedCount =
                            TrafficGroupLockstepRuntimeDiagnostics.CountRenderedLights(
                                subObjects,
                                m_TrafficLightData);
                        lockstepDebug.SyncDisposition =
                            TrafficGroupLockstepSyncDisposition.Applied;
                        lockstepDebug.PassFlags |=
                            TrafficGroupLockstepPassFlags.SynchronizationApplied;
                        m_ExtraTypeHandle.m_TrafficGroupLockstepDebugState[
                            currentEntity] = lockstepDebug;
                    }

                    if (i < customTrafficLightsArray.Length)
                    {
                        customTrafficLightsArray[i] = customTrafficLights;
                    }

                    nativeArray2[i] = trafficLights;
                    laneSignals.Clear();
                    continue;
                }

                if (isCoordinatedMember
                    && !groupMember.m_IsGroupLeader
                    && !isActiveCoordinatedGroup)
                {
                    if (hasLockstepDebug)
                    {
                        TrafficGroupLockstepControllerSnapshot heldSnapshot =
                            TrafficGroupLockstepRuntimeDiagnostics.Snapshot(
                                trafficLights,
                                customTrafficLights);
                        lockstepDebug.IndependentSimulationFrame = m_SimulationFrame;
                        lockstepDebug.PassFlags &=
                            ~(TrafficGroupLockstepPassFlags.IndependentVisited
                                | TrafficGroupLockstepPassFlags.IndependentDeferred
                                | TrafficGroupLockstepPassFlags.IndependentHeld
                                | TrafficGroupLockstepPassFlags.IndependentAdvanced);
                        lockstepDebug.PassFlags |=
                            TrafficGroupLockstepPassFlags.IndependentVisited
                            | TrafficGroupLockstepPassFlags.IndependentHeld;
                        lockstepDebug.IndependentBefore = heldSnapshot;
                        lockstepDebug.IndependentAfter = heldSnapshot;
                        m_ExtraTypeHandle.m_TrafficGroupLockstepDebugState[
                            currentEntity] = lockstepDebug;
                    }
                    laneSignals.Clear();
                    continue;
                }

                TrafficGroupLockstepControllerSnapshot independentBefore = default;
                if (hasLockstepDebug && isExpectedLockstepFollower)
                {
                    lockstepDebug.IndependentSimulationFrame = m_SimulationFrame;
                    lockstepDebug.PassFlags &=
                        ~(TrafficGroupLockstepPassFlags.IndependentVisited
                            | TrafficGroupLockstepPassFlags.IndependentDeferred
                            | TrafficGroupLockstepPassFlags.IndependentHeld
                            | TrafficGroupLockstepPassFlags.IndependentAdvanced);
                    independentBefore =
                        TrafficGroupLockstepRuntimeDiagnostics.Snapshot(
                            trafficLights,
                            customTrafficLights);
                    lockstepDebug.IndependentBefore = independentBefore;
                    lockstepDebug.PassFlags |=
                        TrafficGroupLockstepPassFlags.IndependentVisited;
                }

                bool hasTspRequest = false;
                TransitSignalPriorityRequest activeTspRequest = default;
                TransitSignalPriorityRuntimeDebugInfo activeTspDebugInfo = default;
                bool hasActiveTspDebugInfo = false;
                TransitSignalPriorityBusApproachDebugInfo activeBusApproachDebugInfo = default;
                bool hasActiveBusApproachDebugInfo = false;
                TransitSignalPriorityBusApproachDebugInfo reusableBusApproachDebugInfo = default;
                bool hasReusableBusApproachDebugInfo = false;
                C2VM.TrafficLightsEnhancement.Components.TransitSignalPrioritySettings activeTspSettings =
                    C2VM.TrafficLightsEnhancement.Components.TransitSignalPrioritySettings.CreateDefault();
                TspPedestrianFairnessState pedestrianFairnessState =
                    m_ExtraTypeHandle.m_TransitSignalPriorityPedestrianFairnessState.TryGetComponent(currentEntity, out var pedestrianFairnessComponent)
                        ? pedestrianFairnessComponent.ToLogicState()
                        : TspPedestrianFairnessState.None;
                TspVehicleFairnessState vehicleFairnessState =
                    m_ExtraTypeHandle.m_TransitSignalPriorityVehicleFairnessState.TryGetComponent(currentEntity, out var vehicleFairnessComponent)
                        ? vehicleFairnessComponent.ToLogicState()
                        : TspVehicleFairnessState.None;

                if (TspRuntime.TryResolveActiveLocalRequest(
                    this,
                    currentEntity,
                    subLanes,
                    trafficLights,
                    m_TransitSignalPriorityDiagnosticsEnabled,
                    out var tspRequest,
                    out var tspSettings,
                    out var runtimeDebugInfo,
                    out reusableBusApproachDebugInfo,
                    out hasReusableBusApproachDebugInfo,
                    out var busProgress))
                {
                    hasTspRequest = true;
                    activeTspRequest = tspRequest;
                    activeTspSettings = tspSettings;
                    activeTspDebugInfo = runtimeDebugInfo;
                    hasActiveTspDebugInfo = true;

                    if (m_ExtraTypeHandle.m_TransitSignalPriorityRequest.HasComponent(currentEntity))
                    {
                        m_CommandBuffer.SetComponent(unfilteredChunkIndex, currentEntity, tspRequest);
                    }
                    else
                    {
                        m_CommandBuffer.AddComponent(unfilteredChunkIndex, currentEntity, tspRequest);
                    }
                }
                else
                {
                    if (m_ExtraTypeHandle.m_TransitSignalPriorityRequest.HasComponent(currentEntity))
                    {
                        m_CommandBuffer.RemoveComponent<TransitSignalPriorityRequest>(unfilteredChunkIndex, currentEntity);
                    }
                }

                // Progress outlives a suppressed request. Only currently observed eligible
                // buses are retained; source disable/grouping/absence clears this transient buffer.
                bool hadBusProgress = m_ExtraTypeHandle.m_TransitSignalPriorityBusProgress.HasBuffer(currentEntity);
                if (busProgress.IsCreated && busProgress.Length > 0)
                {
                    var storedProgress = hadBusProgress
                        ? m_CommandBuffer.SetBuffer<TransitSignalPriorityBusProgress>(unfilteredChunkIndex, currentEntity)
                        : m_CommandBuffer.AddBuffer<TransitSignalPriorityBusProgress>(unfilteredChunkIndex, currentEntity);
                    storedProgress.CopyFrom(busProgress.AsArray());
                }
                else if (hadBusProgress)
                {
                    m_CommandBuffer.RemoveComponent<TransitSignalPriorityBusProgress>(unfilteredChunkIndex, currentEntity);
                }
                if (busProgress.IsCreated)
                    busProgress.Dispose();

                if (hasActiveTspDebugInfo)
                {
                    if (m_ExtraTypeHandle.m_TransitSignalPriorityRuntimeDebugInfo.HasComponent(currentEntity))
                    {
                        m_CommandBuffer.SetComponent(unfilteredChunkIndex, currentEntity, activeTspDebugInfo);
                    }
                    else
                    {
                        m_CommandBuffer.AddComponent(unfilteredChunkIndex, currentEntity, activeTspDebugInfo);
                    }
                }
                else if (m_ExtraTypeHandle.m_TransitSignalPriorityRuntimeDebugInfo.HasComponent(currentEntity))
                {
                    m_CommandBuffer.RemoveComponent<TransitSignalPriorityRuntimeDebugInfo>(unfilteredChunkIndex, currentEntity);
                }

                C2VM.TrafficLightsEnhancement.Components.TransitSignalPrioritySettings diagnosticsTspSettings = activeTspSettings;
                if (!hasTspRequest
                    && m_ExtraTypeHandle.m_TransitSignalPrioritySettingsLookup.TryGetComponent(currentEntity, out var selectedTspSettings))
                {
                    diagnosticsTspSettings = selectedTspSettings;
                    diagnosticsTspSettings.Normalize();
                }

                if (m_TransitSignalPriorityDiagnosticsEnabled)
                {
                    activeBusApproachDebugInfo = hasReusableBusApproachDebugInfo
                        ? reusableBusApproachDebugInfo
                        : TspRuntime.BuildBusApproachDebugInfo(
                            this,
                            subLanes,
                            trafficLights,
                            diagnosticsTspSettings.ToLogicSettings());
                    hasActiveBusApproachDebugInfo = true;
                }

                if (hasActiveBusApproachDebugInfo)
                {
                    if (m_ExtraTypeHandle.m_TransitSignalPriorityBusApproachDebugInfo.HasComponent(currentEntity))
                    {
                        m_CommandBuffer.SetComponent(unfilteredChunkIndex, currentEntity, activeBusApproachDebugInfo);
                    }
                    else
                    {
                        m_CommandBuffer.AddComponent(unfilteredChunkIndex, currentEntity, activeBusApproachDebugInfo);
                    }
                }
                else if (m_ExtraTypeHandle.m_TransitSignalPriorityBusApproachDebugInfo.HasComponent(currentEntity))
                {
                    m_CommandBuffer.RemoveComponent<TransitSignalPriorityBusApproachDebugInfo>(unfilteredChunkIndex, currentEntity);
                }

                bool tspTraceWritten = false;
                bool deferCoordinatedFollower = isCoordinatedMember
                    && !groupMember.m_IsGroupLeader
                    && hasValidCoordinationInputs
                    && isActiveCoordinatedGroup;

                if (deferCoordinatedFollower)
                {
                    // The dependent follower pass applies this tick's leader state.
                    if (hasLockstepDebug)
                    {
                        lockstepDebug.PassFlags |=
                            TrafficGroupLockstepPassFlags.IndependentDeferred;
                    }
                }
                else if (usesCustomPhase && CustomStateMachine.ShouldFollowLeader(this, currentEntity, out Entity customGroupEntity))
                {
                    CustomStateMachine.SyncSignalGroupWithLeader(this, currentEntity, customGroupEntity, ref trafficLights, ref customTrafficLights);
                    UpdateLaneSignals(laneSignals, trafficLights);
                    UpdateTrafficLightObjects(subObjects, trafficLights);
                }
                else if (usesCustomPhase)
                {
                    DynamicBuffer<CustomPhaseData> customPhaseDataBuffer = customPhaseDataBufferAccessor[i];
                    CustomStateMachine.CalculatePriority(this, subLanes, customPhaseDataBuffer);
                    CustomStateMachine.CalculateFlow(this, unfilteredChunkIndex, subLanes, trafficLights, customPhaseDataBuffer);
                    
                    bool trafficLightStateUpdated = CustomStateMachine.UpdateTrafficLightState(
                        ref trafficLights,
                        ref customTrafficLights,
                        customPhaseDataBuffer,
                        customPhaseDataBuffer,
                        activeTspSettings,
                        hasTspRequest,
                        activeTspRequest,
                        ref pedestrianFairnessState,
                        ref vehicleFairnessState,
                        out var tspSelection);

                    if (tspSelection.Applied
                        && (trafficLightStateUpdated || tspSelection.Reason == TspSelectionReason.ExtendedCurrentPhase))
                    {
                        WriteTspDecisionTrace(
                            unfilteredChunkIndex,
                            currentEntity,
                            trafficLights,
                            activeTspRequest,
                            tspSelection,
                            customTrafficLights,
                            pedestrianFairnessState,
                            vehicleFairnessState);
                        tspTraceWritten = true;
                    }

                    if (trafficLightStateUpdated)
                    {
                        UpdateLaneSignals(laneSignals, trafficLights);
                        UpdateTrafficLightObjects(subObjects, trafficLights);
                    }

                    if (isCoordinatedMember
                        && groupMember.m_IsGroupLeader
                        && hasValidCoordinationInputs
                        && isActiveCoordinatedGroup
                        && HasCompletePhaseMapping(currentEntity, trafficLights.m_SignalGroupCount))
                    {
                        PublishSameTickMasterState(groupEntity, trafficLights, customTrafficLights);
                    }
                }
                else
                {
                    VanillaDemandSource demandSource = default;
                    bool publishSameTickMaster = false;
                    if (isCoordinatedBaseMember
                        && groupMember.m_IsGroupLeader
                        && hasValidCoordinationInputs
                        && isActiveCoordinatedGroup)
                    {
                        bool hasCompleteLeaderMapping = HasCompletePhaseMapping(
                            currentEntity,
                            trafficLights.m_SignalGroupCount);
                        demandSource = hasCompleteLeaderMapping
                            ? GetGroupedLeaderDemand(currentEntity, groupEntity)
                            : GetLocalGroupedDemand(currentEntity);
                        publishSameTickMaster = hasCompleteLeaderMapping
                            && demandSource.UseCollectedDemand;
                    }

                    bool trafficLightStateUpdated = UpdateTrafficLightState(
                        laneSignals,
                        moveableBridgeData,
                        ref trafficLights,
                        ref customTrafficLights,
                        activeTspSettings,
                        hasTspRequest,
                        activeTspRequest,
                        ref pedestrianFairnessState,
                        ref vehicleFairnessState,
                        demandSource,
                        out var tspSelection);

                    if (publishSameTickMaster)
                    {
                        PublishSameTickMasterState(groupEntity, trafficLights, customTrafficLights);
                    }

                    if (tspSelection.Applied)
                    {
                        WriteTspDecisionTrace(
                            unfilteredChunkIndex,
                            currentEntity,
                            trafficLights,
                            activeTspRequest,
                            tspSelection,
                            customTrafficLights,
                            pedestrianFairnessState,
                            vehicleFairnessState);
                        tspTraceWritten = true;
                    }

                    if (trafficLightStateUpdated)
                    {
                        UpdateLaneSignals(
                            laneSignals,
                            trafficLights,
                            resetPriority: !demandSource.UseCollectedDemand);
                        UpdateTrafficLightObjects(subObjects, trafficLights);
                        if (entity != Entity.Null)
                        {
                            ref PointOfInterest valueRW = ref m_PointOfInterestData.GetRefRW(entity).ValueRW;
                            UpdateMoveableBridge(trafficLights, m_TransformData[entity], moveableBridgeData, ref valueRW);
                            m_CommandBuffer.AddComponent<EffectsUpdated>(unfilteredChunkIndex, currentEntity);
                        }
                    }
                }

                if (!tspTraceWritten && m_ExtraTypeHandle.m_TransitSignalPriorityDecisionTrace.HasComponent(currentEntity))
                {
                    m_CommandBuffer.RemoveComponent<TransitSignalPriorityDecisionTrace>(unfilteredChunkIndex, currentEntity);
                }

                pedestrianFairnessState = TspPedestrianFairnessPolicy.Refresh(
                    pedestrianFairnessState,
                    IsExclusivePedestrianEnabled(customTrafficLights),
                    customTrafficLights.m_PedestrianPhaseGroupMask,
                    trafficLights.m_CurrentSignalGroup);
                WriteTspPedestrianFairnessState(unfilteredChunkIndex, currentEntity, pedestrianFairnessState);
                vehicleFairnessState = TspVehicleFairnessPolicy.Refresh(
                    vehicleFairnessState,
                    trafficLights.m_SignalGroupCount,
                    trafficLights.m_CurrentSignalGroup);
                WriteTspVehicleFairnessState(unfilteredChunkIndex, currentEntity, vehicleFairnessState);

                if (i < customTrafficLightsArray.Length)
                {
                    customTrafficLightsArray[i] = customTrafficLights;
                }

                if (hasLockstepDebug && isExpectedLockstepFollower)
                {
                    TrafficGroupLockstepControllerSnapshot independentAfter =
                        TrafficGroupLockstepRuntimeDiagnostics.Snapshot(
                            trafficLights,
                            customTrafficLights);
                    lockstepDebug.IndependentAfter = independentAfter;
                    if (independentAfter != independentBefore)
                    {
                        lockstepDebug.PassFlags |=
                            TrafficGroupLockstepPassFlags.IndependentAdvanced;
                    }
                    m_ExtraTypeHandle.m_TrafficGroupLockstepDebugState[
                        currentEntity] = lockstepDebug;
                }

                nativeArray2[i] = trafficLights;
                laneSignals.Clear();
            }

            laneSignals.Dispose();
        }

        private bool TryGetCoordinatedMember(
            Entity currentEntity,
            TrafficLights trafficLights,
            out Entity groupEntity,
            out TrafficGroupMember member,
            out TrafficGroup group,
            out bool hasValidCoordinationInputs)
        {
            groupEntity = Entity.Null;
            member = default;
            group = default;
            hasValidCoordinationInputs = false;

            if (!m_ExtraTypeHandle.m_TrafficGroupMember.TryGetComponent(currentEntity, out member)
                || member.m_GroupEntity == Entity.Null
                || !m_ExtraTypeHandle.m_TrafficGroup.TryGetComponent(member.m_GroupEntity, out group)
                || !group.m_IsCoordinated)
            {
                return false;
            }

            groupEntity = member.m_GroupEntity;
            Entity leaderEntity = member.m_IsGroupLeader ? currentEntity : member.m_LeaderEntity;
            hasValidCoordinationInputs = trafficLights.m_SignalGroupCount is >= 1
                    and <= TrafficGroupMovementMappingPolicy.MaximumMappedPhaseCount
                && leaderEntity != Entity.Null
                && m_ExtraTypeHandle.m_TrafficLightsLookup.TryGetComponent(leaderEntity, out var leaderTrafficLights)
                && leaderTrafficLights.m_SignalGroupCount is >= 1
                    and <= TrafficGroupMovementMappingPolicy.MaximumMappedPhaseCount;
            return true;
        }

        private void PublishSameTickMasterState(
            Entity groupEntity,
            TrafficLights trafficLights,
            CustomTrafficLights customTrafficLights)
        {
            m_SameTickMasterState.AsParallelWriter().TryAdd(
                groupEntity,
                new TrafficGroupMasterSignalState(
                    trafficLights.m_State,
                    trafficLights.m_CurrentSignalGroup,
                    trafficLights.m_NextSignalGroup,
                    trafficLights.m_Timer,
                    customTrafficLights.m_Timer,
                    trafficLights.m_SignalGroupCount));
        }

        private void CollectAndResetGroupedBaseDemand(
            Entity currentEntity,
            Entity groupEntity,
            NativeList<Entity> laneSignals,
            TrafficLights trafficLights)
        {
            Entity petitioner = Entity.Null;
            Entity blocker = Entity.Null;
            int highestPriority = 0;
            int requestedPhaseMask = 0;
            int extendablePhaseMask = 0;
            int suppressedPhaseMask = 0;
            int priorityCap = math.select(
                127,
                1,
                (trafficLights.m_Flags & TrafficLightFlags.MoveableBridge) != 0);

            for (int i = 0; i < laneSignals.Length; i++)
            {
                Entity laneSignalEntity = laneSignals[i];
                LaneSignal laneSignal = m_LaneSignalData[laneSignalEntity];
                ExtraLaneSignal extraLaneSignal = m_ExtraTypeHandle.m_ExtraLaneSignal.TryGetComponent(
                    laneSignalEntity,
                    out var existingExtraLaneSignal)
                    ? existingExtraLaneSignal
                    : default;

                if (trafficLights.m_CurrentSignalGroup > 0)
                {
                    int currentGroupMask = 1 << (trafficLights.m_CurrentSignalGroup - 1);
                    if ((laneSignal.m_GroupMask & currentGroupMask) != 0
                        && (extraLaneSignal.m_IgnorePriorityGroupMask & currentGroupMask) != 0)
                    {
                        laneSignal.m_Priority = laneSignal.m_Default;
                    }
                }

                int priority = math.min(laneSignal.m_Priority, priorityCap);
                if (priority > highestPriority)
                {
                    petitioner = laneSignal.m_Petitioner;
                    highestPriority = priority;
                    requestedPhaseMask = laneSignal.m_GroupMask;
                    extendablePhaseMask = math.select(
                        0,
                        laneSignal.m_GroupMask,
                        (laneSignal.m_Flags & LaneSignalFlags.CanExtend) != 0);
                }
                else if (priority == highestPriority)
                {
                    requestedPhaseMask |= laneSignal.m_GroupMask;
                    extendablePhaseMask |= math.select(
                        0,
                        laneSignal.m_GroupMask,
                        (laneSignal.m_Flags & LaneSignalFlags.CanExtend) != 0);
                }
                else if (priority < 0)
                {
                    suppressedPhaseMask |= laneSignal.m_GroupMask;
                }

                if (laneSignal.m_Blocker != Entity.Null)
                {
                    blocker = laneSignal.m_Blocker;
                }
            }

            for (int i = 0; i < laneSignals.Length; i++)
            {
                Entity laneSignalEntity = laneSignals[i];
                LaneSignal laneSignal = m_LaneSignalData[laneSignalEntity];
                if (petitioner != blocker)
                {
                    laneSignal.m_Blocker = (requestedPhaseMask & laneSignal.m_GroupMask) != 0
                        ? Entity.Null
                        : petitioner;
                }

                laneSignal.m_Petitioner = Entity.Null;
                laneSignal.m_Priority = laneSignal.m_Default;
                m_LaneSignalData[laneSignalEntity] = laneSignal;
            }

            var localDemand = new VanillaTrafficGroupDemand(
                highestPriority,
                requestedPhaseMask,
                extendablePhaseMask,
                suppressedPhaseMask);
            m_LocalGroupedDemand.AsParallelWriter().TryAdd(currentEntity, localDemand);

            if (m_ExtraTypeHandle.m_TrafficGroupPhaseMapping.TryGetComponent(
                    currentEntity,
                    out var phaseMapping)
                && VanillaTrafficGroupDemandPolicy.TryRemapMemberToLeader(
                    localDemand,
                    phaseMapping.m_Map,
                    out var groupDemand))
            {
                m_GroupedDemand.AsParallelWriter().Add(groupEntity, groupDemand);
            }
        }

        private VanillaDemandSource GetGroupedLeaderDemand(Entity leaderEntity, Entity groupEntity)
        {
            bool hasGroupDemand = m_GroupedDemand.TryGetFirstValue(
                groupEntity,
                out var memberDemand,
                out var iterator);
            if (hasGroupDemand)
            {
                VanillaTrafficGroupDemand aggregate = default;
                do
                {
                    aggregate = VanillaTrafficGroupDemandPolicy.Merge(aggregate, memberDemand);
                }
                while (m_GroupedDemand.TryGetNextValue(out memberDemand, ref iterator));

                return new VanillaDemandSource(aggregate);
            }

            return m_LocalGroupedDemand.TryGetValue(leaderEntity, out var localDemand)
                ? new VanillaDemandSource(localDemand)
                : default;
        }

        private VanillaDemandSource GetLocalGroupedDemand(Entity memberEntity)
        {
            return m_LocalGroupedDemand.TryGetValue(memberEntity, out var localDemand)
                ? new VanillaDemandSource(localDemand)
                : default;
        }

        private static bool IsValidMasterState(TrafficGroupMasterSignalState masterState)
        {
            return masterState.SignalGroupCount is >= 1
                and <= TrafficGroupMovementMappingPolicy.MaximumMappedPhaseCount;
        }

        private bool IsActiveCoordinatedGroup(Entity groupEntity)
        {
            return m_ExtraTypeHandle.m_TrafficGroupRuntimeData.TryGetComponent(
                       groupEntity,
                       out var runtimeData)
                   && runtimeData.m_LeaderUpdateFrameIndex == m_UpdateFrameIndex;
        }

        private bool HasCompletePhaseMapping(Entity memberEntity, int memberPhaseCount)
        {
            return m_ExtraTypeHandle.m_TrafficGroupPhaseMapping.TryGetComponent(
                       memberEntity,
                       out var phaseMapping)
                   && phaseMapping.m_Map.IsComplete
                   && phaseMapping.m_Map.MemberPhaseCount == memberPhaseCount;
        }

        private bool CanMapMasterStateToMember(
            Entity memberEntity,
            TrafficGroupMasterSignalState masterState)
        {
            if (!m_ExtraTypeHandle.m_TrafficGroupPhaseMapping.TryGetComponent(
                    memberEntity,
                    out var phaseMapping)
                || !phaseMapping.m_Map.IsComplete
                || phaseMapping.m_Map.LeaderPhaseCount != masterState.SignalGroupCount
                || !phaseMapping.m_Map.TryMapLeaderToMember(
                    masterState.CurrentSignalGroup,
                    out _))
            {
                return false;
            }

            return masterState.NextSignalGroup == 0
                || phaseMapping.m_Map.TryMapLeaderToMember(
                    masterState.NextSignalGroup,
                    out _);
        }

        private TrafficGroupLockstepSyncDisposition TryBuildMappedMasterSnapshot(
            Entity memberEntity,
            TrafficLights memberTrafficLights,
            TrafficGroupMasterSignalState masterState,
            out TrafficGroupLockstepControllerSnapshot mappedMaster,
            out byte mappedCurrentGroup,
            out byte mappedNextGroup)
        {
            mappedMaster = default;
            mappedCurrentGroup = 0;
            mappedNextGroup = 0;
            if (!m_ExtraTypeHandle.m_TrafficGroupPhaseMapping.TryGetComponent(
                    memberEntity,
                    out TrafficGroupPhaseMapping phaseMapping))
            {
                return TrafficGroupLockstepSyncDisposition.MissingMapping;
            }

            if (!phaseMapping.m_Map.IsComplete
                || phaseMapping.m_Map.LeaderPhaseCount != masterState.SignalGroupCount
                || phaseMapping.m_Map.MemberPhaseCount
                    != memberTrafficLights.m_SignalGroupCount)
            {
                return TrafficGroupLockstepSyncDisposition.IncompleteMapping;
            }

            if (!phaseMapping.m_Map.TryMapLeaderToMember(
                    masterState.CurrentSignalGroup,
                    out int mappedCurrent))
            {
                return TrafficGroupLockstepSyncDisposition.UnmappedCurrentPhase;
            }

            int mappedNext = 0;
            if (masterState.NextSignalGroup != 0
                && !phaseMapping.m_Map.TryMapLeaderToMember(
                    masterState.NextSignalGroup,
                    out mappedNext))
            {
                return TrafficGroupLockstepSyncDisposition.UnmappedNextPhase;
            }

            mappedCurrentGroup = (byte)mappedCurrent;
            mappedNextGroup = (byte)mappedNext;
            mappedMaster = new TrafficGroupLockstepControllerSnapshot(
                (byte)masterState.State,
                mappedCurrentGroup,
                mappedNextGroup,
                masterState.Timer,
                masterState.CustomTimer,
                memberTrafficLights.m_SignalGroupCount);
            return TrafficGroupLockstepSyncDisposition.Applied;
        }

        private void FillLaneSignals(DynamicBuffer<Game.Net.SubLane> subLanes, NativeList<Entity> laneSignals)
        {
            for (int i = 0; i < subLanes.Length; i++)
            {
                Entity value = subLanes[i].m_SubLane;
                if (m_LaneSignalData.HasComponent(value))
                {
                    laneSignals.Add(in value);
                }
            }
        }

        private void FillLaneSignals(Entity node, DynamicBuffer<ConnectedEdge> connectedEdges, NativeList<Entity> laneSignals)
        {
            for (int i = 0; i < connectedEdges.Length; i++)
            {
                Entity edge = connectedEdges[i].m_Edge;
                if (m_SubNets.TryGetBuffer(edge, out var bufferData))
                {
                    FillLaneSignals(node, edge, bufferData, laneSignals);
                }
            }
        }

        private void FillLaneSignals(Entity node, Entity edge, DynamicBuffer<Game.Net.SubNet> subNets, NativeList<Entity> laneSignals)
        {
            Node componentData = m_NodeData[node];
            Curve curve = m_CurveData[edge];
            float num = math.distancesq(componentData.m_Position, curve.m_Bezier.a);
            float num2 = math.distancesq(componentData.m_Position, curve.m_Bezier.d);
            bool flag = num <= num2;
            for (int i = 0; i < subNets.Length; i++)
            {
                Entity subNet = subNets[i].m_SubNet;
                if (m_NodeData.TryGetComponent(subNet, out componentData))
                {
                    float num3 = math.distancesq(componentData.m_Position, curve.m_Bezier.a);
                    num2 = math.distancesq(componentData.m_Position, curve.m_Bezier.d);
                    bool flag2 = num3 <= num2;
                    if (flag == flag2 && m_SubLanes.TryGetBuffer(subNet, out var bufferData))
                    {
                        FillLaneSignals(bufferData, laneSignals);
                    }
                }
            }
        }

        private void WriteTspDecisionTrace(
            int unfilteredChunkIndex,
            Entity currentEntity,
            TrafficLights trafficLights,
            TransitSignalPriorityRequest activeTspRequest,
            TspOverrideSelection tspSelection,
            CustomTrafficLights customTrafficLights,
            TspPedestrianFairnessState pedestrianFairnessState,
            TspVehicleFairnessState vehicleFairnessState)
        {
            bool exclusivePedestrianEnabled = IsExclusivePedestrianEnabled(customTrafficLights);
            var trace = new TransitSignalPriorityDecisionTrace
            {
                m_RequestTargetSignalGroup = activeTspRequest.m_TargetSignalGroup,
                m_SelectedSignalGroup = trafficLights.m_NextSignalGroup > 0
                    ? trafficLights.m_NextSignalGroup
                    : trafficLights.m_CurrentSignalGroup,
                m_BaseSignalGroup = tspSelection.BasePhaseIndex >= 0
                    ? (byte)(tspSelection.BasePhaseIndex + 1)
                    : (byte)0,
                m_SourceType = activeTspRequest.m_SourceType,
                m_OnDedicatedLane = activeTspRequest.m_OnDedicatedLane,
                m_Reason = (byte)tspSelection.Reason,
                m_ExclusivePedestrianEnabled = exclusivePedestrianEnabled,
                m_ActiveExclusivePedestrianPhase = TspPreemptionPolicy.ShouldProtectActivePedestrianPhase(
                    exclusivePedestrianEnabled,
                    trafficLights.m_CurrentSignalGroup,
                    customTrafficLights.m_PedestrianPhaseGroupMask,
                    isOngoing: trafficLights.m_State == Game.Net.TrafficLightState.Ongoing),
                m_PendingPedestrianFairness = pedestrianFairnessState.HasPendingPedestrianPhase,
                m_PendingPedestrianSignalGroup = pedestrianFairnessState.PendingPedestrianSignalGroup,
                m_PendingVehicleFairness = vehicleFairnessState.HasPendingVehiclePhase,
                m_PendingVehicleSignalGroup = vehicleFairnessState.PendingVehicleSignalGroup,
            };

            if (m_ExtraTypeHandle.m_TransitSignalPriorityDecisionTrace.HasComponent(currentEntity))
            {
                m_CommandBuffer.SetComponent(unfilteredChunkIndex, currentEntity, trace);
            }
            else
            {
                m_CommandBuffer.AddComponent(unfilteredChunkIndex, currentEntity, trace);
            }
        }

        private void WriteTspPedestrianFairnessState(
            int unfilteredChunkIndex,
            Entity currentEntity,
            TspPedestrianFairnessState pedestrianFairnessState)
        {
            if (!pedestrianFairnessState.HasPendingPedestrianPhase)
            {
                if (m_ExtraTypeHandle.m_TransitSignalPriorityPedestrianFairnessState.HasComponent(currentEntity))
                {
                    m_CommandBuffer.RemoveComponent<TransitSignalPriorityPedestrianFairnessState>(unfilteredChunkIndex, currentEntity);
                }

                return;
            }

            TransitSignalPriorityPedestrianFairnessState component =
                TransitSignalPriorityPedestrianFairnessState.FromLogicState(pedestrianFairnessState);
            if (m_ExtraTypeHandle.m_TransitSignalPriorityPedestrianFairnessState.HasComponent(currentEntity))
            {
                m_CommandBuffer.SetComponent(unfilteredChunkIndex, currentEntity, component);
            }
            else
            {
                m_CommandBuffer.AddComponent(unfilteredChunkIndex, currentEntity, component);
            }
        }

        private void WriteTspVehicleFairnessState(
            int unfilteredChunkIndex,
            Entity currentEntity,
            TspVehicleFairnessState vehicleFairnessState)
        {
            if (!vehicleFairnessState.HasPendingVehiclePhase)
            {
                if (m_ExtraTypeHandle.m_TransitSignalPriorityVehicleFairnessState.HasComponent(currentEntity))
                {
                    m_CommandBuffer.RemoveComponent<TransitSignalPriorityVehicleFairnessState>(unfilteredChunkIndex, currentEntity);
                }

                return;
            }

            TransitSignalPriorityVehicleFairnessState component =
                TransitSignalPriorityVehicleFairnessState.FromLogicState(vehicleFairnessState);
            if (m_ExtraTypeHandle.m_TransitSignalPriorityVehicleFairnessState.HasComponent(currentEntity))
            {
                m_CommandBuffer.SetComponent(unfilteredChunkIndex, currentEntity, component);
            }
            else
            {
                m_CommandBuffer.AddComponent(unfilteredChunkIndex, currentEntity, component);
            }
        }

        private bool UpdateTrafficLightState(
            NativeList<Entity> laneSignals,
            MoveableBridgeData moveableBridgeData,
            ref TrafficLights trafficLights,
            ref CustomTrafficLights customTrafficLights,
            C2VM.TrafficLightsEnhancement.Components.TransitSignalPrioritySettings tspSettings,
            bool hasTspRequest,
            TransitSignalPriorityRequest tspRequest,
            ref TspPedestrianFairnessState pedestrianFairnessState,
            ref TspVehicleFairnessState vehicleFairnessState,
            VanillaDemandSource demandSource,
            out TspOverrideSelection tspSelection)
        {
            tspSelection = default;
            bool canExtend;
            switch (trafficLights.m_State)
            {
                case Game.Net.TrafficLightState.None:
                    if (++trafficLights.m_Timer >= 1)
                    {
                        trafficLights.m_State = Game.Net.TrafficLightState.Beginning;
                        trafficLights.m_CurrentSignalGroup = 0;
                        trafficLights.m_NextSignalGroup = (byte)GetNextSignalGroup(laneSignals, trafficLights, preferChange: true, out canExtend, ref customTrafficLights, hasTspRequest, tspRequest, ref pedestrianFairnessState, ref vehicleFairnessState, demandSource, out tspSelection);
                        trafficLights.m_Timer = 0;
                        return true;
                    }

                    break;
                case Game.Net.TrafficLightState.Beginning:
                    if (++trafficLights.m_Timer >= 1)
                    {
                        trafficLights.m_State = Game.Net.TrafficLightState.Ongoing;
                        trafficLights.m_CurrentSignalGroup = trafficLights.m_NextSignalGroup;
                        trafficLights.m_NextSignalGroup = 0;
                        trafficLights.m_Timer = 0;
                        return true;
                    }

                    break;
                case Game.Net.TrafficLightState.Ongoing:
                    float greenDuration = 2;
                    if ((customTrafficLights.m_PedestrianPhaseGroupMask & 1 << trafficLights.m_CurrentSignalGroup - 1) != 0)
                    {
                        greenDuration *= customTrafficLights.m_PedestrianPhaseDurationMultiplier;
                    }
                    if (hasTspRequest)
                    {
                        int tspMinimumGreenDuration = TspRuntime.GetMinimumGreenDurationTicks(
                            (int)math.ceil(greenDuration),
                            trafficLights,
                            tspRequest,
                            protectActivePedestrianPhase: IsActiveExclusivePedestrianPhase(trafficLights, customTrafficLights));
                        if (tspMinimumGreenDuration < greenDuration)
                        {
                            greenDuration = tspMinimumGreenDuration;
                        }
                    }
                    #if VERBOSITY_DEBUG
                    System.Console.WriteLine($"UpdateTrafficLightState m_CurrentSignalGroup {trafficLights.m_CurrentSignalGroup} greenDuration {greenDuration} m_PedestrianPhaseGroupMask {customTrafficLights.m_PedestrianPhaseGroupMask}");
                    #endif
                    if (++trafficLights.m_Timer >= greenDuration)
                    {
                        if (TryApplyTspCurrentGroupHold(
                                ref trafficLights,
                                hasTspRequest,
                                tspRequest,
                                tspSettings,
                                customTrafficLights,
                                pedestrianFairnessState,
                                out tspSelection))
                        {
                            return false;
                        }

                        int num2 = 6;
                        if (moveableBridgeData.m_MovingTime != 0f)
                        {
                            num2 = math.clamp((int)(moveableBridgeData.m_MovingTime * 1.875f + 0.5f), num2, 255);
                        }

                        bool canExtend2;
                        int nextSignalGroup2 = GetNextSignalGroup(laneSignals, trafficLights, trafficLights.m_Timer >= num2, out canExtend2, ref customTrafficLights, hasTspRequest, tspRequest, ref pedestrianFairnessState, ref vehicleFairnessState, demandSource, out tspSelection);
                        if (nextSignalGroup2 != trafficLights.m_CurrentSignalGroup)
                        {
                            trafficLights.m_State = (canExtend2 ? Game.Net.TrafficLightState.Extending : Game.Net.TrafficLightState.Ending);
                            trafficLights.m_NextSignalGroup = (byte)nextSignalGroup2;
                            trafficLights.m_Timer = 0;
                            return true;
                        }

                        return false;
                    }

                    break;
                case Game.Net.TrafficLightState.Extending:
                    ++trafficLights.m_Timer;
                    if (TryApplyTspCurrentGroupHold(
                            ref trafficLights,
                            hasTspRequest,
                            tspRequest,
                            tspSettings,
                            customTrafficLights,
                            pedestrianFairnessState,
                            out tspSelection))
                    {
                        return true;
                    }

                    if (trafficLights.m_Timer >= 2)
                    {
                        bool canExtend4;
                        int nextSignalGroup4 = GetNextSignalGroup(laneSignals, trafficLights, preferChange: true, out canExtend4, ref customTrafficLights, hasTspRequest, tspRequest, ref pedestrianFairnessState, ref vehicleFairnessState, demandSource, out tspSelection);
                        if (nextSignalGroup4 == trafficLights.m_CurrentSignalGroup)
                        {
                            trafficLights.m_State = Game.Net.TrafficLightState.Beginning;
                            trafficLights.m_CurrentSignalGroup = 0;
                        }
                        else
                        {
                            trafficLights.m_State = (canExtend4 ? Game.Net.TrafficLightState.Extended : Game.Net.TrafficLightState.Ending);
                        }

                        trafficLights.m_NextSignalGroup = (byte)nextSignalGroup4;
                        trafficLights.m_Timer = 0;
                        return true;
                    }

                    break;
                case Game.Net.TrafficLightState.Extended:
                    ++trafficLights.m_Timer;
                    if (TryApplyTspCurrentGroupHold(
                            ref trafficLights,
                            hasTspRequest,
                            tspRequest,
                            tspSettings,
                            customTrafficLights,
                            pedestrianFairnessState,
                            out tspSelection))
                    {
                        return true;
                    }

                    if (trafficLights.m_Timer >= 2)
                    {
                        bool canExtend3;
                        int nextSignalGroup3 = GetNextSignalGroup(laneSignals, trafficLights, preferChange: true, out canExtend3, ref customTrafficLights, hasTspRequest, tspRequest, ref pedestrianFairnessState, ref vehicleFairnessState, demandSource, out tspSelection);
                        if (nextSignalGroup3 == trafficLights.m_CurrentSignalGroup)
                        {
                            trafficLights.m_State = Game.Net.TrafficLightState.Beginning;
                            trafficLights.m_CurrentSignalGroup = 0;
                            trafficLights.m_NextSignalGroup = (byte)nextSignalGroup3;
                            trafficLights.m_Timer = 0;
                            return true;
                        }

                        if (trafficLights.m_Timer >= 4 || !canExtend3)
                        {
                            trafficLights.m_State = Game.Net.TrafficLightState.Ending;
                            trafficLights.m_NextSignalGroup = (byte)nextSignalGroup3;
                            trafficLights.m_Timer = 0;
                            return true;
                        }

                        return false;
                    }

                    break;
                case Game.Net.TrafficLightState.Ending:
                    {
                        if (++trafficLights.m_Timer < 2)
                        {
                            break;
                        }

                        int nextSignalGroup5 = GetNextSignalGroup(laneSignals, trafficLights, preferChange: true, out canExtend, ref customTrafficLights, hasTspRequest, tspRequest, ref pedestrianFairnessState, ref vehicleFairnessState, demandSource, out tspSelection);
                        if ((trafficLights.m_Flags & TrafficLightFlags.MoveableBridge) != 0 && !IsEmpty(laneSignals, nextSignalGroup5))
                        {
                            return false;
                        }

                        if (nextSignalGroup5 != trafficLights.m_NextSignalGroup)
                        {
                            if (RequireEnding(laneSignals, nextSignalGroup5))
                            {
                                trafficLights.m_CurrentSignalGroup = trafficLights.m_NextSignalGroup;
                            }
                            else
                            {
                                trafficLights.m_State = Game.Net.TrafficLightState.Changing;
                            }

                            trafficLights.m_NextSignalGroup = (byte)nextSignalGroup5;
                        }
                        else
                        {
                            trafficLights.m_State = Game.Net.TrafficLightState.Changing;
                        }

                        trafficLights.m_Timer = 0;
                        return true;
                    }
                case Game.Net.TrafficLightState.Changing:
                    {
                        int num = 1;
                        if (moveableBridgeData.m_MovingTime != 0f && trafficLights.m_CurrentSignalGroup != trafficLights.m_NextSignalGroup)
                        {
                            num = math.clamp((int)(moveableBridgeData.m_MovingTime * 0.9375f + 0.5f), num, 255);
                        }

                        if (++trafficLights.m_Timer < num)
                        {
                            break;
                        }

                        int nextSignalGroup = GetNextSignalGroup(laneSignals, trafficLights, preferChange: true, out canExtend, ref customTrafficLights, hasTspRequest, tspRequest, ref pedestrianFairnessState, ref vehicleFairnessState, demandSource, out tspSelection);
                        if (nextSignalGroup != trafficLights.m_NextSignalGroup)
                        {
                            if (RequireEnding(laneSignals, nextSignalGroup))
                            {
                                trafficLights.m_CurrentSignalGroup = trafficLights.m_NextSignalGroup;
                                trafficLights.m_State = Game.Net.TrafficLightState.Ending;
                            }
                            else if (moveableBridgeData.m_MovingTime == 0f)
                            {
                                trafficLights.m_State = Game.Net.TrafficLightState.Beginning;
                            }
                            else
                            {
                                trafficLights.m_CurrentSignalGroup = trafficLights.m_NextSignalGroup;
                            }

                            trafficLights.m_NextSignalGroup = (byte)nextSignalGroup;
                        }
                        else
                        {
                            trafficLights.m_State = Game.Net.TrafficLightState.Beginning;
                        }

                        trafficLights.m_Timer = 0;
                        return true;
                    }
            }

            if (!demandSource.UseCollectedDemand)
            {
                ClearPriority(laneSignals);
            }
            return false;
        }

        private static bool TryApplyTspCurrentGroupHold(
            ref TrafficLights trafficLights,
            bool hasTspRequest,
            TransitSignalPriorityRequest tspRequest,
            C2VM.TrafficLightsEnhancement.Components.TransitSignalPrioritySettings tspSettings,
            CustomTrafficLights customTrafficLights,
            TspPedestrianFairnessState pedestrianFairnessState,
            out TspOverrideSelection tspSelection)
        {
            tspSelection = default;
            if (!hasTspRequest || !TspRuntime.ShouldHoldCurrentGroup(trafficLights, tspRequest, tspSettings.m_MaxGreenExtensionTicks))
            {
                return false;
            }

            if (TspPedestrianFairnessPolicy.ShouldSuppressCurrentGroupHold(
                    pedestrianFairnessState,
                    IsExclusivePedestrianEnabled(customTrafficLights),
                    customTrafficLights.m_PedestrianPhaseGroupMask,
                    trafficLights.m_CurrentSignalGroup))
            {
                return false;
            }

            int currentPhaseIndex = trafficLights.m_CurrentSignalGroup > 0
                ? trafficLights.m_CurrentSignalGroup - 1
                : -1;
            tspSelection = new TspOverrideSelection(
                currentPhaseIndex,
                currentPhaseIndex,
                canExtendCurrent: true,
                TspSelectionReason.ExtendedCurrentPhase);

            trafficLights.m_State = Game.Net.TrafficLightState.Ongoing;
            trafficLights.m_NextSignalGroup = 0;
            return true;
        }

        private bool RequireEnding(NativeList<Entity> laneSignals, int nextSignalGroup)
        {
            int num = 0;
            if (nextSignalGroup > 0)
            {
                num |= 1 << nextSignalGroup - 1;
            }

            for (int i = 0; i < laneSignals.Length; i++)
            {
                LaneSignal laneSignal = m_LaneSignalData[laneSignals[i]];
                if (laneSignal.m_Signal == LaneSignalType.Go && (laneSignal.m_GroupMask & num) == 0)
                {
                    return true;
                }
            }

            return false;
        }

        private int GetNextSignalGroup(
            NativeList<Entity> laneSignals,
            TrafficLights trafficLights,
            bool preferChange,
            out bool canExtend,
            ref CustomTrafficLights customTrafficLights,
            bool hasTspRequest,
            TransitSignalPriorityRequest tspRequest,
            ref TspPedestrianFairnessState pedestrianFairnessState,
            ref TspVehicleFairnessState vehicleFairnessState,
            VanillaDemandSource demandSource,
            out TspOverrideSelection tspSelection)
        {
            int nextSignalGroup = GetNextSignalGroupWithoutTsp(
                laneSignals,
                trafficLights,
                preferChange,
                out canExtend,
                ref customTrafficLights,
                demandSource);

            tspSelection = default;
            bool exclusivePedestrianEnabled = IsExclusivePedestrianEnabled(customTrafficLights);
            pedestrianFairnessState = TspPedestrianFairnessPolicy.Refresh(
                pedestrianFairnessState,
                exclusivePedestrianEnabled,
                customTrafficLights.m_PedestrianPhaseGroupMask,
                trafficLights.m_CurrentSignalGroup);
            if (pedestrianFairnessState.HasPendingPedestrianPhase)
            {
                byte pendingPedestrianGroup = pedestrianFairnessState.PendingPedestrianSignalGroup;
                byte inFlightSignalGroup = trafficLights.m_NextSignalGroup;
                if (!TspPedestrianFairnessPolicy.ShouldDeferToPendingPedestrianPhase(
                        pedestrianFairnessState,
                        exclusivePedestrianEnabled,
                        customTrafficLights.m_PedestrianPhaseGroupMask,
                        trafficLights.m_CurrentSignalGroup,
                        hasTspRequest ? tspRequest.m_TargetSignalGroup : (byte)0,
                        inFlightSignalGroup))
                {
                    return inFlightSignalGroup > 0 ? inFlightSignalGroup : nextSignalGroup;
                }

                if (hasTspRequest && tspRequest.m_TargetSignalGroup != pendingPedestrianGroup)
                {
                    tspSelection = new TspOverrideSelection(
                        basePhaseIndex: nextSignalGroup > 0 ? nextSignalGroup - 1 : -1,
                        selectedPhaseIndex: pendingPedestrianGroup - 1,
                        canExtendCurrent: false,
                        TspSelectionReason.DeferredForPedestrianFairness);
                }

                canExtend = false;
                return pendingPedestrianGroup;
            }

            vehicleFairnessState = TspVehicleFairnessPolicy.Refresh(
                vehicleFairnessState,
                trafficLights.m_SignalGroupCount,
                trafficLights.m_CurrentSignalGroup);
            if (vehicleFairnessState.HasPendingVehiclePhase)
            {
                byte pendingVehicleGroup = vehicleFairnessState.PendingVehicleSignalGroup;
                byte inFlightSignalGroup = trafficLights.m_NextSignalGroup;
                if (TspVehicleFairnessPolicy.ShouldDeferToPendingVehiclePhase(
                        vehicleFairnessState,
                        trafficLights.m_SignalGroupCount,
                        trafficLights.m_CurrentSignalGroup,
                        (byte)nextSignalGroup,
                        hasTspRequest ? tspRequest.m_TargetSignalGroup : (byte)0,
                        inFlightSignalGroup))
                {
                    if (hasTspRequest && tspRequest.m_TargetSignalGroup != pendingVehicleGroup)
                    {
                        tspSelection = new TspOverrideSelection(
                            basePhaseIndex: nextSignalGroup > 0 ? nextSignalGroup - 1 : -1,
                            selectedPhaseIndex: pendingVehicleGroup - 1,
                            canExtendCurrent: false,
                            TspSelectionReason.DeferredForVehicleFairness);
                    }

                    canExtend = false;
                    return pendingVehicleGroup;
                }
            }

            if (!hasTspRequest || !TspRuntime.ShouldApplyTargetGroupSelection(
                    tspRequest,
                    protectActivePedestrianPhase: IsActiveExclusivePedestrianPhase(trafficLights, customTrafficLights)))
            {
                return nextSignalGroup;
            }

            tspSelection = TspOverrideEngine.ApplySignalGroupOverride(
                baseSignalGroup: nextSignalGroup,
                currentSignalGroup: trafficLights.m_CurrentSignalGroup,
                signalGroupCount: trafficLights.m_SignalGroupCount,
                targetSignalGroup: tspRequest.m_TargetSignalGroup,
                new TspRequest(
                    (TspSource)tspRequest.m_SourceType,
                    tspRequest.m_Strength,
                    tspRequest.m_ExtendCurrentPhase),
                protectActivePedestrianPhase: IsActiveExclusivePedestrianPhase(trafficLights, customTrafficLights));

            if (tspSelection.Applied && tspSelection.SelectedPhaseIndex >= 0)
            {
                canExtend = tspSelection.CanExtendCurrent;
                byte selectedSignalGroup = (byte)(tspSelection.SelectedPhaseIndex + 1);
                pedestrianFairnessState = TspPedestrianFairnessPolicy.UpdateAfterSelection(
                    pedestrianFairnessState,
                    exclusivePedestrianEnabled,
                    customTrafficLights.m_PedestrianPhaseGroupMask,
                    trafficLights.m_CurrentSignalGroup,
                    (byte)nextSignalGroup,
                    selectedSignalGroup,
                    tspOverrideApplied: tspSelection.ChangedBaseSelection);
                vehicleFairnessState = TspVehicleFairnessPolicy.UpdateAfterSelection(
                    vehicleFairnessState,
                    trafficLights.m_SignalGroupCount,
                    trafficLights.m_CurrentSignalGroup,
                    (byte)nextSignalGroup,
                    selectedSignalGroup,
                    tspOverrideApplied: tspSelection.ChangedBaseSelection);
                return selectedSignalGroup;
            }

            return nextSignalGroup;
        }

        private static bool IsExclusivePedestrianEnabled(CustomTrafficLights customTrafficLights)
        {
            return ((uint)customTrafficLights.GetPattern() & (uint)CustomTrafficLights.Patterns.ExclusivePedestrian) != 0;
        }

        private static bool IsActiveExclusivePedestrianPhase(TrafficLights trafficLights, CustomTrafficLights customTrafficLights)
        {
            return TspPreemptionPolicy.ShouldProtectActivePedestrianPhase(
                exclusivePedestrianEnabled: IsExclusivePedestrianEnabled(customTrafficLights),
                trafficLights.m_CurrentSignalGroup,
                customTrafficLights.m_PedestrianPhaseGroupMask,
                isOngoing: trafficLights.m_State == Game.Net.TrafficLightState.Ongoing);
        }

        private int GetNextSignalGroupWithoutTsp(
            NativeList<Entity> laneSignals,
            TrafficLights trafficLights,
            bool preferChange,
            out bool canExtend,
            ref CustomTrafficLights customTrafficLights,
            VanillaDemandSource demandSource)
        {
            if (demandSource.UseCollectedDemand)
            {
                return VanillaTrafficGroupDemandPolicy.SelectNextPhase(
                    demandSource.Demand,
                    trafficLights.m_CurrentSignalGroup,
                    trafficLights.m_SignalGroupCount,
                    preferChange,
                    out canExtend);
            }

            Entity entity = Entity.Null;
            Entity entity2 = Entity.Null;
            int num = 0;
            int num2 = 0;
            int num3 = 0;
            int num4 = 0;
            int y = math.select(127, 1, (trafficLights.m_Flags & TrafficLightFlags.MoveableBridge) != 0);
            for (int i = 0; i < laneSignals.Length; i++)
            {
                Entity entity3 = laneSignals[i];
                LaneSignal value = m_LaneSignalData[entity3];

                ExtraLaneSignal extraLaneSignal = new ExtraLaneSignal();
                if (m_ExtraTypeHandle.m_ExtraLaneSignal.HasComponent(entity3))
                {
                    extraLaneSignal = m_ExtraTypeHandle.m_ExtraLaneSignal[entity3];
                }
                if ((value.m_GroupMask & (1 << trafficLights.m_CurrentSignalGroup - 1)) != 0)
                {
                    
                    if ((extraLaneSignal.m_IgnorePriorityGroupMask & (1 << trafficLights.m_CurrentSignalGroup - 1)) != 0)
                    {
                        value.m_Priority = value.m_Default;
                    }

                    
                    
                    
                    
                    
                }

                int num5 = math.min(value.m_Priority, y);
                if (num5 > num)
                {
                    entity = value.m_Petitioner;
                    num = num5;
                    num2 = value.m_GroupMask;
                    num3 = math.select(0, value.m_GroupMask, (value.m_Flags & LaneSignalFlags.CanExtend) != 0);
                }
                else if (num5 == num)
                {
                    num2 |= value.m_GroupMask;
                    num3 |= math.select(0, value.m_GroupMask, (value.m_Flags & LaneSignalFlags.CanExtend) != 0);
                }
                else if (num5 < 0)
                {
                    num4 |= value.m_GroupMask;
                }

                if (value.m_Blocker != Entity.Null)
                {
                    entity2 = value.m_Blocker;
                }

                value.m_Petitioner = Entity.Null;
                value.m_Priority = value.m_Default;
                m_LaneSignalData[entity3] = value;
            }

            if (entity != entity2)
            {
                for (int j = 0; j < laneSignals.Length; j++)
                {
                    Entity entity4 = laneSignals[j];
                    LaneSignal value2 = m_LaneSignalData[entity4];
                    if ((num2 & value2.m_GroupMask) != 0)
                    {
                        value2.m_Blocker = Entity.Null;
                    }
                    else
                    {
                        value2.m_Blocker = entity;
                    }

                    m_LaneSignalData[entity4] = value2;
                }
            }

            return VanillaTrafficGroupDemandPolicy.SelectNextPhase(
                new VanillaTrafficGroupDemand(num, num2, num3, num4),
                trafficLights.m_CurrentSignalGroup,
                trafficLights.m_SignalGroupCount,
                preferChange,
                out canExtend);
        }

        private void UpdateLaneSignals(
            NativeList<Entity> laneSignals,
            TrafficLights trafficLights,
            bool resetPriority = true)
        {
            for (int i = 0; i < laneSignals.Length; i++)
            {
                Entity entity = laneSignals[i];
                LaneSignal laneSignal = m_LaneSignalData[entity];
                ExtraLaneSignal extraLaneSignal = new ExtraLaneSignal();
                if (m_ExtraTypeHandle.m_ExtraLaneSignal.HasComponent(entity))
                {
                    extraLaneSignal = m_ExtraTypeHandle.m_ExtraLaneSignal[entity];
                }
                UpdateLaneSignal(trafficLights, ref laneSignal, ref extraLaneSignal);
                if (resetPriority)
                {
                    laneSignal.m_Petitioner = Entity.Null;
                    laneSignal.m_Priority = laneSignal.m_Default;
                }
                m_LaneSignalData[entity] = laneSignal;
            }
        }

        private bool FindMoveableBridge(DynamicBuffer<Game.Objects.SubObject> subObjects, out Entity entity, out MoveableBridgeData moveableBridgeData)
        {
            for (int i = 0; i < subObjects.Length; i++)
            {
                Entity subObject = subObjects[i].m_SubObject;
                if (m_PointOfInterestData.HasComponent(subObject))
                {
                    PrefabRef prefabRef = m_PrefabRefData[subObject];
                    if (m_PrefabMoveableBridgeData.TryGetComponent(prefabRef.m_Prefab, out moveableBridgeData))
                    {
                        entity = subObject;
                        return true;
                    }
                }
            }

            entity = default(Entity);
            moveableBridgeData = default(MoveableBridgeData);
            return false;
        }

        private void UpdateTrafficLightObjects(DynamicBuffer<Game.Objects.SubObject> subObjects, TrafficLights trafficLights)
        {
            for (int i = 0; i < subObjects.Length; i++)
            {
                Entity subObject = subObjects[i].m_SubObject;
                if (m_TrafficLightData.TryGetComponent(subObject, out var componentData))
                {
                    PatchedTrafficLightSystem.UpdateTrafficLightState(trafficLights, ref componentData);
                    m_TrafficLightData[subObject] = componentData;
                }
            }
        }

        private void ClearPriority(NativeList<Entity> laneSignals)
        {
            for (int i = 0; i < laneSignals.Length; i++)
            {
                Entity entity = laneSignals[i];
                LaneSignal value = m_LaneSignalData[entity];
                value.m_Petitioner = Entity.Null;
                value.m_Priority = value.m_Default;
                m_LaneSignalData[entity] = value;
            }
        }

        private bool IsEmpty(NativeList<Entity> laneSignals, int nextSignalGroup)
        {
            bool result = true;
            if (nextSignalGroup > 0)
            {
                int num = 1 << nextSignalGroup - 1;
                Entity blocker = Entity.Null;
                for (int i = 0; i < laneSignals.Length; i++)
                {
                    Entity entity = laneSignals[i];
                    if ((m_LaneSignalData[entity].m_GroupMask & num) != 0)
                    {
                        continue;
                    }

                    if (m_LaneObjects.TryGetBuffer(entity, out var bufferData) && bufferData.Length != 0)
                    {
                        blocker = bufferData[0].m_LaneObject;
                        result = false;
                        break;
                    }

                    if (m_LaneReservationData.TryGetComponent(entity, out var componentData) && componentData.GetPriority() >= 100)
                    {
                        blocker = componentData.m_Blocker;
                        result = false;
                        if (blocker != Entity.Null)
                        {
                            break;
                        }
                    }

                    if (m_PrefabRefData.TryGetComponent(entity, out var componentData2) && m_PrefabCarLaneData.TryGetComponent(componentData2.m_Prefab, out var componentData3) && (componentData3.m_RoadTypes & RoadTypes.Watercraft) != RoadTypes.None && CheckNextLane(Entity.Null, entity, 0f, 0, out blocker))
                    {
                        result = false;
                        break;
                    }
                }

                if (blocker != Entity.Null)
                {
                    for (int j = 0; j < laneSignals.Length; j++)
                    {
                        Entity entity2 = laneSignals[j];
                        LaneSignal value = m_LaneSignalData[entity2];
                        if (value.m_Blocker == Entity.Null)
                        {
                            value.m_Blocker = blocker;
                            m_LaneSignalData[entity2] = value;
                        }
                    }
                }
            }

            return result;
        }

        private bool CheckNextLane(Entity prevOwner, Entity lane, float distance, int depth, out Entity blocker)
        {
            if (m_OwnerData.TryGetComponent(lane, out var componentData))
            {
                Edge componentData2;
                if (m_ConnectedEdges.TryGetBuffer(componentData.m_Owner, out var bufferData))
                {
                    for (int i = 0; i < bufferData.Length; i++)
                    {
                        ConnectedEdge connectedEdge = bufferData[i];
                        if (!(connectedEdge.m_Edge == prevOwner) && CheckNextLane(componentData.m_Owner, connectedEdge.m_Edge, lane, distance, depth, out blocker))
                        {
                            return true;
                        }
                    }
                }
                else if (m_EdgeData.TryGetComponent(componentData.m_Owner, out componentData2) && (componentData2.m_Start == prevOwner || componentData2.m_End == prevOwner))
                {
                    if (CheckNextLane(componentData.m_Owner, (componentData2.m_End == prevOwner) ? componentData2.m_Start : componentData2.m_End, lane, distance, depth, out blocker))
                    {
                        return true;
                    }

                    if (CheckNextLane(prevOwner, componentData.m_Owner, lane, distance, depth, out blocker))
                    {
                        return true;
                    }
                }
            }

            blocker = Entity.Null;
            return false;
        }

        private bool CheckNextLane(Entity prevOwner, Entity nextOwner, Entity lane, float distance, int depth, out Entity blocker)
        {
            if (m_SubLanes.TryGetBuffer(nextOwner, out var bufferData) && m_LaneData.TryGetComponent(lane, out var componentData))
            {
                for (int i = 0; i < bufferData.Length; i++)
                {
                    Entity subLane = bufferData[i].m_SubLane;
                    if (!m_LaneData.TryGetComponent(subLane, out var componentData2) || !componentData.m_EndNode.Equals(componentData2.m_StartNode) || !m_CurveData.TryGetComponent(subLane, out var componentData3) || !m_LaneObjects.TryGetBuffer(subLane, out var bufferData2))
                    {
                        continue;
                    }

                    for (int j = 0; j < bufferData2.Length; j++)
                    {
                        LaneObject laneObject = bufferData2[j];
                        if (m_PrefabRefData.TryGetComponent(laneObject.m_LaneObject, out var componentData4) && m_PrefabObjectGeometryData.TryGetComponent(componentData4.m_Prefab, out var componentData5))
                        {
                            float3 x = MathUtils.Position(componentData3.m_Bezier, laneObject.m_CurvePosition.x);
                            float3 @float = MathUtils.Size(componentData5.m_Bounds);
                            if (math.distance(x, componentData3.m_Bezier.a) + distance < @float.z - @float.x * 0.25f)
                            {
                                blocker = laneObject.m_LaneObject;
                                return true;
                            }
                        }
                    }

                    float num = distance + componentData3.m_Length;
                    if (num < 150f && depth < 3 && CheckNextLane(prevOwner, subLane, num, depth + 1, out blocker))
                    {
                        return true;
                    }
                }
            }

            blocker = Entity.Null;
            return false;
        }

        void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
        }
    }

    private struct TypeHandle
    {
        [ReadOnly]
        public EntityTypeHandle __Unity_Entities_Entity_TypeHandle;

        [ReadOnly]
        public BufferTypeHandle<Game.Net.SubLane> __Game_Net_SubLane_RO_BufferTypeHandle;

        [ReadOnly]
        public BufferTypeHandle<ConnectedEdge> __Game_Net_ConnectedEdge_RO_BufferTypeHandle;

        [ReadOnly]
        public BufferTypeHandle<Game.Objects.SubObject> __Game_Objects_SubObject_RO_BufferTypeHandle;

        public ComponentTypeHandle<TrafficLights> __Game_Net_TrafficLights_RW_ComponentTypeHandle;

        [ReadOnly]
        public ComponentLookup<Owner> __Game_Common_Owner_RO_ComponentLookup;

        [ReadOnly]
        public ComponentLookup<Node> __Game_Net_Node_RO_ComponentLookup;

        [ReadOnly]
        public ComponentLookup<Edge> __Game_Net_Edge_RO_ComponentLookup;

        [ReadOnly]
        public ComponentLookup<Curve> __Game_Net_Curve_RO_ComponentLookup;

        [ReadOnly]
        public ComponentLookup<Lane> __Game_Net_Lane_RO_ComponentLookup;

        [ReadOnly]
        public ComponentLookup<LaneReservation> __Game_Net_LaneReservation_RO_ComponentLookup;

        [ReadOnly]
        public ComponentLookup<Transform> __Game_Objects_Transform_RO_ComponentLookup;

        [ReadOnly]
        public ComponentLookup<PrefabRef> __Game_Prefabs_PrefabRef_RO_ComponentLookup;

        [ReadOnly]
        public ComponentLookup<CarLaneData> __Game_Prefabs_CarLaneData_RO_ComponentLookup;

        [ReadOnly]
        public ComponentLookup<MoveableBridgeData> __Game_Prefabs_MoveableBridgeData_RO_ComponentLookup;

        [ReadOnly]
        public ComponentLookup<ObjectGeometryData> __Game_Prefabs_ObjectGeometryData_RO_ComponentLookup;

        [ReadOnly]
        public BufferLookup<LaneObject> __Game_Net_LaneObject_RO_BufferLookup;

        [ReadOnly]
        public BufferLookup<Game.Net.SubNet> __Game_Net_SubNet_RO_BufferLookup;

        [ReadOnly]
        public BufferLookup<Game.Net.SubLane> __Game_Net_SubLane_RO_BufferLookup;

        [ReadOnly]
        public BufferLookup<ConnectedEdge> __Game_Net_ConnectedEdge_RO_BufferLookup;

        public ComponentLookup<LaneSignal> __Game_Net_LaneSignal_RW_ComponentLookup;

        public ComponentLookup<TrafficLight> __Game_Objects_TrafficLight_RW_ComponentLookup;

        public ComponentLookup<PointOfInterest> __Game_Common_PointOfInterest_RW_ComponentLookup;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void __AssignHandles(ref SystemState state)
        {
            __Unity_Entities_Entity_TypeHandle = state.GetEntityTypeHandle();
            __Game_Net_SubLane_RO_BufferTypeHandle = state.GetBufferTypeHandle<Game.Net.SubLane>(isReadOnly: true);
            __Game_Net_ConnectedEdge_RO_BufferTypeHandle = state.GetBufferTypeHandle<ConnectedEdge>(isReadOnly: true);
            __Game_Objects_SubObject_RO_BufferTypeHandle = state.GetBufferTypeHandle<Game.Objects.SubObject>(isReadOnly: true);
            __Game_Net_TrafficLights_RW_ComponentTypeHandle = state.GetComponentTypeHandle<TrafficLights>();
            __Game_Common_Owner_RO_ComponentLookup = state.GetComponentLookup<Owner>(isReadOnly: true);
            __Game_Net_Node_RO_ComponentLookup = state.GetComponentLookup<Node>(isReadOnly: true);
            __Game_Net_Edge_RO_ComponentLookup = state.GetComponentLookup<Edge>(isReadOnly: true);
            __Game_Net_Curve_RO_ComponentLookup = state.GetComponentLookup<Curve>(isReadOnly: true);
            __Game_Net_Lane_RO_ComponentLookup = state.GetComponentLookup<Lane>(isReadOnly: true);
            __Game_Net_LaneReservation_RO_ComponentLookup = state.GetComponentLookup<LaneReservation>(isReadOnly: true);
            __Game_Objects_Transform_RO_ComponentLookup = state.GetComponentLookup<Transform>(isReadOnly: true);
            __Game_Prefabs_PrefabRef_RO_ComponentLookup = state.GetComponentLookup<PrefabRef>(isReadOnly: true);
            __Game_Prefabs_CarLaneData_RO_ComponentLookup = state.GetComponentLookup<CarLaneData>(isReadOnly: true);
            __Game_Prefabs_MoveableBridgeData_RO_ComponentLookup = state.GetComponentLookup<MoveableBridgeData>(isReadOnly: true);
            __Game_Prefabs_ObjectGeometryData_RO_ComponentLookup = state.GetComponentLookup<ObjectGeometryData>(isReadOnly: true);
            __Game_Net_LaneObject_RO_BufferLookup = state.GetBufferLookup<LaneObject>(isReadOnly: true);
            __Game_Net_SubNet_RO_BufferLookup = state.GetBufferLookup<Game.Net.SubNet>(isReadOnly: true);
            __Game_Net_SubLane_RO_BufferLookup = state.GetBufferLookup<Game.Net.SubLane>(isReadOnly: true);
            __Game_Net_ConnectedEdge_RO_BufferLookup = state.GetBufferLookup<ConnectedEdge>(isReadOnly: true);
            __Game_Net_LaneSignal_RW_ComponentLookup = state.GetComponentLookup<LaneSignal>();
            __Game_Objects_TrafficLight_RW_ComponentLookup = state.GetComponentLookup<TrafficLight>();
            __Game_Common_PointOfInterest_RW_ComponentLookup = state.GetComponentLookup<PointOfInterest>();
        }
    }

    private const uint UPDATE_INTERVAL = 64u;

    public SimulationSystem m_SimulationSystem;

    private EndFrameBarrier m_EndFrameBarrier;

    private EntityQuery m_TrafficLightQuery;

    private EntityQuery m_GroupedTrafficLightQuery;

    private EntityQuery m_RailTransitQuery;

    private EntityQuery m_BusTransitQuery;

    private EntityQuery m_TransitSignalPrioritySettingsQuery;

    private TypeHandle __TypeHandle;

    private ExtraTypeHandle m_ExtraTypeHandle;

    public TimeSystem m_TimeSystem;

    private struct HasApproachIndexEligibleTransitSignalPrioritySettingsJob : IJobChunk
    {
        [ReadOnly]
        public EntityTypeHandle m_EntityType;

        [ReadOnly]
        public ComponentTypeHandle<C2VM.TrafficLightsEnhancement.Components.TransitSignalPrioritySettings> m_TransitSignalPrioritySettingsType;

        [ReadOnly]
        public ComponentLookup<TrafficGroupMember> m_TrafficGroupMemberLookup;

        public bool m_RequirePublicCarRequests;

        public NativeArray<int> m_Result;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            if (m_Result[0] != 0)
            {
                return;
            }

            NativeArray<Entity> entities = chunk.GetNativeArray(m_EntityType);
            NativeArray<C2VM.TrafficLightsEnhancement.Components.TransitSignalPrioritySettings> settings =
                chunk.GetNativeArray(ref m_TransitSignalPrioritySettingsType);

            for (int i = 0; i < settings.Length; i++)
            {
                Entity entity = entities[i];
                bool isGroupedIntersection = m_TrafficGroupMemberLookup.HasComponent(entity);

                var logicSettings = settings[i].ToLogicSettings();
                bool isEligible = m_RequirePublicCarRequests
                    ? TspPolicy.IsBusApproachIndexEligibleSetting(logicSettings, isGroupedIntersection)
                    : TspPolicy.IsApproachIndexEligibleSetting(logicSettings, isGroupedIntersection);

                if (isEligible)
                {
                    m_Result[0] = 1;
                    return;
                }
            }
        }
    }

    public override int GetUpdateInterval(SystemUpdatePhase phase)
    {
        return 4;
    }

    [Preserve]
    protected override void OnCreate()
    {
        base.OnCreate();
        m_SimulationSystem = base.World.GetOrCreateSystemManaged<SimulationSystem>();
        m_EndFrameBarrier = base.World.GetOrCreateSystemManaged<EndFrameBarrier>();
        m_TimeSystem = base.World.GetOrCreateSystemManaged<TimeSystem>();
        m_TrafficLightQuery = GetEntityQuery(ComponentType.ReadWrite<TrafficLights>(), ComponentType.ReadOnly<UpdateFrame>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Destroyed>(), ComponentType.Exclude<Temp>());
        m_GroupedTrafficLightQuery = GetEntityQuery(
            ComponentType.ReadWrite<TrafficLights>(),
            ComponentType.ReadOnly<UpdateFrame>(),
            ComponentType.ReadOnly<TrafficGroupMember>(),
            ComponentType.Exclude<Deleted>(),
            ComponentType.Exclude<Destroyed>(),
            ComponentType.Exclude<Temp>());
        m_RailTransitQuery = GetEntityQuery(
            ComponentType.ReadOnly<TrainCurrentLane>(),
            ComponentType.ReadOnly<TrainNavigation>(),
            ComponentType.ReadOnly<Game.Vehicles.PublicTransport>(),
            ComponentType.Exclude<Deleted>(),
            ComponentType.Exclude<Destroyed>(),
            ComponentType.Exclude<Temp>());
        m_BusTransitQuery = GetEntityQuery(
            ComponentType.ReadOnly<CarCurrentLane>(),
            ComponentType.ReadOnly<PassengerTransport>(),
            ComponentType.ReadOnly<Game.Vehicles.PublicTransport>(),
            ComponentType.ReadOnly<PrefabRef>(),
            ComponentType.Exclude<Deleted>(),
            ComponentType.Exclude<Destroyed>(),
            ComponentType.Exclude<Temp>());
        m_TransitSignalPrioritySettingsQuery = GetEntityQuery(
            ComponentType.ReadOnly<C2VM.TrafficLightsEnhancement.Components.TransitSignalPrioritySettings>(),
            ComponentType.ReadOnly<TrafficLights>(),
            ComponentType.Exclude<Deleted>(),
            ComponentType.Exclude<Destroyed>(),
            ComponentType.Exclude<Temp>());
        RequireForUpdate(m_TrafficLightQuery);
    }

    [Preserve]
    protected override void OnUpdate()
    {
        m_TrafficLightQuery.ResetFilter();
        uint updateFrameIndex = SimulationUtils.GetUpdateFrameWithInterval(
            m_SimulationSystem.frameIndex,
            (uint)GetUpdateInterval(SystemUpdatePhase.GameSimulation),
            16);
        m_TrafficLightQuery.SetSharedComponentFilter(new UpdateFrame(updateFrameIndex));
        var updatedExtraTypeHandle = m_ExtraTypeHandle.Update(ref base.CheckedStateRef);
        bool hasTransitSignalPrioritySettings = !m_TransitSignalPrioritySettingsQuery.IsEmptyIgnoreFilter;
        bool shouldBuildTramApproachIndex = TspPolicy.ShouldBuildApproachIndex(
            hasTransitSignalPrioritySettings,
            hasApproachIndexEligibleTransitSignalPrioritySettings:
                HasApproachIndexEligibleTransitSignalPrioritySettings(requirePublicCarRequests: false));
        bool showTransitSignalPriorityDiagnostics = Mod.m_Setting != null && Mod.m_Setting.m_ShowTransitSignalPriorityDiagnostics;
        bool shouldBuildBusApproachIndex = showTransitSignalPriorityDiagnostics
            || HasApproachIndexEligibleTransitSignalPrioritySettings(requirePublicCarRequests: true);
        var tramApproachIndex = shouldBuildTramApproachIndex
            ? TramApproachIndex.Build(
                m_RailTransitQuery,
                updatedExtraTypeHandle,
                Allocator.TempJob)
            : new NativeParallelHashMap<Entity, float>(1, Allocator.TempJob);
        int tramApproachIndexLaneCount = tramApproachIndex.Count();
        var busApproachIndex = shouldBuildBusApproachIndex
            ? BusApproachIndex.Build(
                m_BusTransitQuery,
                updatedExtraTypeHandle,
                Allocator.TempJob)
            : new NativeParallelHashMap<Entity, BusApproachSample>(1, Allocator.TempJob);
        int busApproachIndexLaneCount = busApproachIndex.Count();
        int trafficLightCount = math.max(1, m_GroupedTrafficLightQuery.CalculateEntityCount());
        var localGroupedDemand = new NativeParallelHashMap<Entity, VanillaTrafficGroupDemand>(
            trafficLightCount,
            Allocator.Persistent);
        var groupedDemand = new NativeParallelMultiHashMap<Entity, VanillaTrafficGroupDemand>(
            trafficLightCount,
            Allocator.Persistent);
        var sameTickMasterState = new NativeParallelHashMap<Entity, TrafficGroupMasterSignalState>(
            trafficLightCount,
            Allocator.Persistent);

        var updateJob = new UpdateTrafficLightsJob
        {
            m_EntityType = InternalCompilerInterface.GetEntityTypeHandle(ref __TypeHandle.__Unity_Entities_Entity_TypeHandle, ref base.CheckedStateRef),
            m_SubLaneType = InternalCompilerInterface.GetBufferTypeHandle(ref __TypeHandle.__Game_Net_SubLane_RO_BufferTypeHandle, ref base.CheckedStateRef),
            m_ConnectedEdgeType = InternalCompilerInterface.GetBufferTypeHandle(ref __TypeHandle.__Game_Net_ConnectedEdge_RO_BufferTypeHandle, ref base.CheckedStateRef),
            m_SubObjectType = InternalCompilerInterface.GetBufferTypeHandle(ref __TypeHandle.__Game_Objects_SubObject_RO_BufferTypeHandle, ref base.CheckedStateRef),
            m_TrafficLightsType = InternalCompilerInterface.GetComponentTypeHandle(ref __TypeHandle.__Game_Net_TrafficLights_RW_ComponentTypeHandle, ref base.CheckedStateRef),
            m_OwnerData = InternalCompilerInterface.GetComponentLookup(ref __TypeHandle.__Game_Common_Owner_RO_ComponentLookup, ref base.CheckedStateRef),
            m_NodeData = InternalCompilerInterface.GetComponentLookup(ref __TypeHandle.__Game_Net_Node_RO_ComponentLookup, ref base.CheckedStateRef),
            m_EdgeData = InternalCompilerInterface.GetComponentLookup(ref __TypeHandle.__Game_Net_Edge_RO_ComponentLookup, ref base.CheckedStateRef),
            m_CurveData = InternalCompilerInterface.GetComponentLookup(ref __TypeHandle.__Game_Net_Curve_RO_ComponentLookup, ref base.CheckedStateRef),
            m_LaneData = InternalCompilerInterface.GetComponentLookup(ref __TypeHandle.__Game_Net_Lane_RO_ComponentLookup, ref base.CheckedStateRef),
            m_LaneReservationData = InternalCompilerInterface.GetComponentLookup(ref __TypeHandle.__Game_Net_LaneReservation_RO_ComponentLookup, ref base.CheckedStateRef),
            m_TransformData = InternalCompilerInterface.GetComponentLookup(ref __TypeHandle.__Game_Objects_Transform_RO_ComponentLookup, ref base.CheckedStateRef),
            m_PrefabRefData = InternalCompilerInterface.GetComponentLookup(ref __TypeHandle.__Game_Prefabs_PrefabRef_RO_ComponentLookup, ref base.CheckedStateRef),
            m_PrefabCarLaneData = InternalCompilerInterface.GetComponentLookup(ref __TypeHandle.__Game_Prefabs_CarLaneData_RO_ComponentLookup, ref base.CheckedStateRef),
            m_PrefabMoveableBridgeData = InternalCompilerInterface.GetComponentLookup(ref __TypeHandle.__Game_Prefabs_MoveableBridgeData_RO_ComponentLookup, ref base.CheckedStateRef),
            m_PrefabObjectGeometryData = InternalCompilerInterface.GetComponentLookup(ref __TypeHandle.__Game_Prefabs_ObjectGeometryData_RO_ComponentLookup, ref base.CheckedStateRef),
            m_LaneObjects = InternalCompilerInterface.GetBufferLookup(ref __TypeHandle.__Game_Net_LaneObject_RO_BufferLookup, ref base.CheckedStateRef),
            m_SubNets = InternalCompilerInterface.GetBufferLookup(ref __TypeHandle.__Game_Net_SubNet_RO_BufferLookup, ref base.CheckedStateRef),
            m_SubLanes = InternalCompilerInterface.GetBufferLookup(ref __TypeHandle.__Game_Net_SubLane_RO_BufferLookup, ref base.CheckedStateRef),
            m_ConnectedEdges = InternalCompilerInterface.GetBufferLookup(ref __TypeHandle.__Game_Net_ConnectedEdge_RO_BufferLookup, ref base.CheckedStateRef),
            m_LaneSignalData = InternalCompilerInterface.GetComponentLookup(ref __TypeHandle.__Game_Net_LaneSignal_RW_ComponentLookup, ref base.CheckedStateRef),
            m_TrafficLightData = InternalCompilerInterface.GetComponentLookup(ref __TypeHandle.__Game_Objects_TrafficLight_RW_ComponentLookup, ref base.CheckedStateRef),
            m_PointOfInterestData = InternalCompilerInterface.GetComponentLookup(ref __TypeHandle.__Game_Common_PointOfInterest_RW_ComponentLookup, ref base.CheckedStateRef),
            m_TramApproachIndex = tramApproachIndex.AsReadOnly(),
            m_TramApproachIndexLaneCount = tramApproachIndexLaneCount,
            m_BusApproachIndex = busApproachIndex.AsReadOnly(),
            m_BusApproachIndexLaneCount = busApproachIndexLaneCount,
            m_TransitSignalPriorityDiagnosticsEnabled = showTransitSignalPriorityDiagnostics,
            m_CommandBuffer = m_EndFrameBarrier.CreateCommandBuffer().AsParallelWriter(),
            m_ExtraTypeHandle = updatedExtraTypeHandle,
            m_ExtraData = new ExtraData(this),
            m_UpdateFrameIndex = updateFrameIndex,
            m_SimulationFrame = m_SimulationSystem.frameIndex,
            m_LocalGroupedDemand = localGroupedDemand,
            m_GroupedDemand = groupedDemand,
            m_SameTickMasterState = sameTickMasterState
        };

        // These passes intentionally stay single-threaded because they share mutable native maps.
        // Parallel scheduling previously required disabling container safety and correlated with allocator crashes after group creation.
        updateJob.m_Pass = TrafficLightUpdatePass.CollectGroupedBaseDemand;
        JobHandle collectDependency = JobChunkExtensions.Schedule(
            updateJob,
            m_GroupedTrafficLightQuery,
            base.Dependency);

        updateJob.m_Pass = TrafficLightUpdatePass.UpdateLeadersAndIndependent;
        JobHandle leaderDependency = JobChunkExtensions.Schedule(
            updateJob,
            m_TrafficLightQuery,
            collectDependency);

        updateJob.m_Pass = TrafficLightUpdatePass.SynchronizeGroupedBaseFollowers;
        JobHandle followerDependency = JobChunkExtensions.Schedule(
            updateJob,
            m_GroupedTrafficLightQuery,
            leaderDependency);

        JobHandle disposeTramIndexDependency = tramApproachIndex.Dispose(followerDependency);
        JobHandle disposeBusIndexDependency = busApproachIndex.Dispose(followerDependency);
        JobHandle disposeLocalDemandDependency = localGroupedDemand.Dispose(followerDependency);
        JobHandle disposeGroupedDemandDependency = groupedDemand.Dispose(followerDependency);
        JobHandle disposeMasterStateDependency = sameTickMasterState.Dispose(followerDependency);
        JobHandle disposeIndexDependency = JobHandle.CombineDependencies(
            disposeTramIndexDependency,
            disposeBusIndexDependency);
        JobHandle disposeDemandDependency = JobHandle.CombineDependencies(
            disposeLocalDemandDependency,
            disposeGroupedDemandDependency);
        base.Dependency = JobHandle.CombineDependencies(
            disposeIndexDependency,
            JobHandle.CombineDependencies(
                disposeDemandDependency,
                disposeMasterStateDependency));
        m_EndFrameBarrier.AddJobHandleForProducer(base.Dependency);
    }

    private bool HasApproachIndexEligibleTransitSignalPrioritySettings(bool requirePublicCarRequests)
    {
        if (m_TransitSignalPrioritySettingsQuery.IsEmptyIgnoreFilter)
        {
            return false;
        }

        using NativeArray<int> result = new NativeArray<int>(1, Allocator.TempJob);
        var job = new HasApproachIndexEligibleTransitSignalPrioritySettingsJob
        {
            m_EntityType = GetEntityTypeHandle(),
            m_TransitSignalPrioritySettingsType =
                GetComponentTypeHandle<C2VM.TrafficLightsEnhancement.Components.TransitSignalPrioritySettings>(isReadOnly: true),
            m_TrafficGroupMemberLookup = GetComponentLookup<TrafficGroupMember>(isReadOnly: true),
            m_RequirePublicCarRequests = requirePublicCarRequests,
            m_Result = result
        };
        job.Run(m_TransitSignalPrioritySettingsQuery);

        return result[0] != 0;
    }

    public void SetCompatibilityMode(bool enable)
    {
        if (enable)
        {
            m_TrafficLightQuery = GetEntityQuery
            (
                ComponentType.ReadWrite<TrafficLights>(),
                ComponentType.ReadWrite<CustomTrafficLights>(),
                ComponentType.ReadOnly<UpdateFrame>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Destroyed>(),
                ComponentType.Exclude<Temp>()
            );
        }
        else
        {
            m_TrafficLightQuery = GetEntityQuery
            (
                ComponentType.ReadWrite<TrafficLights>(),
                ComponentType.ReadOnly<UpdateFrame>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Destroyed>(),
                ComponentType.Exclude<Temp>()
            );
        }
    }

    public static void UpdateLaneSignal(TrafficLights trafficLights, ref LaneSignal laneSignal)
    {
        ExtraLaneSignal extraLaneSignal = new();
        UpdateLaneSignal(trafficLights, ref laneSignal, ref extraLaneSignal);
    }

    public static void UpdateLaneSignal(TrafficLights trafficLights, ref LaneSignal laneSignal, ref ExtraLaneSignal extraLaneSignal)
    {
        int num = 0;
        int num2 = 0;
        if (trafficLights.m_CurrentSignalGroup > 0)
        {
            num |= 1 << trafficLights.m_CurrentSignalGroup - 1;
        }

        if (trafficLights.m_NextSignalGroup > 0)
        {
            num2 |= 1 << trafficLights.m_NextSignalGroup - 1;
        }

        LaneSignalType goSignalType = LaneSignalType.Go;

        if ((extraLaneSignal.m_YieldGroupMask & (1 << trafficLights.m_CurrentSignalGroup - 1)) != 0)
        {
            goSignalType = LaneSignalType.Yield;
        }

        switch (trafficLights.m_State)
        {
            case Game.Net.TrafficLightState.Beginning:
                if ((laneSignal.m_GroupMask & num2) != 0)
                {
                    if (laneSignal.m_Signal != goSignalType)
                    {
                        laneSignal.m_Signal = LaneSignalType.Yield;
                    }
                }
                else
                {
                    laneSignal.m_Signal = LaneSignalType.Stop;
                }

                break;
            case Game.Net.TrafficLightState.Ongoing:
                if ((laneSignal.m_GroupMask & num) != 0)
                {
                    laneSignal.m_Signal = goSignalType;
                }
                else
                {
                    laneSignal.m_Signal = LaneSignalType.Stop;
                }

                break;
            case Game.Net.TrafficLightState.Extending:
                if ((laneSignal.m_Flags & LaneSignalFlags.CanExtend) != 0)
                {
                    if ((laneSignal.m_GroupMask & num) != 0)
                    {
                        laneSignal.m_Signal = goSignalType;
                    }
                    else
                    {
                        laneSignal.m_Signal = LaneSignalType.Stop;
                    }
                }
                else if (laneSignal.m_Signal == goSignalType)
                {
                    if ((laneSignal.m_GroupMask & num2) == 0)
                    {
                        laneSignal.m_Signal = LaneSignalType.SafeStop;
                    }
                }
                else
                {
                    laneSignal.m_Signal = LaneSignalType.Stop;
                }

                break;
            case Game.Net.TrafficLightState.Extended:
                if ((laneSignal.m_Flags & LaneSignalFlags.CanExtend) != 0 && (laneSignal.m_GroupMask & num) != 0)
                {
                    laneSignal.m_Signal = goSignalType;
                }
                else
                {
                    laneSignal.m_Signal = LaneSignalType.Stop;
                }

                break;
            case Game.Net.TrafficLightState.Ending:
                if (laneSignal.m_Signal == goSignalType)
                {
                    if ((laneSignal.m_GroupMask & num2) == 0)
                    {
                        laneSignal.m_Signal = LaneSignalType.SafeStop;
                    }
                }
                else
                {
                    laneSignal.m_Signal = LaneSignalType.Stop;
                }

                break;
            case Game.Net.TrafficLightState.Changing:
                if (laneSignal.m_Signal != goSignalType || (laneSignal.m_GroupMask & num2) == 0)
                {
                    laneSignal.m_Signal = LaneSignalType.Stop;
                }

                break;
            default:
                laneSignal.m_Signal = LaneSignalType.None;
                break;
        }
    }

    public static void UpdateTrafficLightState(TrafficLights trafficLights, ref TrafficLight trafficLight)
    {
        int num = 0;
        int num2 = 0;
        if (trafficLights.m_CurrentSignalGroup > 0)
        {
            num |= 1 << trafficLights.m_CurrentSignalGroup - 1;
        }

        if (trafficLights.m_NextSignalGroup > 0)
        {
            num2 |= 1 << trafficLights.m_NextSignalGroup - 1;
        }

        Game.Objects.TrafficLightState trafficLightState = trafficLight.m_State & (Game.Objects.TrafficLightState.Red | Game.Objects.TrafficLightState.Yellow | Game.Objects.TrafficLightState.Green | Game.Objects.TrafficLightState.Flashing);
        Game.Objects.TrafficLightState trafficLightState2 = (Game.Objects.TrafficLightState)(((int)trafficLight.m_State >> 4) & 0xF);
        Game.Objects.TrafficLightState trafficLightState3 = (((trafficLights.m_Flags & TrafficLightFlags.LevelCrossing) != 0) ? (Game.Objects.TrafficLightState.Yellow | Game.Objects.TrafficLightState.Flashing) : Game.Objects.TrafficLightState.Yellow);
        Game.Objects.TrafficLightState trafficLightState4 = (((trafficLights.m_Flags & TrafficLightFlags.LevelCrossing) == 0) ? Game.Objects.TrafficLightState.Red : (Game.Objects.TrafficLightState.Red | Game.Objects.TrafficLightState.Flashing));
        switch (trafficLights.m_State)
        {
            case Game.Net.TrafficLightState.Beginning:
                if ((trafficLight.m_GroupMask0 & num2) != 0)
                {
                    if (trafficLightState != Game.Objects.TrafficLightState.Green)
                    {
                        trafficLightState = trafficLightState4 | trafficLightState3;
                    }
                }
                else
                {
                    trafficLightState = trafficLightState4;
                }

                trafficLightState2 = (((trafficLight.m_GroupMask1 & num2) == 0) ? Game.Objects.TrafficLightState.Red : Game.Objects.TrafficLightState.Green);
                break;
            case Game.Net.TrafficLightState.Ongoing:
                trafficLightState = (((trafficLight.m_GroupMask0 & num) == 0) ? trafficLightState4 : Game.Objects.TrafficLightState.Green);
                trafficLightState2 = (((trafficLight.m_GroupMask1 & num) == 0) ? Game.Objects.TrafficLightState.Red : Game.Objects.TrafficLightState.Green);
                break;
            case Game.Net.TrafficLightState.Extending:
                trafficLightState = (((trafficLight.m_GroupMask0 & num) == 0) ? trafficLightState4 : Game.Objects.TrafficLightState.Green);
                if (trafficLightState2 == Game.Objects.TrafficLightState.Green)
                {
                    if ((trafficLight.m_GroupMask1 & num2) == 0)
                    {
                        trafficLightState2 = Game.Objects.TrafficLightState.Green | Game.Objects.TrafficLightState.Flashing;
                    }
                }
                else
                {
                    trafficLightState2 = Game.Objects.TrafficLightState.Red;
                }

                break;
            case Game.Net.TrafficLightState.Extended:
                trafficLightState = (((trafficLight.m_GroupMask0 & num) == 0) ? trafficLightState4 : Game.Objects.TrafficLightState.Green);
                if (trafficLightState2 != Game.Objects.TrafficLightState.Green || (trafficLight.m_GroupMask1 & num2) == 0)
                {
                    trafficLightState2 = Game.Objects.TrafficLightState.Red;
                }

                break;
            case Game.Net.TrafficLightState.Ending:
                if (trafficLightState == Game.Objects.TrafficLightState.Green)
                {
                    if ((trafficLight.m_GroupMask0 & num2) == 0)
                    {
                        trafficLightState = trafficLightState3;
                    }
                }
                else
                {
                    trafficLightState = trafficLightState4;
                }

                if (trafficLightState2 == Game.Objects.TrafficLightState.Green)
                {
                    if ((trafficLight.m_GroupMask1 & num2) == 0)
                    {
                        trafficLightState2 = Game.Objects.TrafficLightState.Green | Game.Objects.TrafficLightState.Flashing;
                    }
                }
                else
                {
                    trafficLightState2 = Game.Objects.TrafficLightState.Red;
                }

                break;
            case Game.Net.TrafficLightState.Changing:
                if (trafficLightState != Game.Objects.TrafficLightState.Green || (trafficLight.m_GroupMask0 & num2) == 0)
                {
                    trafficLightState = trafficLightState4;
                }

                if (trafficLightState2 != Game.Objects.TrafficLightState.Green || (trafficLight.m_GroupMask1 & num2) == 0)
                {
                    trafficLightState2 = Game.Objects.TrafficLightState.Red;
                }

                break;
            default:
                trafficLightState = Game.Objects.TrafficLightState.None;
                trafficLightState2 = Game.Objects.TrafficLightState.None;
                break;
        }

        trafficLight.m_State = (Game.Objects.TrafficLightState)((uint)trafficLightState | ((uint)trafficLightState2 << 4));
    }

    public static void UpdateMoveableBridge(TrafficLights trafficLights, Transform transform, MoveableBridgeData moveableBridgeData, ref PointOfInterest pointOfInterest)
    {
        int num = -1;
        if (trafficLights.m_State == Game.Net.TrafficLightState.Beginning || trafficLights.m_State == Game.Net.TrafficLightState.Changing)
        {
            if (trafficLights.m_NextSignalGroup > 0)
            {
                num = trafficLights.m_NextSignalGroup - 1;
            }
        }
        else if (trafficLights.m_State != Game.Net.TrafficLightState.Ending && trafficLights.m_CurrentSignalGroup > 0)
        {
            num = trafficLights.m_CurrentSignalGroup - 1;
        }

        pointOfInterest.m_IsValid = false;
        if (num >= 0 && num <= 2)
        {
            pointOfInterest.m_Position = transform.m_Position;
            pointOfInterest.m_Position.y += moveableBridgeData.m_LiftOffsets[num];
            pointOfInterest.m_IsValid = true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void __AssignQueries(ref SystemState state)
    {
        new EntityQueryBuilder(Allocator.Temp).Dispose();
    }

    protected override void OnCreateForCompiler()
    {
        base.OnCreateForCompiler();
        __AssignQueries(ref base.CheckedStateRef);
        __TypeHandle.__AssignHandles(ref base.CheckedStateRef);
        m_ExtraTypeHandle.AssignHandles(ref base.CheckedStateRef);
    }

    [Preserve]
    public PatchedTrafficLightSystem()
    {
    }
}
