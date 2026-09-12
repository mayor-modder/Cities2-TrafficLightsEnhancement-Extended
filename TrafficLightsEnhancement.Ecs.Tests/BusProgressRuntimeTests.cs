using C2VM.TrafficLightsEnhancement.Components;
using C2VM.TrafficLightsEnhancement.Systems.TrafficLightSystems.Simulation;
using Game.Net;
using TrafficLightsEnhancement.Logic.Tsp;
using Unity.Entities;
using Xunit;

namespace TrafficLightsEnhancement.Ecs.Tests;

public class BusProgressRuntimeTests
{
    [Fact]
    public void Stationary_bus_receiving_green_is_classified_as_blocked()
    {
        var sample = Sample(1);
        var progress = default(TransitSignalPriorityBusProgress);
        for (int tick = 0; tick <= 20; tick++)
            progress = BusProgressRuntime.Observe(progress, sample, isServingGreen: true);

        Assert.True(progress.m_State.IsSuppressed);
        Assert.True(BusProgressRuntime.BlocksRequest(Request(1), progress));
    }

    [Fact]
    public void Red_queue_and_brief_green_stop_keep_priority()
    {
        var sample = Sample(1);
        var progress = default(TransitSignalPriorityBusProgress);
        for (int tick = 0; tick < 100; tick++)
            progress = BusProgressRuntime.Observe(progress, sample, isServingGreen: false);
        for (int tick = 0; tick < 3; tick++)
            progress = BusProgressRuntime.Observe(progress, sample, isServingGreen: true);

        Assert.False(progress.m_State.IsSuppressed);
    }

    [Fact]
    public void Suppression_does_not_reappear_as_a_fresh_request_during_red()
    {
        var progress = Stalled();
        progress = BusProgressRuntime.Observe(progress, Sample(1), isServingGreen: false);
        Assert.True(progress.m_State.IsSuppressed);
    }

    [Fact]
    public void Progress_or_replacement_bus_or_lane_rearms_priority()
    {
        var moving = Sample(1);
        moving.CurvePosition = 0.7f;
        Assert.False(BusProgressRuntime.Observe(Stalled(), moving, true).m_State.IsSuppressed);
        Assert.False(BusProgressRuntime.Observe(Stalled(), Sample(2), true).m_State.IsSuppressed);
        var otherLane = Sample(1);
        otherLane.LaneEntity = new Entity { Index = 11, Version = 1 };
        Assert.False(BusProgressRuntime.Observe(Stalled(), otherLane, true).m_State.IsSuppressed);
        var replacement = Sample(1);
        replacement.VehicleEntity = new Entity { Index = 1, Version = 2 };
        Assert.False(BusProgressRuntime.Observe(Stalled(), replacement, true).m_State.IsSuppressed);
    }

    [Fact]
    public void Blocked_bus_does_not_block_other_bus_or_tram_latch()
    {
        Assert.False(BusProgressRuntime.BlocksRequest(Request(2), Stalled()));
        var tram = Request(1);
        tram.m_SourceType = (byte)TspSource.Track;
        Assert.False(BusProgressRuntime.BlocksRequest(tram, Stalled()));
    }

    [Theory]
    [InlineData(TrafficLightState.Ongoing, LaneSignalType.Go, 1, true)]
    [InlineData(TrafficLightState.Ongoing, LaneSignalType.Stop, 1, false)]
    [InlineData(TrafficLightState.Ongoing, LaneSignalType.Yield, 1, false)]
    [InlineData(TrafficLightState.Ending, LaneSignalType.Go, 1, false)]
    [InlineData(TrafficLightState.Changing, LaneSignalType.Go, 1, false)]
    [InlineData(TrafficLightState.Ongoing, LaneSignalType.Go, 2, false)]
    public void Only_actual_serving_green_counts(TrafficLightState state, LaneSignalType signal, int mask, bool expected)
    {
        Assert.Equal(expected, BusProgressRuntime.IsServingGreen(
            new TrafficLights { m_State = state, m_CurrentSignalGroup = 1 },
            new LaneSignal { m_Signal = signal, m_GroupMask = (ushort)mask }));
    }

    private static TransitSignalPriorityBusProgress Stalled()
    {
        var progress = default(TransitSignalPriorityBusProgress);
        for (int tick = 0; tick <= 20; tick++)
            progress = BusProgressRuntime.Observe(progress, Sample(1), true);
        return progress;
    }

    private static BusApproachSample Sample(int bus) => new()
    {
        VehicleEntity = new Entity { Index = bus, Version = 1 },
        LaneEntity = new Entity { Index = 10, Version = 1 },
        CurvePosition = 0.6f,
    };

    private static TransitSignalPriorityRequest Request(int bus) => new()
    {
        m_SourceType = (byte)TspSource.PublicCar,
        m_TargetSignalGroup = 1,
        m_Strength = 1f,
        m_ExpiryTimer = 10,
        m_BusVehicleEntity = new Entity { Index = bus, Version = 1 },
        m_BusLaneEntity = new Entity { Index = 10, Version = 1 },
    };
}
