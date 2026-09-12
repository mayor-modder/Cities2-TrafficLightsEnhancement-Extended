using System;
using C2VM.TrafficLightsEnhancement.Components;
using C2VM.TrafficLightsEnhancement.Systems.TrafficLightSystems.Simulation;
using Game.Net;
using TrafficLightsEnhancement.Logic.Tsp;
using Unity.Entities;
using Xunit;
using EcsTspRuntime = C2VM.TrafficLightsEnhancement.Systems.TrafficLightSystems.Simulation.TransitSignalPriorityRuntime;

namespace TrafficLightsEnhancement.Ecs.Tests;

public unsafe class BusProgressBatchTests
{
    [Fact]
    public void Repeated_stall_filters_candidate_and_retains_one_history_per_bus_lane()
    {
        var states = Array.Empty<TransitSignalPriorityBusProgress>();
        BusRequestCandidate[] candidates = null;
        for (int tick = 0; tick <= 10; tick++)
        {
            candidates = new[] { Candidate(1), Candidate(1), Candidate(1) };
            states = Observe(candidates, states);
            Assert.Single(states);
            Assert.Equal(tick, states[0].m_State.GreenTicksWithoutProgress);
            Assert.Equal(tick < 10, candidates[0].IsEligibleRequest);
        }

        Assert.True(states[0].m_State.IsSuppressed);
        Assert.All(candidates, candidate => Assert.False(candidate.IsEligibleRequest));
    }

    [Theory]
    [InlineData(LaneSignalType.Stop)]
    [InlineData(LaneSignalType.Yield)]
    public void Shared_lane_with_a_non_green_movement_never_spends_grace(LaneSignalType otherSignal)
    {
        var states = Array.Empty<TransitSignalPriorityBusProgress>();
        for (int tick = 0; tick < 30; tick++)
        {
            var otherMovement = Candidate(1);
            otherMovement.LaneSignal.m_Signal = otherSignal;
            var candidates = new[] { Candidate(1), otherMovement };
            states = Observe(candidates, states);
            Assert.Equal(0, states[0].m_State.GreenTicksWithoutProgress);
            Assert.False(states[0].m_State.IsSuppressed);
            Assert.All(candidates, candidate => Assert.True(candidate.IsEligibleRequest));
        }
    }

    [Fact]
    public void Shared_lane_with_an_ineligible_observed_movement_never_spends_grace()
    {
        var states = Array.Empty<TransitSignalPriorityBusProgress>();
        for (int tick = 0; tick < 30; tick++)
        {
            var ambiguous = Candidate(1);
            ambiguous.IsEligibleRequest = false;
            var candidates = new[] { Candidate(1), ambiguous };
            states = Observe(candidates, states);
            Assert.Equal(0, states[0].m_State.GreenTicksWithoutProgress);
            Assert.True(candidates[0].IsEligibleRequest);
        }
    }

    [Fact]
    public void Observed_but_ineligible_bus_retains_suppression_when_it_becomes_eligible_again()
    {
        var states = Stalled();
        var ineligible = Candidate(1);
        ineligible.IsEligibleRequest = false;
        for (int tick = 0; tick < 20; tick++)
            states = Observe(new[] { ineligible }, states);

        Assert.Single(states);
        Assert.True(states[0].m_State.IsSuppressed);
        var recovered = new[] { Candidate(1) };
        states = Observe(recovered, states);
        Assert.False(recovered[0].IsEligibleRequest);
    }

    [Fact]
    public void Absence_prunes_history_and_later_observation_gets_a_new_grace_period()
    {
        var states = Observe(Array.Empty<BusRequestCandidate>(), Stalled());
        Assert.Empty(states);
        var returned = new[] { Candidate(1) };
        states = Observe(returned, states);

        Assert.True(returned[0].IsEligibleRequest);
        Assert.Equal(0, states[0].m_State.GreenTicksWithoutProgress);
    }

    [Fact]
    public void Forward_progress_and_replacement_bus_rearm_without_inheriting_stall()
    {
        var moving = Candidate(1);
        moving.Sample.CurvePosition = 0.7f;
        var candidates = new[] { moving, Candidate(2) };
        var states = Observe(candidates, Stalled());

        Assert.Equal(2, states.Length);
        Assert.All(candidates, candidate => Assert.True(candidate.IsEligibleRequest));
        Assert.All(states, state => Assert.False(state.m_State.IsSuppressed));
    }

    [Fact]
    public void Blocked_candidate_is_filtered_while_another_bus_can_win_selection()
    {
        var candidates = new[] { Candidate(1), Candidate(2) };
        var states = Observe(candidates, Stalled());

        Assert.False(candidates[0].IsEligibleRequest);
        Assert.True(candidates[1].IsEligibleRequest);
        TspRequest? selected = null;
        int selectedBus = 0;
        foreach (var candidate in candidates)
        {
            if (!candidate.IsEligibleRequest)
                continue;
            selected = EcsTspRuntime.SelectPreferredRequestAndRole(
                selected, TransitSignalPriorityApproachLaneRole.None,
                candidate.Request, TransitSignalPriorityApproachLaneRole.ApproachLane, out _);
            selectedBus = candidate.Sample.VehicleEntity.Index;
        }

        Assert.True(selected.HasValue);
        Assert.Equal(2, selectedBus);
        Assert.True(states[0].m_State.IsSuppressed);
    }

    [Fact]
    public void Suppressed_bus_prior_cannot_survive_through_actual_request_latching()
    {
        var states = Stalled();
        fixed (TransitSignalPriorityBusProgress* pointer = states)
        {
            bool resolved = BusProgressRuntime.TryRefreshOrLatchRequest(
                null, Request(1), pointer, states.Length, 10, 1, out _, out bool hadExisting);

            Assert.False(resolved);
            Assert.False(hadExisting);
        }
    }

    [Fact]
    public void Tram_prior_still_latches_and_outranks_a_fresh_other_bus()
    {
        var states = Stalled();
        var tram = Request(1);
        tram.m_SourceType = (byte)TspSource.Track;
        tram.m_TargetSignalGroup = 3;
        fixed (TransitSignalPriorityBusProgress* pointer = states)
        {
            Assert.True(BusProgressRuntime.TryRefreshOrLatchRequest(
                Request(2), tram, pointer, states.Length, 10, 1, out var active, out bool hadExisting));

            Assert.True(hadExisting);
            Assert.Equal((byte)TspSource.Track, active.m_SourceType);
            Assert.Equal(3, active.m_TargetSignalGroup);
            Assert.Equal(9u, active.m_ExpiryTimer);
            Assert.Equal(Entity.Null, active.m_BusVehicleEntity);
        }
    }

    [Fact]
    public void Another_bus_replaces_blocked_prior_and_keeps_its_own_identity_through_latching()
    {
        var states = Stalled();
        fixed (TransitSignalPriorityBusProgress* pointer = states)
        {
            Assert.True(BusProgressRuntime.TryRefreshOrLatchRequest(
                Request(2), Request(1), pointer, states.Length, 10, 1, out var active, out bool hadExisting));
            Assert.False(hadExisting);
            Assert.Equal(Request(2).m_BusVehicleEntity, active.m_BusVehicleEntity);
            Assert.Equal(Request(2).m_BusLaneEntity, active.m_BusLaneEntity);
            Assert.Equal(10u, active.m_ExpiryTimer);

            Assert.True(BusProgressRuntime.TryRefreshOrLatchRequest(
                null, active, pointer, states.Length, 10, 1, out var latched, out hadExisting));
            Assert.True(hadExisting);
            Assert.Equal(active.m_BusVehicleEntity, latched.m_BusVehicleEntity);
            Assert.Equal(active.m_BusLaneEntity, latched.m_BusLaneEntity);
            Assert.Equal(9u, latched.m_ExpiryTimer);
        }
    }

    private static TransitSignalPriorityRequest Request(int bus) => new()
    {
        m_SourceType = (byte)TspSource.PublicCar,
        m_TargetSignalGroup = 1,
        m_Strength = 1f,
        m_ExpiryTimer = 10,
        m_OnDedicatedLane = true,
        m_BusVehicleEntity = new Entity { Index = bus, Version = 1 },
        m_BusLaneEntity = new Entity { Index = bus + 10, Version = 1 },
    };

    private static TransitSignalPriorityBusProgress[] Stalled()
    {
        var states = Array.Empty<TransitSignalPriorityBusProgress>();
        for (int tick = 0; tick <= 10; tick++)
            states = Observe(new[] { Candidate(1) }, states);
        Assert.True(states[0].m_State.IsSuppressed);
        return states;
    }

    private static TransitSignalPriorityBusProgress[] Observe(
        BusRequestCandidate[] candidates, TransitSignalPriorityBusProgress[] previous)
    {
        var output = new TransitSignalPriorityBusProgress[candidates.Length];
        int count;
        fixed (BusRequestCandidate* candidatePointer = candidates)
        fixed (TransitSignalPriorityBusProgress* previousPointer = previous)
        fixed (TransitSignalPriorityBusProgress* outputPointer = output)
        {
            count = BusProgressRuntime.ObserveAndFilterCandidates(
                candidatePointer, candidates.Length, previousPointer, previous.Length, outputPointer,
                new TrafficLights { m_State = TrafficLightState.Ongoing, m_CurrentSignalGroup = 1 });
        }
        Array.Resize(ref output, count);
        return output;
    }

    private static BusRequestCandidate Candidate(int bus) => new()
    {
        IsEligibleRequest = true,
        Request = new TspRequest(TspSource.PublicCar, 1f, true, true),
        TargetSignalGroup = 1,
        LaneSignal = new LaneSignal { m_Signal = LaneSignalType.Go, m_GroupMask = 1 },
        Sample = new BusApproachSample
        {
            VehicleEntity = new Entity { Index = bus, Version = 1 },
            LaneEntity = new Entity { Index = bus + 10, Version = 1 },
            CurvePosition = 0.6f,
        },
    };
}
