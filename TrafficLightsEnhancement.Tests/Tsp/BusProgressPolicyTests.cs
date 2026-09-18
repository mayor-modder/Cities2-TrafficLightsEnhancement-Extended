using TrafficLightsEnhancement.Logic.Tsp;
using Xunit;

namespace TrafficLightsEnhancement.Tests.Tsp;

public class BusProgressPolicyTests
{
    [Fact]
    public void First_observation_anchors_without_spending_green_grace()
    {
        var state = BusProgressPolicy.Observe(default, 0.8f, true);

        Assert.True(state.HasObservation);
        Assert.Equal(0.8f, state.CurvePosition);
        Assert.Equal(0, state.GreenTicksWithoutProgress);
        Assert.False(state.IsSuppressed);
    }

    [Fact]
    public void Stationary_bus_is_suppressed_after_ten_served_green_deltas()
    {
        var state = BusProgressPolicy.Observe(default, 0.8f, true);
        state = ObserveStationary(state, 9, true);
        Assert.False(state.IsSuppressed);

        state = BusProgressPolicy.Observe(state, 0.8f, true);
        Assert.True(state.IsSuppressed);
        Assert.Equal(10, state.GreenTicksWithoutProgress);
    }

    [Fact]
    public void Red_queue_does_not_spend_green_grace()
    {
        var state = BusProgressPolicy.Observe(default, 0.8f, false);
        state = ObserveStationary(state, 100, false);

        Assert.False(state.IsSuppressed);
        Assert.Equal(0, state.GreenTicksWithoutProgress);
    }

    [Fact]
    public void Red_break_restarts_grace_for_a_bus_not_yet_suppressed()
    {
        var state = BusProgressPolicy.Observe(default, 0.8f, true);
        state = ObserveStationary(state, 9, true);
        state = BusProgressPolicy.Observe(state, 0.8f, false);
        state = ObserveStationary(state, 9, true);

        Assert.False(state.IsSuppressed);
        Assert.Equal(9, state.GreenTicksWithoutProgress);
    }

    [Fact]
    public void Brief_stop_then_forward_movement_restarts_grace()
    {
        var state = BusProgressPolicy.Observe(default, 0.8f, true);
        state = ObserveStationary(state, 9, true);
        state = BusProgressPolicy.Observe(state, 0.81f, true);

        Assert.False(state.IsSuppressed);
        Assert.Equal(0, state.GreenTicksWithoutProgress);
        Assert.Equal(0.81f, state.CurvePosition);
    }

    [Fact]
    public void Slow_cumulative_forward_movement_counts_as_progress()
    {
        var state = BusProgressPolicy.Observe(default, 0.8f, true);
        for (int tick = 1; tick <= 20; tick++)
        {
            state = BusProgressPolicy.Observe(state, 0.8f + tick * 0.0004f, true);
            Assert.False(state.IsSuppressed);
        }

        Assert.True(state.CurvePosition > 0.807f);
    }

    [Fact]
    public void Small_back_and_forth_jitter_does_not_rearm_a_stationary_bus()
    {
        var state = BusProgressPolicy.Observe(default, 0.8f, true);
        for (int tick = 0; tick < 10; tick++)
        {
            state = BusProgressPolicy.Observe(state, tick % 2 == 0 ? 0.8004f : 0.7996f, true);
        }

        Assert.True(state.IsSuppressed);
        Assert.Equal(0.8f, state.CurvePosition);
    }

    [Fact]
    public void Suppression_survives_red_and_the_next_green()
    {
        var state = SuppressedState();
        state = ObserveStationary(state, 30, false);
        Assert.True(state.IsSuppressed);

        state = BusProgressPolicy.Observe(state, 0.8f, true);
        Assert.True(state.IsSuppressed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Forward_progress_rearms_a_suppressed_bus_during_any_signal(bool isServingGreen)
    {
        var state = BusProgressPolicy.Observe(SuppressedState(), 0.81f, isServingGreen);

        Assert.False(state.IsSuppressed);
        Assert.Equal(0, state.GreenTicksWithoutProgress);
        Assert.Equal(0.81f, state.CurvePosition);
    }

    [Fact]
    public void Substantial_backward_curve_discontinuity_starts_a_new_observation()
    {
        var state = BusProgressPolicy.Observe(SuppressedState(), 0.2f, true);

        Assert.True(state.HasObservation);
        Assert.False(state.IsSuppressed);
        Assert.Equal(0, state.GreenTicksWithoutProgress);
        Assert.Equal(0.2f, state.CurvePosition);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(-0.01f)]
    [InlineData(1.01f)]
    public void Invalid_curve_clears_suppression_and_observation(float curvePosition)
    {
        var state = BusProgressPolicy.Observe(SuppressedState(), curvePosition, true);

        Assert.False(state.HasObservation);
        Assert.False(state.IsSuppressed);
        Assert.Equal(0, state.GreenTicksWithoutProgress);
        var recovered = BusProgressPolicy.Observe(state, 0.8f, true);
        Assert.True(recovered.HasObservation);
        Assert.False(recovered.IsSuppressed);
        Assert.Equal(0, recovered.GreenTicksWithoutProgress);
    }

    [Fact]
    public void Green_counter_saturates_instead_of_wrapping()
    {
        var state = SuppressedState();
        state.GreenTicksWithoutProgress = ushort.MaxValue;
        state = BusProgressPolicy.Observe(state, 0.8f, true);

        Assert.Equal(ushort.MaxValue, state.GreenTicksWithoutProgress);
        Assert.True(state.IsSuppressed);
    }

    private static BusProgressState SuppressedState()
    {
        return ObserveStationary(BusProgressPolicy.Observe(default, 0.8f, true), 10, true);
    }

    private static BusProgressState ObserveStationary(BusProgressState state, int ticks, bool green)
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            state = BusProgressPolicy.Observe(state, 0.8f, green);
        }

        return state;
    }
}
