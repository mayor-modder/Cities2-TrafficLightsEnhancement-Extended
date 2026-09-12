using C2VM.TrafficLightsEnhancement.Components;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Vehicles;
using TrafficLightsEnhancement.Logic.Tsp;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using NetSubLane = Game.Net.SubLane;

namespace C2VM.TrafficLightsEnhancement.Systems.TrafficLightSystems.Simulation;

public static class TransitSignalPriorityRuntime
{
    private struct FreshRequestDebugInfo
    {
        public TransitSignalPriorityRequestKind RequestKind;
        public TransitSignalPriorityApproachLaneRole ApproachLaneRole;
        public bool HasEarlyCandidate;
        public bool HasPetitionerCandidate;
        public TransitSignalPriorityTrackProbeResult TrackSignaledLaneProbe;
        public TransitSignalPriorityTrackProbeResult TrackApproachLaneProbe;
        public TransitSignalPriorityTrackProbeResult TrackUpstreamLaneProbe;
        public TrackLaneDebugInfo TrackLaneDebugInfo;
    }

    private struct TransitApproachCandidate
    {
        public TspRequest Request;
        public LaneSignal LaneSignal;
        public byte TargetSignalGroup;
        public TransitSignalPriorityApproachLaneRole ApproachLaneRole;
        public TransitSignalPriorityTrackProbeResult TrackSignaledLaneProbe;
        public TransitSignalPriorityTrackProbeResult TrackApproachLaneProbe;
        public TransitSignalPriorityTrackProbeResult TrackUpstreamLaneProbe;
        public TrackLaneDebugInfo TrackLaneDebugInfo;
        public BusApproachSample BusSample;
    }

    private struct ConnectedEdgeFallbackDiagnostics
    {
        public byte ConnectedEdgeCount;
        public byte TramSublaneCount;
        public byte PathNodeMatchCount;
        public byte IndexHitCount;
        public float BestCurvePosition;
    }

    private struct TrackLaneDebugInfo
    {
        public Entity SignaledLaneEntity;
        public Entity ApproachLaneEntity;
        public Entity UpstreamLaneEntity;
        public Entity SignaledLaneOwnerEntity;
        public Entity ApproachLaneOwnerEntity;
        public Entity UpstreamLaneOwnerEntity;
        public byte SignaledSiblingSampleCount;
        public byte ApproachSiblingSampleCount;
        public byte UpstreamSiblingSampleCount;
        public bool SignaledLaneIsMaster;
        public bool ApproachLaneIsMaster;
        public bool UpstreamLaneIsMaster;
        public float TrackSignaledLaneCurvePosition;
        public float TrackApproachLaneCurvePosition;
        public float TrackUpstreamLaneCurvePosition;
        public ConnectedEdgeFallbackDiagnostics FallbackDiagnostics;
    }

    private readonly struct TrackProbeSnapshot
    {
        public TrackProbeSnapshot(bool hasSample, float curvePosition, TransitSignalPriorityTrackProbeResult result)
        {
            HasSample = hasSample;
            CurvePosition = curvePosition;
            Result = result;
        }

        public bool HasSample { get; }

        public float CurvePosition { get; }

        public TransitSignalPriorityTrackProbeResult Result { get; }
    }

    private const float TramApproachLaneCurveThreshold = 0.2f;
    private const float TramUpstreamLaneCurveThreshold = 0.9f;
    private const float TramConnectedEdgeLaneCurveThreshold = 0f;
    private const float BusApproachLaneCurveThreshold = 0.2f;

    public static TransitSignalPriorityBusApproachDebugInfo BuildBusApproachDebugInfo(
        PatchedTrafficLightSystem.UpdateTrafficLightsJob job,
        DynamicBuffer<NetSubLane> subLanes) =>
        BuildBusApproachDebugInfo(
            job,
            subLanes,
            default,
            default);

    public static TransitSignalPriorityBusApproachDebugInfo BuildBusApproachDebugInfo(
        PatchedTrafficLightSystem.UpdateTrafficLightsJob job,
        DynamicBuffer<NetSubLane> subLanes,
        TrafficLights trafficLights,
        global::TrafficLightsEnhancement.Logic.Tsp.TransitSignalPrioritySettings logicSettings)
    {
        TransitSignalPriorityBusApproachDebugInfo debugInfo = CreateBusApproachDebugInfo(job);
        BusApproachSample bestSample = default;
        TransitSignalPriorityBusProbeResult bestProbe = TransitSignalPriorityBusProbeResult.NoBusSamples;
        byte bestTargetSignalGroup = 0;
        bool hasBestSample = false;

        foreach (var subLane in subLanes)
        {
            Entity signaledLaneEntity = subLane.m_SubLane;
            if (!job.m_LaneSignalData.TryGetComponent(signaledLaneEntity, out LaneSignal laneSignal))
            {
                continue;
            }

            debugInfo.m_ScannedSignalLaneCount = IncrementByte(debugInfo.m_ScannedSignalLaneCount);
            Entity approachLaneEntity = ResolveApproachLane(job, signaledLaneEntity);
            if (TryFindBestBusApproachSampleForLane(
                    job,
                    signaledLaneEntity,
                    approachLaneEntity,
                    out BusApproachSample candidate,
                    out TransitSignalPriorityBusProbeResult probe))
            {
                bool shouldReplace = !hasBestSample || candidate.CurvePosition > bestSample.CurvePosition;
                RecordBestBusSample(
                    ref hasBestSample,
                    ref bestSample,
                    ref bestProbe,
                    candidate,
                    probe);

                if (shouldReplace)
                {
                    bestTargetSignalGroup = GetTargetSignalGroup(laneSignal.m_GroupMask, trafficLights.m_CurrentSignalGroup);
                }
            }
        }

        return CompleteBusApproachDebugInfo(
            debugInfo,
            hasBestSample,
            bestSample,
            bestProbe,
            bestTargetSignalGroup,
            logicSettings);
    }

    private static TransitSignalPriorityBusApproachDebugInfo CreateBusApproachDebugInfo(
        PatchedTrafficLightSystem.UpdateTrafficLightsJob job)
    {
        return new TransitSignalPriorityBusApproachDebugInfo
        {
            m_BusApproachIndexLaneCount = job.m_BusApproachIndexLaneCount,
            m_BusProbe = TransitSignalPriorityBusProbeResult.NoBusSamples,
            m_BusDecision = TransitSignalPriorityBusDecision.NoEligibleSample,
        };
    }

    private static TransitSignalPriorityBusApproachDebugInfo CompleteBusApproachDebugInfo(
        TransitSignalPriorityBusApproachDebugInfo debugInfo,
        bool hasBestSample,
        BusApproachSample bestSample,
        TransitSignalPriorityBusProbeResult bestProbe,
        byte bestTargetSignalGroup,
        global::TrafficLightsEnhancement.Logic.Tsp.TransitSignalPrioritySettings logicSettings)
    {
        if (!hasBestSample)
        {
            return debugInfo;
        }

        debugInfo.m_BusProbe = bestProbe;
        debugInfo.m_BusTargetSignalGroup = bestTargetSignalGroup;
        debugInfo.m_BusHitCount = bestSample.HitCount;
        debugInfo.m_BusLaneEntity = bestSample.LaneEntity;
        debugInfo.m_BusVehicleEntity = bestSample.VehicleEntity;
        debugInfo.m_BusCurvePosition = bestSample.CurvePosition;
        debugInfo.m_BusChangeProgress = bestSample.ChangeProgress;
        debugInfo.m_BusSpeed = bestSample.Speed;
        debugInfo.m_BusLaneIsPublicOnly = bestSample.IsBusOnlyLane != 0;
        debugInfo.m_BusIsChangingLane = bestSample.HasChangeLane != 0 || bestSample.IsChangeLaneSample != 0;
        debugInfo.m_BusHasNavigation = bestSample.HasNavigation != 0;
        debugInfo.m_BusNavigationLaneCount = bestSample.NavigationLaneCount;
        debugInfo.m_BusPublicTransportState = bestSample.PublicTransportState;
        debugInfo.m_BusVehicleLaneFlags = bestSample.VehicleLaneFlags;
        ApplyBusDecision(ref debugInfo, bestSample, bestProbe, logicSettings);
        return debugInfo;
    }

    private static void ApplyBusDecision(
        ref TransitSignalPriorityBusApproachDebugInfo debugInfo,
        BusApproachSample sample,
        TransitSignalPriorityBusProbeResult busProbe,
        global::TrafficLightsEnhancement.Logic.Tsp.TransitSignalPrioritySettings logicSettings)
    {
        if (!logicSettings.m_Enabled || !logicSettings.m_AllowPublicCarRequests)
        {
            debugInfo.m_BusDecision = TransitSignalPriorityBusDecision.PriorityDisabled;
            return;
        }

        if (debugInfo.m_BusTargetSignalGroup == 0 || !IsBusPrioritySampleEligible(sample) || !IsBusApproachCurveEligible(sample, busProbe))
        {
            debugInfo.m_BusDecision = TransitSignalPriorityBusDecision.NoEligibleSample;
            return;
        }

        BusPrioritySuppressionDecision stopSuppression =
            BusPrioritySuppressionPolicy.EvaluateStopSuppression(
                GetSuppressionFlags(sample.PublicTransportState),
                BusStopRelation.Unknown,
                sample.IsBusOnlyLane != 0,
                BusPrioritySuppressionPolicy.IsMovingBus(sample.Speed));
        if (stopSuppression.IsSuppressed)
        {
            debugInfo.m_BusDecision = MapBusSuppressionReasonToDecision(stopSuppression.Reason);
            return;
        }

        if (HasAmbiguousBusLaneChange(sample))
        {
            debugInfo.m_BusDecision = TransitSignalPriorityBusDecision.SuppressedAmbiguousLaneChange;
            return;
        }

        debugInfo.m_BusDecision = TransitSignalPriorityBusDecision.RequestEmitted;
    }

    public static TransitSignalPriorityBusDecision MapBusSuppressionReasonToDecision(
        BusPrioritySuppressionReason reason) =>
        reason switch
        {
            BusPrioritySuppressionReason.Boarding => TransitSignalPriorityBusDecision.SuppressedBoarding,
            BusPrioritySuppressionReason.NearSideStop => TransitSignalPriorityBusDecision.SuppressedNearSideStop,
            BusPrioritySuppressionReason.UnknownStopRelation => TransitSignalPriorityBusDecision.SuppressedUnknownStopRelation,
            _ => TransitSignalPriorityBusDecision.None,
        };

    public static bool TryBuildBusApproachRequestFromSample(
        global::TrafficLightsEnhancement.Logic.Tsp.TransitSignalPrioritySettings logicSettings,
        BusApproachSample sample,
        out TspRequest request,
        out TransitSignalPriorityBusDecision decision,
        out BusPrioritySuppressionReason suppressionReason)
    {
        return TryBuildBusApproachRequestFromSample(
            logicSettings,
            sample,
            TransitSignalPriorityBusProbeResult.MatchOnApproachLane,
            out request,
            out decision,
            out suppressionReason);
    }

    public static bool TryBuildBusApproachRequestFromSample(
        global::TrafficLightsEnhancement.Logic.Tsp.TransitSignalPrioritySettings logicSettings,
        BusApproachSample sample,
        TransitSignalPriorityBusProbeResult busProbe,
        out TspRequest request,
        out TransitSignalPriorityBusDecision decision,
        out BusPrioritySuppressionReason suppressionReason)
    {
        request = default;
        decision = TransitSignalPriorityBusDecision.NoEligibleSample;
        suppressionReason = BusPrioritySuppressionReason.None;

        if (!logicSettings.m_Enabled || !logicSettings.m_AllowPublicCarRequests)
        {
            decision = TransitSignalPriorityBusDecision.PriorityDisabled;
            return false;
        }

        if (!IsBusPrioritySampleEligible(sample) || !IsBusApproachCurveEligible(sample, busProbe))
        {
            return false;
        }

        BusPrioritySuppressionDecision stopSuppression =
            BusPrioritySuppressionPolicy.EvaluateStopSuppression(
                GetSuppressionFlags(sample.PublicTransportState),
                BusStopRelation.Unknown,
                sample.IsBusOnlyLane != 0,
                BusPrioritySuppressionPolicy.IsMovingBus(sample.Speed));
        if (stopSuppression.IsSuppressed)
        {
            suppressionReason = stopSuppression.Reason;
            decision = MapBusSuppressionReasonToDecision(stopSuppression.Reason);
            return false;
        }

        if (HasAmbiguousBusLaneChange(sample))
        {
            decision = TransitSignalPriorityBusDecision.SuppressedAmbiguousLaneChange;
            return false;
        }

        if (!global::TrafficLightsEnhancement.Logic.Tsp.TransitSignalPriorityRuntime.TryBuildRequestForLane(
                logicSettings,
                isTrackLane: false,
                isPublicCarLane: true,
                out request))
        {
            return false;
        }

        if (sample.IsBusOnlyLane != 0)
        {
            request = new TspRequest(
                request.Source,
                request.Strength,
                request.ExtensionEligible,
                onDedicatedLane: true);
        }

        decision = TransitSignalPriorityBusDecision.RequestEmitted;
        return request.Source == TspSource.PublicCar;
    }

    private static bool IsBusApproachCurveEligible(
        BusApproachSample sample,
        TransitSignalPriorityBusProbeResult busProbe)
    {
        return busProbe == TransitSignalPriorityBusProbeResult.MatchOnSignaledLane
            || sample.CurvePosition >= BusApproachLaneCurveThreshold;
    }

    public static bool IsBusPrioritySampleEligible(BusApproachSample sample)
    {
        return (sample.PublicTransportState & PublicTransportFlags.DummyTraffic) == 0;
    }

    public static unsafe bool TryResolveActiveLocalRequest(
        PatchedTrafficLightSystem.UpdateTrafficLightsJob job,
        Entity junctionEntity,
        DynamicBuffer<NetSubLane> subLanes,
        TrafficLights trafficLights,
        bool collectReusableBusApproachDebugInfo,
        out TransitSignalPriorityRequest request,
        out Components.TransitSignalPrioritySettings settings,
        out TransitSignalPriorityRuntimeDebugInfo debugInfo,
        out TransitSignalPriorityBusApproachDebugInfo reusableBusApproachDebugInfo,
        out bool hasReusableBusApproachDebugInfo,
        out NativeList<TransitSignalPriorityBusProgress> busProgress)
    {
        request = default;
        settings = default;
        debugInfo = default;
        reusableBusApproachDebugInfo = default;
        hasReusableBusApproachDebugInfo = false;
        busProgress = default;

        if (!job.m_ExtraTypeHandle.m_TransitSignalPrioritySettingsLookup.TryGetComponent(junctionEntity, out settings) || !settings.m_Enabled)
        {
            return false;
        }

        settings.Normalize();
        ushort effectiveRequestHorizonTicks = settings.m_RequestHorizonTicks;

        var availability = TspPolicy.GetAvailability(
            settings.ToLogicSettings(),
            isGroupedIntersection: false);

        if (!availability.IsRuntimeEligible)
        {
            return false;
        }

        if (!IsRuntimeEligibleJunction(job, junctionEntity))
        {
            return false;
        }

        TransitSignalPriorityRequest freshRequest = default;
        FreshRequestDebugInfo freshDebugInfo = default;
        if (settings.m_AllowPublicCarRequests)
        {
            busProgress = new NativeList<TransitSignalPriorityBusProgress>(Allocator.Temp);
        }
        bool hasFreshRequest = TryBuildFreshRequest(
            job,
            junctionEntity,
            subLanes,
            trafficLights,
            settings,
            effectiveRequestHorizonTicks,
            collectReusableBusApproachDebugInfo,
            busProgress,
            out freshRequest,
            out freshDebugInfo,
            out reusableBusApproachDebugInfo,
            out hasReusableBusApproachDebugInfo);

        TransitSignalPriorityRequest priorRequest = default;
        bool hasExistingRequest = job.m_ExtraTypeHandle.m_TransitSignalPriorityRequest.TryGetComponent(junctionEntity, out priorRequest)
            && priorRequest.m_TargetSignalGroup > 0
            && priorRequest.m_Strength > 0f;

        // Use the same identity-aware bridge covered by native-free batch tests. Dropping
        // only a fresh request would let its previous latch continue extending green.
        if (!BusProgressRuntime.TryRefreshOrLatchRequest(
                hasFreshRequest ? freshRequest : (TransitSignalPriorityRequest?)null,
                hasExistingRequest ? priorRequest : (TransitSignalPriorityRequest?)null,
                busProgress.IsCreated ? busProgress.GetUnsafePtr() : null,
                busProgress.IsCreated ? busProgress.Length : 0,
                effectiveRequestHorizonTicks,
                trafficLights.m_CurrentSignalGroup,
                out request,
                out hasExistingRequest))
        {
            return false;
        }
        debugInfo = new TransitSignalPriorityRuntimeDebugInfo
        {
            m_RequestKind = hasFreshRequest ? freshDebugInfo.RequestKind : TransitSignalPriorityRequestKind.LatchedExisting,
            m_ApproachLaneRole = hasFreshRequest ? freshDebugInfo.ApproachLaneRole : TransitSignalPriorityApproachLaneRole.None,
            m_SourceType = request.m_SourceType,
            m_TargetSignalGroup = request.m_TargetSignalGroup,
            m_Strength = request.m_Strength,
            m_ExpiryTimer = request.m_ExpiryTimer,
            m_ExtendCurrentPhase = request.m_ExtendCurrentPhase,
            m_HasEarlyCandidate = freshDebugInfo.HasEarlyCandidate,
            m_HasPetitionerCandidate = freshDebugInfo.HasPetitionerCandidate,
            m_HadExistingRequest = hasExistingRequest,
            m_TrackSignaledLaneProbe = freshDebugInfo.TrackSignaledLaneProbe,
            m_TrackApproachLaneProbe = freshDebugInfo.TrackApproachLaneProbe,
            m_TrackUpstreamLaneProbe = freshDebugInfo.TrackUpstreamLaneProbe,
            m_TrackSignaledLaneCurvePosition = freshDebugInfo.TrackLaneDebugInfo.TrackSignaledLaneCurvePosition,
            m_TrackApproachLaneCurvePosition = freshDebugInfo.TrackLaneDebugInfo.TrackApproachLaneCurvePosition,
            m_TrackUpstreamLaneCurvePosition = freshDebugInfo.TrackLaneDebugInfo.TrackUpstreamLaneCurvePosition,
            m_TramApproachIndexLaneCount = job.m_TramApproachIndexLaneCount,
            m_TrackSignaledLaneEntity = freshDebugInfo.TrackLaneDebugInfo.SignaledLaneEntity,
            m_TrackApproachLaneEntity = freshDebugInfo.TrackLaneDebugInfo.ApproachLaneEntity,
            m_TrackUpstreamLaneEntity = freshDebugInfo.TrackLaneDebugInfo.UpstreamLaneEntity,
            m_TrackSignaledLaneOwnerEntity = freshDebugInfo.TrackLaneDebugInfo.SignaledLaneOwnerEntity,
            m_TrackApproachLaneOwnerEntity = freshDebugInfo.TrackLaneDebugInfo.ApproachLaneOwnerEntity,
            m_TrackUpstreamLaneOwnerEntity = freshDebugInfo.TrackLaneDebugInfo.UpstreamLaneOwnerEntity,
            m_TrackSignaledSiblingSampleCount = freshDebugInfo.TrackLaneDebugInfo.SignaledSiblingSampleCount,
            m_TrackApproachSiblingSampleCount = freshDebugInfo.TrackLaneDebugInfo.ApproachSiblingSampleCount,
            m_TrackUpstreamSiblingSampleCount = freshDebugInfo.TrackLaneDebugInfo.UpstreamSiblingSampleCount,
            m_TrackSignaledLaneIsMaster = freshDebugInfo.TrackLaneDebugInfo.SignaledLaneIsMaster,
            m_TrackApproachLaneIsMaster = freshDebugInfo.TrackLaneDebugInfo.ApproachLaneIsMaster,
            m_TrackUpstreamLaneIsMaster = freshDebugInfo.TrackLaneDebugInfo.UpstreamLaneIsMaster,
            m_FallbackConnectedEdgeCount = freshDebugInfo.TrackLaneDebugInfo.FallbackDiagnostics.ConnectedEdgeCount,
            m_FallbackTramSublaneCount = freshDebugInfo.TrackLaneDebugInfo.FallbackDiagnostics.TramSublaneCount,
            m_FallbackPathNodeMatchCount = freshDebugInfo.TrackLaneDebugInfo.FallbackDiagnostics.PathNodeMatchCount,
            m_FallbackIndexHitCount = freshDebugInfo.TrackLaneDebugInfo.FallbackDiagnostics.IndexHitCount,
            m_FallbackBestCurvePosition = freshDebugInfo.TrackLaneDebugInfo.FallbackDiagnostics.BestCurvePosition,
        };
        return true;
    }

    private static bool IsRuntimeEligibleJunction(
        PatchedTrafficLightSystem.UpdateTrafficLightsJob job,
        Entity junctionEntity)
    {
        return !job.m_ExtraTypeHandle.m_TrafficGroupMember.HasComponent(junctionEntity);
    }

    public static bool ShouldHoldCurrentGroup(
        TrafficLights trafficLights,
        TransitSignalPriorityRequest request,
        ushort maxGreenExtensionTicks)
    {
        return ShouldHoldCurrentGroup(
            trafficLights,
            request,
            trafficLights.m_Timer,
            maxGreenExtensionTicks);
    }

    public static bool ShouldHoldCurrentGroup(
        TrafficLights trafficLights,
        TransitSignalPriorityRequest request,
        uint elapsedTicks,
        ushort maxGreenExtensionTicks)
    {
        return TspPreemptionPolicy.ShouldHoldCurrentGroup(
            trafficLights.m_CurrentSignalGroup,
            ToSignalRequest(request),
            elapsedTicks,
            maxGreenExtensionTicks);
    }

    public static int GetMinimumGreenDurationTicks(
        int defaultMinimumGreenTicks,
        TrafficLights trafficLights,
        TransitSignalPriorityRequest request,
        bool protectActivePedestrianPhase = false)
    {
        return TspPreemptionPolicy.GetMinimumGreenDurationTicks(
            defaultMinimumGreenTicks,
            trafficLights.m_CurrentSignalGroup,
            ToSignalRequest(request),
            protectActivePedestrianPhase);
    }

    public static bool ShouldAggressivelyPreemptToTargetGroup(
        TrafficLights trafficLights,
        TransitSignalPriorityRequest request,
        bool protectActivePedestrianPhase = false)
    {
        return TspPreemptionPolicy.ShouldAggressivelyPreemptToConflictingGroup(
            trafficLights.m_CurrentSignalGroup,
            ToSignalRequest(request),
            protectActivePedestrianPhase);
    }

    public static bool ShouldApplyTargetGroupSelection(
        TransitSignalPriorityRequest request,
        bool protectActivePedestrianPhase = false)
    {
        return TspPreemptionPolicy.ShouldApplyTargetGroupSelection(
            ToSignalRequest(request),
            protectActivePedestrianPhase);
    }

    private static bool TryBuildFreshRequest(
        PatchedTrafficLightSystem.UpdateTrafficLightsJob job,
        Entity junctionEntity,
        DynamicBuffer<NetSubLane> subLanes,
        TrafficLights trafficLights,
        Components.TransitSignalPrioritySettings settings,
        ushort effectiveRequestHorizonTicks,
        bool collectReusableBusApproachDebugInfo,
        NativeList<TransitSignalPriorityBusProgress> busProgress,
        out TransitSignalPriorityRequest request,
        out FreshRequestDebugInfo debugInfo,
        out TransitSignalPriorityBusApproachDebugInfo reusableBusApproachDebugInfo,
        out bool hasReusableBusApproachDebugInfo)
    {
        request = default;
        debugInfo = default;
        reusableBusApproachDebugInfo = default;
        hasReusableBusApproachDebugInfo = false;

        var logicSettings = settings.ToLogicSettings();

        TransitApproachScanState scanState = default;
        TransitApproachCandidate? earlyCandidate = null;
        TransitApproachCandidate? petitionerCandidate = null;
        bool shouldCollectReusableBusDebugInfo = collectReusableBusApproachDebugInfo
            && logicSettings.m_AllowPublicCarRequests;
        TransitSignalPriorityBusApproachDebugInfo busDebugInfo = CreateBusApproachDebugInfo(job);
        BusApproachSample bestBusSample = default;
        TransitSignalPriorityBusProbeResult bestBusProbe = TransitSignalPriorityBusProbeResult.NoBusSamples;
        byte bestBusTargetSignalGroup = 0;
        bool hasBestBusSample = false;
        var busCandidates = logicSettings.m_AllowPublicCarRequests
            ? new NativeList<BusRequestCandidate>(Allocator.Temp)
            : default;

        foreach (var subLane in subLanes)
        {
            Entity subLaneEntity = subLane.m_SubLane;
            if (!job.m_LaneSignalData.TryGetComponent(subLaneEntity, out var laneSignal))
            {
                continue;
            }

            Entity approachLaneEntity = ResolveApproachLane(job, subLaneEntity);
            bool isTramTrackLane = IsTramTrackLane(job, approachLaneEntity);
            if (shouldCollectReusableBusDebugInfo)
            {
                busDebugInfo.m_ScannedSignalLaneCount = IncrementByte(busDebugInfo.m_ScannedSignalLaneCount);
            }

            TspRequest? earlyRequest = null;
            TransitSignalPriorityApproachLaneRole detectedLaneRole = TransitSignalPriorityApproachLaneRole.None;
            TransitSignalPriorityTrackProbeResult trackSignaledLaneProbe = TransitSignalPriorityTrackProbeResult.None;
            TransitSignalPriorityTrackProbeResult trackApproachLaneProbe = TransitSignalPriorityTrackProbeResult.None;
            TransitSignalPriorityTrackProbeResult trackUpstreamLaneProbe = TransitSignalPriorityTrackProbeResult.None;
            TrackLaneDebugInfo trackDebugInfo = default;
            if (isTramTrackLane
                && global::TrafficLightsEnhancement.Logic.Tsp.TransitSignalPriorityRuntime.TryBuildRequestForLane(
                    logicSettings,
                    isTrackLane: true,
                    out var trackLaneRequest)
                && TryBuildEarlyApproachRequestForLane(
                    job,
                    subLaneEntity,
                    approachLaneEntity,
                    isTramTrackLane,
                    trackLaneRequest,
                    out var detectedEarlyRequest,
                    out detectedLaneRole,
                    out trackSignaledLaneProbe,
                    out trackApproachLaneProbe,
                    out trackUpstreamLaneProbe,
                    out trackDebugInfo))
            {
                earlyRequest = detectedEarlyRequest;
            }

            bool hasBusRequest = TryBuildBusApproachRequestForLane(
                    job,
                    subLaneEntity,
                    approachLaneEntity,
                    logicSettings,
                    out var detectedBusRequest,
                    out var detectedBusSample,
                    out var detectedBusProbe,
                    out _);
            if (detectedBusProbe != TransitSignalPriorityBusProbeResult.None
                && detectedBusProbe != TransitSignalPriorityBusProbeResult.NoBusSamples)
            {
                byte busTarget = GetTargetSignalGroup(laneSignal.m_GroupMask, trafficLights.m_CurrentSignalGroup);
                if (busTarget != 0)
                {
                    busCandidates.Add(new BusRequestCandidate
                    {
                        IsEligibleRequest = hasBusRequest,
                        Request = detectedBusRequest,
                        Sample = detectedBusSample,
                        LaneSignal = laneSignal,
                        TargetSignalGroup = busTarget,
                    });
                }
            }

            if (shouldCollectReusableBusDebugInfo
                && detectedBusProbe != TransitSignalPriorityBusProbeResult.None
                && detectedBusProbe != TransitSignalPriorityBusProbeResult.NoBusSamples)
            {
                bool shouldReplaceBusDebugSample = !hasBestBusSample
                    || detectedBusSample.CurvePosition > bestBusSample.CurvePosition;
                RecordBestBusSample(
                    ref hasBestBusSample,
                    ref bestBusSample,
                    ref bestBusProbe,
                    detectedBusSample,
                    detectedBusProbe);

                if (shouldReplaceBusDebugSample)
                {
                    bestBusTargetSignalGroup = GetTargetSignalGroup(laneSignal.m_GroupMask, trafficLights.m_CurrentSignalGroup);
                }
            }

            TspRequest? petitionerRequest = null;
            if (TryBuildPetitionerRequestForLane(
                    job,
                    laneSignal,
                    isTramTrackLane,
                    logicSettings,
                    out var detectedPetitionerRequest))
            {
                petitionerRequest = detectedPetitionerRequest;
            }

            if (!earlyRequest.HasValue && !petitionerRequest.HasValue)
            {
                continue;
            }

            byte currentSignalGroup = trafficLights.m_CurrentSignalGroup;
            byte targetSignalGroup = GetTargetSignalGroup(laneSignal.m_GroupMask, currentSignalGroup);
            if (targetSignalGroup == 0)
            {
                continue;
            }

            scanState = new TransitApproachScanState(
                SelectPreferredRequest(scanState.EarlyRequest, earlyRequest),
                SelectPreferredRequest(scanState.PetitionerRequest, petitionerRequest));

            if (earlyRequest.HasValue)
            {
                TransitApproachCandidate candidate = new()
                {
                    Request = earlyRequest.Value,
                    LaneSignal = laneSignal,
                    TargetSignalGroup = targetSignalGroup,
                    ApproachLaneRole = detectedLaneRole,
                    TrackSignaledLaneProbe = trackSignaledLaneProbe,
                    TrackApproachLaneProbe = trackApproachLaneProbe,
                    TrackUpstreamLaneProbe = trackUpstreamLaneProbe,
                    TrackLaneDebugInfo = trackDebugInfo,
                };

                if (ShouldReplaceCandidate(candidate.Request, earlyCandidate))
                {
                    earlyCandidate = candidate;
                }
            }

            if (petitionerRequest.HasValue)
            {
                TransitApproachCandidate candidate = new()
                {
                    Request = petitionerRequest.Value,
                    LaneSignal = laneSignal,
                    TargetSignalGroup = targetSignalGroup,
                    TrackSignaledLaneProbe = trackSignaledLaneProbe,
                    TrackApproachLaneProbe = trackApproachLaneProbe,
                    TrackUpstreamLaneProbe = trackUpstreamLaneProbe,
                    TrackLaneDebugInfo = trackDebugInfo,
                };

                if (ShouldReplaceCandidate(candidate.Request, petitionerCandidate))
                {
                    petitionerCandidate = candidate;
                }
            }
        }

        if (busCandidates.IsCreated)
        {
            ResolveBusProgressAndCandidates(job, junctionEntity, trafficLights, busCandidates,
                busProgress, ref scanState, ref earlyCandidate);
            busCandidates.Dispose();
        }

        if (shouldCollectReusableBusDebugInfo)
        {
            reusableBusApproachDebugInfo = CompleteBusApproachDebugInfo(
                busDebugInfo,
                hasBestBusSample,
                bestBusSample,
                bestBusProbe,
                bestBusTargetSignalGroup,
                logicSettings);
            hasReusableBusApproachDebugInfo = true;
            for (int i = 0; i < busProgress.Length; i++)
            {
                var progress = busProgress[i];
                if (progress.m_VehicleEntity == bestBusSample.VehicleEntity
                    && progress.m_LaneEntity == bestBusSample.LaneEntity)
                {
                    reusableBusApproachDebugInfo.m_BusNoProgressTicks = progress.m_State.GreenTicksWithoutProgress;
                    reusableBusApproachDebugInfo.m_BusNoProgressSuppressed = progress.m_State.IsSuppressed;
                    if (progress.m_State.IsSuppressed)
                        reusableBusApproachDebugInfo.m_BusDecision = TransitSignalPriorityBusDecision.SuppressedNoProgress;
                    break;
                }
            }
        }

        TspRequest? selectedRequest = EarlyApproachDetection.PreferEarlyRequest(
            scanState.EarlyRequest,
            scanState.PetitionerRequest);

        if (!selectedRequest.HasValue)
        {
            return false;
        }

        TransitApproachCandidate? selectedCandidate = scanState.EarlyRequest.HasValue
            ? earlyCandidate
            : petitionerCandidate;

        if (!selectedCandidate.HasValue)
        {
            return false;
        }

        IndexedTrackProbeDiagnostics reportedTrackProbeDiagnostics = EarlyApproachDetection.SelectReportedTrackProbeDiagnostics(
            selectedEarlyRequest: scanState.EarlyRequest.HasValue,
            earlyDiagnostics: ToIndexedTrackProbeDiagnostics(earlyCandidate),
            selectedPetitionerRequest: !scanState.EarlyRequest.HasValue && scanState.PetitionerRequest.HasValue,
            petitionerDiagnostics: ToIndexedTrackProbeDiagnostics(petitionerCandidate));

        request = CreateRequest(
            selectedCandidate.Value.Request,
            effectiveRequestHorizonTicks,
            trafficLights.m_CurrentSignalGroup,
            selectedCandidate.Value.TargetSignalGroup,
            selectedCandidate.Value.LaneSignal);
        request.m_BusVehicleEntity = selectedCandidate.Value.BusSample.VehicleEntity;
        request.m_BusLaneEntity = selectedCandidate.Value.BusSample.LaneEntity;

        debugInfo = new FreshRequestDebugInfo
        {
            RequestKind = scanState.EarlyRequest.HasValue
                ? TransitSignalPriorityRequestKind.FreshEarly
                : TransitSignalPriorityRequestKind.FreshPetitioner,
            ApproachLaneRole = scanState.EarlyRequest.HasValue
                ? selectedCandidate.Value.ApproachLaneRole
                : TransitSignalPriorityApproachLaneRole.None,
            HasEarlyCandidate = scanState.EarlyRequest.HasValue,
            HasPetitionerCandidate = scanState.PetitionerRequest.HasValue,
            TrackSignaledLaneProbe = ToTrackProbeResult(reportedTrackProbeDiagnostics.SignaledLane),
            TrackApproachLaneProbe = ToTrackProbeResult(reportedTrackProbeDiagnostics.ApproachLane),
            TrackUpstreamLaneProbe = ToTrackProbeResult(reportedTrackProbeDiagnostics.UpstreamLane),
            TrackLaneDebugInfo = selectedCandidate.Value.TrackLaneDebugInfo,
        };
        return true;
    }

    private static unsafe void ResolveBusProgressAndCandidates(
        PatchedTrafficLightSystem.UpdateTrafficLightsJob job,
        Entity junctionEntity,
        TrafficLights trafficLights,
        NativeList<BusRequestCandidate> candidates,
        NativeList<TransitSignalPriorityBusProgress> progressStates,
        ref TransitApproachScanState scanState,
        ref TransitApproachCandidate? earlyCandidate)
    {
        bool hasPrevious = job.m_ExtraTypeHandle.m_TransitSignalPriorityBusProgress
            .TryGetBuffer(junctionEntity, out var previousStates);

        progressStates.ResizeUninitialized(candidates.Length);
        var previousPointer = hasPrevious
            ? (TransitSignalPriorityBusProgress*)previousStates.AsNativeArray().GetUnsafeReadOnlyPtr()
            : null;
        int stateCount = BusProgressRuntime.ObserveAndFilterCandidates(
            candidates.GetUnsafePtr(), candidates.Length,
            previousPointer, hasPrevious ? previousStates.Length : 0,
            progressStates.GetUnsafePtr(), trafficLights);
        progressStates.ResizeUninitialized(stateCount);

        // Filter before ranking so a stalled bus cannot hide another eligible bus or tram.
        for (int i = 0; i < candidates.Length; i++)
        {
            var bus = candidates[i];
            if (!bus.IsEligibleRequest)
                continue;

            scanState = new TransitApproachScanState(
                SelectPreferredRequest(scanState.EarlyRequest, bus.Request), scanState.PetitionerRequest);
            if (ShouldReplaceCandidate(bus.Request, earlyCandidate))
            {
                earlyCandidate = new TransitApproachCandidate
                {
                    Request = bus.Request,
                    LaneSignal = bus.LaneSignal,
                    TargetSignalGroup = bus.TargetSignalGroup,
                    ApproachLaneRole = TransitSignalPriorityApproachLaneRole.ApproachLane,
                    BusSample = bus.Sample,
                };
            }
        }
    }

    private static Entity ResolveApproachLane(
        PatchedTrafficLightSystem.UpdateTrafficLightsJob job,
        Entity subLaneEntity)
    {
        Entity sourceSubLane = Entity.Null;
        if (job.m_ExtraTypeHandle.m_ExtraLaneSignal.TryGetComponent(subLaneEntity, out var extraLaneSignal))
        {
            sourceSubLane = extraLaneSignal.m_SourceSubLane;
        }

        return EarlyApproachDetection.ResolveApproachLane(subLaneEntity, sourceSubLane, Entity.Null);
    }

    private static bool TryProbeBusLane(
        PatchedTrafficLightSystem.UpdateTrafficLightsJob job,
        Entity laneEntity,
        out BusApproachSample sample)
    {
        sample = default;
        return laneEntity != Entity.Null && job.m_BusApproachIndex.TryGetValue(laneEntity, out sample);
    }

    private static bool TryProbeConnectedEdgeBusLane(
        PatchedTrafficLightSystem.UpdateTrafficLightsJob job,
        Entity approachLaneEntity,
        out BusApproachSample sample)
    {
        sample = default;
        if (!job.m_LaneData.TryGetComponent(approachLaneEntity, out var approachLane)
            || !job.m_OwnerData.TryGetComponent(approachLaneEntity, out var approachOwner)
            || !job.m_ConnectedEdges.TryGetBuffer(approachOwner.m_Owner, out var connectedEdges))
        {
            return false;
        }

        bool hasBestSample = false;
        for (int i = 0; i < connectedEdges.Length; i++)
        {
            Entity edgeEntity = connectedEdges[i].m_Edge;
            if (!job.m_SubLanes.TryGetBuffer(edgeEntity, out var edgeSubLanes))
            {
                continue;
            }

            for (int j = 0; j < edgeSubLanes.Length; j++)
            {
                Entity edgeLaneEntity = edgeSubLanes[j].m_SubLane;
                if (!job.m_ExtraTypeHandle.m_CarLane.HasComponent(edgeLaneEntity)
                    || !job.m_LaneData.TryGetComponent(edgeLaneEntity, out var edgeLane)
                    || !edgeLane.m_EndNode.Equals(approachLane.m_StartNode)
                    || !TryProbeBusLane(job, edgeLaneEntity, out BusApproachSample candidate))
                {
                    continue;
                }

                if (!hasBestSample || candidate.CurvePosition > sample.CurvePosition)
                {
                    hasBestSample = true;
                    sample = candidate;
                }
            }
        }

        return hasBestSample;
    }

    private static bool TryFindBestBusApproachSampleForLane(
        PatchedTrafficLightSystem.UpdateTrafficLightsJob job,
        Entity signaledLaneEntity,
        Entity approachLaneEntity,
        out BusApproachSample sample,
        out TransitSignalPriorityBusProbeResult probe)
    {
        sample = default;
        probe = TransitSignalPriorityBusProbeResult.NoBusSamples;
        bool hasSample = false;

        if (TryProbeBusLane(job, signaledLaneEntity, out BusApproachSample signaledSample))
        {
            RecordBestBusSample(
                ref hasSample,
                ref sample,
                ref probe,
                signaledSample,
                TransitSignalPriorityBusProbeResult.MatchOnSignaledLane);
        }

        if (approachLaneEntity != signaledLaneEntity
            && TryProbeBusLane(job, approachLaneEntity, out BusApproachSample approachSample))
        {
            RecordBestBusSample(
                ref hasSample,
                ref sample,
                ref probe,
                approachSample,
                TransitSignalPriorityBusProbeResult.MatchOnApproachLane);
        }
        else if (TryProbeConnectedEdgeBusLane(job, approachLaneEntity, out BusApproachSample connectedSample))
        {
            RecordBestBusSample(
                ref hasSample,
                ref sample,
                ref probe,
                connectedSample,
                TransitSignalPriorityBusProbeResult.MatchOnConnectedApproachLane);
        }

        return hasSample;
    }

    private static void RecordBestBusSample(
        ref bool hasBestSample,
        ref BusApproachSample bestSample,
        ref TransitSignalPriorityBusProbeResult bestProbe,
        BusApproachSample candidate,
        TransitSignalPriorityBusProbeResult probe)
    {
        if (hasBestSample && bestSample.CurvePosition >= candidate.CurvePosition)
        {
            return;
        }

        hasBestSample = true;
        bestSample = candidate;
        bestProbe = probe;
    }

    private static byte IncrementByte(byte value)
    {
        return value == byte.MaxValue ? byte.MaxValue : (byte)(value + 1);
    }

    private static bool TryBuildEarlyApproachRequestForLane(
        PatchedTrafficLightSystem.UpdateTrafficLightsJob job,
        Entity signaledLaneEntity,
        Entity approachLaneEntity,
        bool isTramTrackLane,
        TspRequest laneRequest,
        out TspRequest request,
        out TransitSignalPriorityApproachLaneRole laneRole,
        out TransitSignalPriorityTrackProbeResult trackSignaledLaneProbe,
        out TransitSignalPriorityTrackProbeResult trackApproachLaneProbe,
        out TransitSignalPriorityTrackProbeResult trackUpstreamLaneProbe,
        out TrackLaneDebugInfo trackDebugInfo)
    {
        request = default;
        laneRole = TransitSignalPriorityApproachLaneRole.None;
        trackSignaledLaneProbe = TransitSignalPriorityTrackProbeResult.None;
        trackApproachLaneProbe = TransitSignalPriorityTrackProbeResult.None;
        trackUpstreamLaneProbe = TransitSignalPriorityTrackProbeResult.None;
        trackDebugInfo = default;

        if (isTramTrackLane)
        {
            return TryBuildEarlyApproachRequestForTrackLane(
                job,
                signaledLaneEntity,
                approachLaneEntity,
                laneRequest,
                out request,
                out laneRole,
                out trackSignaledLaneProbe,
                out trackApproachLaneProbe,
                out trackUpstreamLaneProbe,
                out trackDebugInfo);
        }

        return false;
    }

    private static bool TryBuildBusApproachRequestForLane(
        PatchedTrafficLightSystem.UpdateTrafficLightsJob job,
        Entity signaledLaneEntity,
        Entity approachLaneEntity,
        global::TrafficLightsEnhancement.Logic.Tsp.TransitSignalPrioritySettings logicSettings,
        out TspRequest request,
        out BusApproachSample sample,
        out TransitSignalPriorityBusProbeResult busProbe,
        out BusPrioritySuppressionReason suppressionReason)
    {
        request = default;
        sample = default;
        busProbe = TransitSignalPriorityBusProbeResult.NoBusSamples;
        suppressionReason = BusPrioritySuppressionReason.None;

        if (!logicSettings.m_AllowPublicCarRequests)
        {
            return false;
        }

        if (!TryFindBestBusApproachSampleForLane(
                job,
                signaledLaneEntity,
                approachLaneEntity,
                out sample,
                out busProbe))
        {
            return false;
        }

        if (!TryBuildBusApproachRequestFromSample(
                logicSettings,
                sample,
                busProbe,
                out request,
                out _,
                out suppressionReason))
        {
            return false;
        }

        return true;
    }

    private static bool HasAmbiguousBusLaneChange(BusApproachSample sample)
    {
        return sample.HasChangeLane != 0 || sample.IsChangeLaneSample != 0;
    }

    private static bool TryBuildPetitionerRequestForLane(
        PatchedTrafficLightSystem.UpdateTrafficLightsJob job,
        LaneSignal laneSignal,
        bool isTrackLane,
        global::TrafficLightsEnhancement.Logic.Tsp.TransitSignalPrioritySettings logicSettings,
        out TspRequest request)
    {
        request = default;

        if (!isTrackLane || laneSignal.m_Petitioner == Entity.Null)
        {
            return false;
        }

        if (!global::TrafficLightsEnhancement.Logic.Tsp.TransitSignalPriorityRuntime.TryBuildRequestForLane(
                logicSettings,
                isTrackLane,
                out request))
        {
            return false;
        }

        return true;
    }

    private static TspRequest? SelectPreferredRequest(TspRequest? existingRequest, TspRequest candidateRequest)
    {
        if (!existingRequest.HasValue || IsPreferredRequest(candidateRequest, existingRequest.Value))
        {
            return candidateRequest;
        }

        return existingRequest;
    }

    public static TspRequest? SelectPreferredRequestAndRole(
        TspRequest? existingRequest,
        TransitSignalPriorityApproachLaneRole existingRole,
        TspRequest candidateRequest,
        TransitSignalPriorityApproachLaneRole candidateRole,
        out TransitSignalPriorityApproachLaneRole selectedRole)
    {
        if (!existingRequest.HasValue || IsPreferredRequest(candidateRequest, existingRequest.Value))
        {
            selectedRole = candidateRole;
            return candidateRequest;
        }

        selectedRole = existingRole;
        return existingRequest;
    }

    private static TspRequest? SelectPreferredRequest(TspRequest? existingRequest, TspRequest? candidateRequest)
    {
        return candidateRequest.HasValue
            ? SelectPreferredRequest(existingRequest, candidateRequest.Value)
            : existingRequest;
    }

    private static bool ShouldReplaceCandidate(TspRequest request, TransitApproachCandidate? existingCandidate)
    {
        return !existingCandidate.HasValue || IsPreferredRequest(request, existingCandidate.Value.Request);
    }

    private static bool IsPreferredRequest(TspRequest candidateRequest, TspRequest existingRequest)
    {
        return TspSourcePriority.IsPreferredRequest(candidateRequest, existingRequest);
    }

    private static bool IsTramTrackLane(PatchedTrafficLightSystem.UpdateTrafficLightsJob job, Entity approachLaneEntity)
    {
        if (!job.m_ExtraTypeHandle.m_TrackLane.HasComponent(approachLaneEntity))
        {
            return false;
        }

        if (!job.m_ExtraTypeHandle.m_PrefabRef.TryGetComponent(approachLaneEntity, out var prefabRef))
        {
            return false;
        }

        if (!job.m_ExtraTypeHandle.m_TrackLaneData.TryGetComponent(prefabRef.m_Prefab, out var trackLaneData))
        {
            return false;
        }

        return (trackLaneData.m_TrackTypes & TrackTypes.Tram) != 0;
    }

    private static TransitApproachSuppressionFlags GetSuppressionFlags(PublicTransportFlags state)
    {
        TransitApproachSuppressionFlags flags = TransitApproachSuppressionFlags.None;

        if ((state & PublicTransportFlags.Boarding) != 0) flags |= TransitApproachSuppressionFlags.Boarding;
        if ((state & PublicTransportFlags.Arriving) != 0) flags |= TransitApproachSuppressionFlags.Arriving;
        if ((state & PublicTransportFlags.RequireStop) != 0) flags |= TransitApproachSuppressionFlags.RequireStop;

        return flags;
    }

    private static bool TryBuildEarlyApproachRequestForTrackLane(
        PatchedTrafficLightSystem.UpdateTrafficLightsJob job,
        Entity signaledLaneEntity,
        Entity approachLaneEntity,
        TspRequest laneRequest,
        out TspRequest request,
        out TransitSignalPriorityApproachLaneRole laneRole,
        out TransitSignalPriorityTrackProbeResult signaledLaneProbe,
        out TransitSignalPriorityTrackProbeResult approachLaneProbe,
        out TransitSignalPriorityTrackProbeResult upstreamLaneProbe,
        out TrackLaneDebugInfo trackDebugInfo)
    {
        request = default;
        laneRole = TransitSignalPriorityApproachLaneRole.None;
        signaledLaneProbe = TransitSignalPriorityTrackProbeResult.None;
        approachLaneProbe = TransitSignalPriorityTrackProbeResult.None;
        upstreamLaneProbe = TransitSignalPriorityTrackProbeResult.None;
        trackDebugInfo = default;
        TrackProbeSnapshot signaledProbe = ProbeIndexedTrackLane(
            job.m_TramApproachIndex,
            signaledLaneEntity,
            TramApproachLaneCurveThreshold,
            isUpstreamLane: false);
        signaledLaneProbe = signaledProbe.Result;

        TrackProbeSnapshot approachProbe = signaledLaneEntity == approachLaneEntity
            ? signaledProbe
            : ProbeIndexedTrackLane(
                job.m_TramApproachIndex,
                approachLaneEntity,
                TramApproachLaneCurveThreshold,
                isUpstreamLane: false);

        ConnectedEdgeFallbackDiagnostics fallbackDiagnostics = default;
        Entity connectedApproachLaneEntity = Entity.Null;
        if (approachProbe.Result == TransitSignalPriorityTrackProbeResult.NoTramSamples)
        {
            TrackProbeSnapshot connectedProbe = TryProbeConnectedEdgeTramLane(
                job,
                approachLaneEntity,
                TramConnectedEdgeLaneCurveThreshold,
                out fallbackDiagnostics,
                out connectedApproachLaneEntity);
            if (connectedProbe.HasSample)
            {
                approachProbe = connectedProbe;
            }
        }

        approachLaneProbe = approachProbe.Result;

        Entity upstreamLaneEntity = TryResolveImmediateUpstreamTramLane(job, approachLaneEntity);
        if (upstreamLaneEntity == Entity.Null && connectedApproachLaneEntity != Entity.Null)
        {
            upstreamLaneEntity = TryResolveConnectedUpstreamTramLane(job, connectedApproachLaneEntity);
        }

        TrackProbeSnapshot upstreamProbe = upstreamLaneEntity == Entity.Null
            ? default
            : ProbeIndexedTrackLane(
                job.m_TramApproachIndex,
                upstreamLaneEntity,
                TramUpstreamLaneCurveThreshold,
                isUpstreamLane: true);
        upstreamLaneProbe = upstreamProbe.Result;
        trackDebugInfo = BuildTrackLaneDebugInfo(
            job,
            signaledLaneEntity,
            approachLaneEntity,
            upstreamLaneEntity);
        trackDebugInfo.FallbackDiagnostics = fallbackDiagnostics;
        trackDebugInfo.TrackSignaledLaneCurvePosition = signaledProbe.CurvePosition;
        trackDebugInfo.TrackApproachLaneCurvePosition = approachProbe.CurvePosition;
        trackDebugInfo.TrackUpstreamLaneCurvePosition = upstreamProbe.CurvePosition;

        if (approachProbe.Result == TransitSignalPriorityTrackProbeResult.MatchOnConnectedApproachLane)
        {
            request = laneRequest;
            laneRole = TransitSignalPriorityApproachLaneRole.ApproachLane;
            return true;
        }

        IndexedTrackProbeMatch indexedMatch = EarlyApproachDetection.EvaluateIndexedTrackTramSamples(
            approachProbe.HasSample,
            approachProbe.CurvePosition,
            upstreamProbe.HasSample,
            upstreamProbe.CurvePosition,
            TramApproachLaneCurveThreshold,
            TramUpstreamLaneCurveThreshold);

        switch (indexedMatch)
        {
            case IndexedTrackProbeMatch.MatchOnApproachLane:
                request = laneRequest;
                laneRole = TransitSignalPriorityApproachLaneRole.ApproachLane;
                return true;

            case IndexedTrackProbeMatch.MatchOnUpstreamLane:
                request = laneRequest;
                laneRole = TransitSignalPriorityApproachLaneRole.UpstreamLane;
                return true;

            default:
                return false;
        }
    }

    private static TrackProbeSnapshot ProbeIndexedTrackLane(
        NativeParallelHashMap<Entity, float>.ReadOnly tramApproachIndex,
        Entity laneEntity,
        float threshold,
        bool isUpstreamLane)
    {
        if (laneEntity == Entity.Null || !tramApproachIndex.TryGetValue(laneEntity, out float curvePosition))
        {
            return new TrackProbeSnapshot(false, 0f, TransitSignalPriorityTrackProbeResult.NoTramSamples);
        }

        if (curvePosition < threshold)
        {
            return new TrackProbeSnapshot(true, curvePosition, TransitSignalPriorityTrackProbeResult.BelowThreshold);
        }

        return new TrackProbeSnapshot(
            true,
            curvePosition,
            isUpstreamLane
                ? TransitSignalPriorityTrackProbeResult.MatchOnUpstreamLane
                : TransitSignalPriorityTrackProbeResult.MatchOnApproachLane);
    }

    /// <summary>
    /// Fallback: when the junction-owned approach lane has no tram samples in the index,
    /// enumerate connected edge sublanes to find the inbound edge lane the tram is actually on.
    /// </summary>
    private static TrackProbeSnapshot TryProbeConnectedEdgeTramLane(
        PatchedTrafficLightSystem.UpdateTrafficLightsJob job,
        Entity approachLaneEntity,
        float threshold,
        out ConnectedEdgeFallbackDiagnostics diagnostics,
        out Entity matchedLaneEntity)
    {
        diagnostics = default;
        matchedLaneEntity = Entity.Null;

        if (!job.m_LaneData.TryGetComponent(approachLaneEntity, out var approachLane)
            || !job.m_OwnerData.TryGetComponent(approachLaneEntity, out var approachOwner))
        {
            return default;
        }

        Entity junctionEntity = approachOwner.m_Owner;
        if (!job.m_ConnectedEdges.TryGetBuffer(junctionEntity, out var connectedEdges))
        {
            return default;
        }

        diagnostics.ConnectedEdgeCount = (byte)System.Math.Min(connectedEdges.Length, 255);
        TrackProbeSnapshot bestProbe = default;

        for (int i = 0; i < connectedEdges.Length; i++)
        {
            Entity edgeEntity = connectedEdges[i].m_Edge;
            if (!job.m_SubLanes.TryGetBuffer(edgeEntity, out var edgeSubLanes))
            {
                continue;
            }

            for (int j = 0; j < edgeSubLanes.Length; j++)
            {
                Entity edgeLaneEntity = edgeSubLanes[j].m_SubLane;
                if (!IsTramTrackLane(job, edgeLaneEntity)
                    || !job.m_LaneData.TryGetComponent(edgeLaneEntity, out var edgeLane))
                {
                    continue;
                }

                diagnostics.TramSublaneCount = (byte)System.Math.Min(diagnostics.TramSublaneCount + 1, 255);

                if (!edgeLane.m_EndNode.Equals(approachLane.m_StartNode))
                {
                    continue;
                }

                diagnostics.PathNodeMatchCount = (byte)System.Math.Min(diagnostics.PathNodeMatchCount + 1, 255);

                if (!job.m_TramApproachIndex.TryGetValue(edgeLaneEntity, out float curvePosition))
                {
                    continue;
                }

                diagnostics.IndexHitCount = (byte)System.Math.Min(diagnostics.IndexHitCount + 1, 255);

                if (!bestProbe.HasSample || curvePosition > bestProbe.CurvePosition)
                {
                    diagnostics.BestCurvePosition = curvePosition;
                    matchedLaneEntity = edgeLaneEntity;
                    bestProbe = new TrackProbeSnapshot(
                        true,
                        curvePosition,
                        curvePosition >= threshold
                            ? TransitSignalPriorityTrackProbeResult.MatchOnConnectedApproachLane
                            : TransitSignalPriorityTrackProbeResult.BelowThreshold);
                }
            }
        }

        return bestProbe;
    }

    private static Entity TryResolveConnectedUpstreamTramLane(
        PatchedTrafficLightSystem.UpdateTrafficLightsJob job,
        Entity connectedApproachLaneEntity)
    {
        if (!job.m_LaneData.TryGetComponent(connectedApproachLaneEntity, out var connectedApproachLane)
            || !job.m_OwnerData.TryGetComponent(connectedApproachLaneEntity, out var connectedApproachOwner)
            || !job.m_EdgeData.TryGetComponent(connectedApproachOwner.m_Owner, out var connectedApproachEdge)
            || !EarlyApproachDetection.TryResolvePathNodeOwnerEntityIndex(
                connectedApproachLane.m_StartNode.GetOwnerIndex(),
                connectedApproachEdge.m_Start.Index,
                connectedApproachEdge.m_End.Index,
                out int upstreamNodeIndex))
        {
            return Entity.Null;
        }

        Entity upstreamNodeEntity = connectedApproachEdge.m_Start.Index == upstreamNodeIndex
            ? connectedApproachEdge.m_Start
            : connectedApproachEdge.m_End;

        if (!job.m_ConnectedEdges.TryGetBuffer(upstreamNodeEntity, out var connectedEdges))
        {
            return Entity.Null;
        }

        Entity currentEdgeEntity = connectedApproachOwner.m_Owner;
        Entity bestLaneEntity = Entity.Null;
        float bestCurvePosition = float.MinValue;

        for (int i = 0; i < connectedEdges.Length; i++)
        {
            Entity edgeEntity = connectedEdges[i].m_Edge;
            if (edgeEntity == currentEdgeEntity || !job.m_SubLanes.TryGetBuffer(edgeEntity, out var edgeSubLanes))
            {
                continue;
            }

            for (int j = 0; j < edgeSubLanes.Length; j++)
            {
                Entity candidateLaneEntity = edgeSubLanes[j].m_SubLane;
                if (!IsTramTrackLane(job, candidateLaneEntity)
                    || !job.m_LaneData.TryGetComponent(candidateLaneEntity, out var candidateLane)
                    || !EarlyApproachDetection.IsConnectedUpstreamEdgeCandidate(
                        currentEdgeEntity.Index,
                        edgeEntity.Index,
                        candidateLane.m_EndNode.GetOwnerIndex(),
                        connectedApproachLane.m_StartNode.GetOwnerIndex()))
                {
                    continue;
                }

                if (job.m_TramApproachIndex.TryGetValue(candidateLaneEntity, out float curvePosition))
                {
                    if (curvePosition > bestCurvePosition)
                    {
                        bestCurvePosition = curvePosition;
                        bestLaneEntity = candidateLaneEntity;
                    }

                    continue;
                }

                if (bestLaneEntity == Entity.Null)
                {
                    bestLaneEntity = candidateLaneEntity;
                }
            }
        }

        return bestLaneEntity;
    }

    private static Entity TryResolveImmediateUpstreamTramLane(
        PatchedTrafficLightSystem.UpdateTrafficLightsJob job,
        Entity approachLaneEntity)
    {
        if (!job.m_OwnerData.TryGetComponent(approachLaneEntity, out var owner)
            || !job.m_SubLanes.TryGetBuffer(owner.m_Owner, out var subLanes)
            || !job.m_LaneData.TryGetComponent(approachLaneEntity, out var approachLane))
        {
            return Entity.Null;
        }

        for (int i = 0; i < subLanes.Length; i++)
        {
            Entity candidateLaneEntity = subLanes[i].m_SubLane;
            if (candidateLaneEntity == approachLaneEntity
                || !IsTramTrackLane(job, candidateLaneEntity)
                || !job.m_LaneData.TryGetComponent(candidateLaneEntity, out var candidateLane))
            {
                continue;
            }

            if (candidateLane.m_EndNode.Equals(approachLane.m_StartNode))
            {
                return candidateLaneEntity;
            }
        }

        return Entity.Null;
    }

    private static IndexedTrackProbeDiagnostics ToIndexedTrackProbeDiagnostics(TransitApproachCandidate? candidate)
    {
        if (!candidate.HasValue)
        {
            return default;
        }

        return new IndexedTrackProbeDiagnostics(
            (IndexedTrackProbeMatch)candidate.Value.TrackSignaledLaneProbe,
            (IndexedTrackProbeMatch)candidate.Value.TrackApproachLaneProbe,
            (IndexedTrackProbeMatch)candidate.Value.TrackUpstreamLaneProbe);
    }

    private static TransitSignalPriorityTrackProbeResult ToTrackProbeResult(IndexedTrackProbeMatch match)
    {
        return (TransitSignalPriorityTrackProbeResult)match;
    }

    private static TrackLaneDebugInfo BuildTrackLaneDebugInfo(
        PatchedTrafficLightSystem.UpdateTrafficLightsJob job,
        Entity signaledLaneEntity,
        Entity approachLaneEntity,
        Entity upstreamLaneEntity)
    {
        return new TrackLaneDebugInfo
        {
            SignaledLaneEntity = signaledLaneEntity,
            ApproachLaneEntity = approachLaneEntity,
            UpstreamLaneEntity = upstreamLaneEntity,
            SignaledLaneOwnerEntity = GetLaneOwnerEntity(job, signaledLaneEntity),
            ApproachLaneOwnerEntity = GetLaneOwnerEntity(job, approachLaneEntity),
            UpstreamLaneOwnerEntity = GetLaneOwnerEntity(job, upstreamLaneEntity),
            SignaledSiblingSampleCount = CountIndexedSiblingSamples(job, signaledLaneEntity),
            ApproachSiblingSampleCount = CountIndexedSiblingSamples(job, approachLaneEntity),
            UpstreamSiblingSampleCount = CountIndexedSiblingSamples(job, upstreamLaneEntity),
            SignaledLaneIsMaster = HasMasterLane(job, signaledLaneEntity),
            ApproachLaneIsMaster = HasMasterLane(job, approachLaneEntity),
            UpstreamLaneIsMaster = HasMasterLane(job, upstreamLaneEntity),
        };
    }

    private static Entity GetLaneOwnerEntity(PatchedTrafficLightSystem.UpdateTrafficLightsJob job, Entity laneEntity)
    {
        return laneEntity != Entity.Null && job.m_OwnerData.TryGetComponent(laneEntity, out var owner)
            ? owner.m_Owner
            : Entity.Null;
    }

    private static byte CountIndexedSiblingSamples(PatchedTrafficLightSystem.UpdateTrafficLightsJob job, Entity laneEntity)
    {
        if (laneEntity == Entity.Null
            || !job.m_OwnerData.TryGetComponent(laneEntity, out var owner)
            || !job.m_SubLanes.TryGetBuffer(owner.m_Owner, out var subLanes))
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < subLanes.Length; i++)
        {
            if (job.m_TramApproachIndex.ContainsKey(subLanes[i].m_SubLane))
            {
                count++;
            }
        }

        return count >= byte.MaxValue ? byte.MaxValue : (byte)count;
    }

    private static bool HasMasterLane(PatchedTrafficLightSystem.UpdateTrafficLightsJob job, Entity laneEntity)
    {
        return laneEntity != Entity.Null && job.m_ExtraTypeHandle.m_MasterLane.HasComponent(laneEntity);
    }

    private static TransitSignalPriorityRequest CreateRequest(
        TspRequest request,
        ushort expiryTimer,
        byte currentSignalGroup,
        byte targetSignalGroup,
        LaneSignal laneSignal)
    {
        return new TransitSignalPriorityRequest
        {
            m_TargetSignalGroup = targetSignalGroup,
            m_SourceType = (byte)request.Source,
            m_Strength = request.Strength,
            m_ExpiryTimer = expiryTimer,
            m_ExtendCurrentPhase = request.ExtensionEligible
                && (laneSignal.m_Flags & LaneSignalFlags.CanExtend) != 0,
            m_OnDedicatedLane = request.OnDedicatedLane,
        };
    }

    private static TspSignalRequest ToSignalRequest(TransitSignalPriorityRequest request)
    {
        return new TspSignalRequest(
            request.m_TargetSignalGroup,
            (TspSource)request.m_SourceType,
            request.m_Strength,
            request.m_ExpiryTimer,
            request.m_ExtendCurrentPhase,
            request.m_OnDedicatedLane);
    }

    public static TransitSignalPriorityRequest SelectPreferredRequest(
        TransitSignalPriorityRequest activeRequest,
        TransitSignalPriorityRequest candidateRequest,
        bool preferActiveOnTie)
    {
        if (candidateRequest.m_Strength > activeRequest.m_Strength)
        {
            return candidateRequest;
        }

        if (activeRequest.m_Strength > candidateRequest.m_Strength)
        {
            return activeRequest;
        }

        return preferActiveOnTie ? activeRequest : candidateRequest;
    }

    private static byte GetTargetSignalGroup(ushort groupMask, byte currentSignalGroup)
    {
        if (currentSignalGroup > 0 && (groupMask & (1 << (currentSignalGroup - 1))) != 0)
        {
            return currentSignalGroup;
        }

        for (byte i = 1; i <= 16; i++)
        {
            if ((groupMask & (1 << (i - 1))) != 0)
            {
                return i;
            }
        }

        return 0;
    }
}
