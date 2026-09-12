using Unity.Entities;

namespace C2VM.TrafficLightsEnhancement.Components;

public struct TransitSignalPriorityRequest : IComponentData
{
    public byte m_TargetSignalGroup;
    public byte m_SourceType;
    public float m_Strength;
    public uint m_ExpiryTimer;
    public bool m_ExtendCurrentPhase;
    public bool m_OnDedicatedLane;
    public Entity m_BusVehicleEntity;
    public Entity m_BusLaneEntity;
}
