using Game.Vehicles;
using Unity.Entities;

namespace C2VM.TrafficLightsEnhancement.Components;

public enum TransitSignalPriorityBusProbeResult : byte
{
    None = 0,
    NoBusSamples = 1,
    MatchOnSignaledLane = 2,
    MatchOnApproachLane = 3,
    MatchOnConnectedApproachLane = 4,
}

public enum TransitSignalPriorityBusDecision : byte
{
    None = 0,
    NoEligibleSample = 1,
    PriorityDisabled = 2,
    RequestEmitted = 3,
    SuppressedBoarding = 4,
    SuppressedUnknownStopRelation = 5,
    SuppressedAmbiguousLaneChange = 7,
    SuppressedNearSideStop = 8,
    SuppressedNoProgress = 9,
}

public struct TransitSignalPriorityBusApproachDebugInfo : IComponentData
{
    public int m_BusApproachIndexLaneCount;
    public byte m_ScannedSignalLaneCount;
    public byte m_BusHitCount;
    public TransitSignalPriorityBusProbeResult m_BusProbe;
    public TransitSignalPriorityBusDecision m_BusDecision;
    public byte m_BusTargetSignalGroup;
    public Entity m_BusLaneEntity;
    public Entity m_BusVehicleEntity;
    public float m_BusCurvePosition;
    public float m_BusChangeProgress;
    public float m_BusSpeed;
    public bool m_BusLaneIsPublicOnly;
    public bool m_BusIsChangingLane;
    public bool m_BusHasNavigation;
    public byte m_BusNavigationLaneCount;
    public PublicTransportFlags m_BusPublicTransportState;
    public CarLaneFlags m_BusVehicleLaneFlags;
    public ushort m_BusNoProgressTicks;
    public bool m_BusNoProgressSuppressed;
}
