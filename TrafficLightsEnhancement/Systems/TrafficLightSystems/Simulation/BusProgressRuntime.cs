using C2VM.TrafficLightsEnhancement.Components;
using Game.Net;
using TrafficLightsEnhancement.Logic.Tsp;
using Unity.Entities;

namespace C2VM.TrafficLightsEnhancement.Systems.TrafficLightSystems.Simulation;

public struct BusRequestCandidate
{
    public bool IsEligibleRequest;
    public TspRequest Request;
    public BusApproachSample Sample;
    public LaneSignal LaneSignal;
    public byte TargetSignalGroup;
}

public static class BusProgressRuntime
{
    /// <summary>
    /// Observes each bus/lane once and filters blocked requests before arbitration.
    /// Output capacity must be at least candidateCount; input and output histories must not alias.
    /// Retention follows observed samples, including ineligible ones, rather than request emission.
    /// </summary>
    public static unsafe int ObserveAndFilterCandidates(
        BusRequestCandidate* candidates, int candidateCount,
        TransitSignalPriorityBusProgress* previousStates, int previousCount,
        TransitSignalPriorityBusProgress* outputStates, TrafficLights trafficLights)
    {
        int outputCount = 0;
        for (int i = 0; i < candidateCount; i++)
        {
            var sample = candidates[i].Sample;
            if (FindProgress(outputStates, outputCount, sample) >= 0)
                continue;

            // We do not know which turn a shared-approach bus intends. A green movement
            // alongside a red/yield/otherwise ambiguous one is not proof of usable green.
            bool isServingGreen = true;
            for (int j = 0; j < candidateCount; j++)
            {
                if (candidates[j].Sample.VehicleEntity == sample.VehicleEntity
                    && candidates[j].Sample.LaneEntity == sample.LaneEntity)
                    isServingGreen &= candidates[j].IsEligibleRequest
                        && IsServingGreen(trafficLights, candidates[j].LaneSignal);
            }

            int previousIndex = FindProgress(previousStates, previousCount, sample);
            var previous = previousIndex >= 0 ? previousStates[previousIndex] : default;
            outputStates[outputCount++] = Observe(previous, sample, isServingGreen);
        }

        // Finish all observations before mutating eligibility: duplicate candidates must
        // use the same original observations, independent of their enumeration order.
        for (int i = 0; i < candidateCount; i++)
        {
            int stateIndex = FindProgress(outputStates, outputCount, candidates[i].Sample);
            candidates[i].IsEligibleRequest &= !outputStates[stateIndex].m_State.IsSuppressed;
        }

        return outputCount;
    }

    private static unsafe int FindProgress(
        TransitSignalPriorityBusProgress* states, int count, BusApproachSample sample)
    {
        for (int i = 0; i < count; i++)
        {
            if (states[i].m_VehicleEntity == sample.VehicleEntity
                && states[i].m_LaneEntity == sample.LaneEntity)
                return i;
        }
        return -1;
    }

    public static TransitSignalPriorityBusProgress Observe(
        TransitSignalPriorityBusProgress previous, BusApproachSample sample, bool isServingGreen)
    {
        bool sameBusAndLane = previous.m_VehicleEntity == sample.VehicleEntity
            && previous.m_LaneEntity == sample.LaneEntity;
        return new TransitSignalPriorityBusProgress
        {
            m_VehicleEntity = sample.VehicleEntity,
            m_LaneEntity = sample.LaneEntity,
            m_State = BusProgressPolicy.Observe(
                sameBusAndLane ? previous.m_State : default,
                sample.CurvePosition,
                isServingGreen),
        };
    }

    public static bool BlocksRequest(TransitSignalPriorityRequest request, TransitSignalPriorityBusProgress progress)
    {
        return request.m_SourceType == (byte)TspSource.PublicCar
            && progress.m_State.IsSuppressed
            && request.m_BusVehicleEntity != Entity.Null
            && request.m_BusVehicleEntity == progress.m_VehicleEntity
            && request.m_BusLaneEntity == progress.m_LaneEntity;
    }

    /// <summary>Filters only identified stalled bus latches, then preserves the selected bus identity.</summary>
    public static unsafe bool TryRefreshOrLatchRequest(
        TransitSignalPriorityRequest? freshRequest, TransitSignalPriorityRequest? existingRequest,
        TransitSignalPriorityBusProgress* progressStates, int progressCount,
        ushort requestHorizonTicks, byte currentSignalGroup,
        out TransitSignalPriorityRequest request, out bool hadExistingRequest)
    {
        hadExistingRequest = existingRequest.HasValue
            && existingRequest.Value.m_TargetSignalGroup > 0
            && existingRequest.Value.m_Strength > 0f;
        for (int i = 0; hadExistingRequest && i < progressCount; i++)
        {
            if (BlocksRequest(existingRequest.Value, progressStates[i]))
                hadExistingRequest = false;
        }

        request = default;
        if (!TspPreemptionPolicy.TryRefreshOrLatchRequest(
                freshRequest.HasValue ? ToSignalRequest(freshRequest.Value) : (TspSignalRequest?)null,
                hadExistingRequest ? ToSignalRequest(existingRequest.Value) : (TspSignalRequest?)null,
                requestHorizonTicks, currentSignalGroup, out var activeRequest))
            return false;

        request = new TransitSignalPriorityRequest
        {
            m_TargetSignalGroup = (byte)activeRequest.TargetSignalGroup,
            m_SourceType = (byte)activeRequest.Source,
            m_Strength = activeRequest.Strength,
            m_ExpiryTimer = activeRequest.ExpiryTimer,
            m_ExtendCurrentPhase = activeRequest.ExtendCurrentPhase,
            m_OnDedicatedLane = activeRequest.OnDedicatedLane,
        };
        if (activeRequest.Source == TspSource.PublicCar)
        {
            var identitySource = freshRequest.HasValue
                && freshRequest.Value.m_SourceType == (byte)TspSource.PublicCar
                ? freshRequest.Value
                : existingRequest.Value;
            request.m_BusVehicleEntity = identitySource.m_BusVehicleEntity;
            request.m_BusLaneEntity = identitySource.m_BusLaneEntity;
        }
        return true;
    }

    private static TspSignalRequest ToSignalRequest(TransitSignalPriorityRequest request)
    {
        return new TspSignalRequest(request.m_TargetSignalGroup, (TspSource)request.m_SourceType,
            request.m_Strength, request.m_ExpiryTimer, request.m_ExtendCurrentPhase, request.m_OnDedicatedLane);
    }

    public static bool IsServingGreen(TrafficLights lights, LaneSignal laneSignal)
    {
        // A matching phase number alone also matches amber/all-red transitions. Yield
        // movements may legitimately wait for conflicts, so they do not consume grace.
        return lights.m_State == TrafficLightState.Ongoing
            && lights.m_CurrentSignalGroup > 0
            && lights.m_CurrentSignalGroup <= 16
            && laneSignal.m_Signal == LaneSignalType.Go
            && (laneSignal.m_GroupMask & (1 << (lights.m_CurrentSignalGroup - 1))) != 0;
    }
}
