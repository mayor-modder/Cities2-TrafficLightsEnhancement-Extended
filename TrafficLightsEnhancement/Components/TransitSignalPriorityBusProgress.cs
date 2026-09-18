using TrafficLightsEnhancement.Logic.Tsp;
using Unity.Entities;

namespace C2VM.TrafficLightsEnhancement.Components;

// Transient per-junction history, retained even when the bus request is suppressed.
[InternalBufferCapacity(4)]
public struct TransitSignalPriorityBusProgress : IBufferElementData
{
    public Entity m_VehicleEntity;
    public Entity m_LaneEntity;
    public BusProgressState m_State;
}
