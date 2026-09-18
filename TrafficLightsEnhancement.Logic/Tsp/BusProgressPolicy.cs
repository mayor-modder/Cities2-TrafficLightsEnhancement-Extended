namespace TrafficLightsEnhancement.Logic.Tsp;

/// <summary>Transient observation for one bus on one lane; identity is owned by the runtime adapter.</summary>
public struct BusProgressState
{
    public bool HasObservation;
    public float CurvePosition;
    public ushort GreenTicksWithoutProgress;
    public bool IsSuppressed;
}

public static class BusProgressPolicy
{
    // Provisional grace in serving-green signal updates, matching the default request horizon.
    // This is deliberately not a wall-clock duration or a saved/user-configurable setting.
    public const ushort NoProgressGraceTicks = 10;
    public const float MinimumCurveProgress = 0.001f;

    /// <summary>
    /// Stops repeated priority for a bus that cannot advance despite receiving green.
    /// The anchor only moves after meaningful progress, so small forward steps accumulate.
    /// Red breaks restart an unused grace period but cannot release an already blocked bus.
    /// </summary>
    public static BusProgressState Observe(BusProgressState previous, float curvePosition, bool isServingGreen)
    {
        // Unknown geometry must not leave a vehicle permanently suppressed. The comparisons
        // also reject infinities; NaN needs its own check because ordered comparisons fail.
        if (float.IsNaN(curvePosition) || curvePosition < 0f || curvePosition > 1f)
        {
            return default;
        }

        float displacement = curvePosition - previous.CurvePosition;
        if (!previous.HasObservation
            || float.IsNaN(previous.CurvePosition)
            || previous.CurvePosition < 0f
            || previous.CurvePosition > 1f
            || displacement > MinimumCurveProgress
            || displacement < -MinimumCurveProgress)
        {
            // Substantial backward movement means a lane reversal/discontinuity, not a stall.
            // Sub-epsilon backward jitter keeps the existing anchor and cannot reset grace.
            return new BusProgressState
            {
                HasObservation = true,
                CurvePosition = curvePosition,
            };
        }

        if (isServingGreen)
        {
            if (previous.GreenTicksWithoutProgress < ushort.MaxValue)
            {
                previous.GreenTicksWithoutProgress++;
            }

            previous.IsSuppressed |= previous.GreenTicksWithoutProgress >= NoProgressGraceTicks;
        }
        else if (!previous.IsSuppressed)
        {
            previous.GreenTicksWithoutProgress = 0;
        }

        return previous;
    }
}
