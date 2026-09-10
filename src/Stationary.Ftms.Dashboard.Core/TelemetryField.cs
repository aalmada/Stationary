namespace Stationary.Ftms.Dashboard.Core;

[Flags]
public enum TelemetryField : uint
{
    None = 0,
    Speed = 1u << 0,
    AverageSpeed = 1u << 1,
    Cadence = 1u << 2,
    AverageCadence = 1u << 3,
    Power = 1u << 4,
    AveragePower = 1u << 5,
    Resistance = 1u << 6,
    HeartRate = 1u << 7,
    TotalDistance = 1u << 8,
    Inclination = 1u << 9,
    RampAngle = 1u << 10,
    PositiveElevation = 1u << 11,
    NegativeElevation = 1u << 12,
    InstantaneousPace = 1u << 13,
    AveragePace = 1u << 14,
    StepCount = 1u << 15,
    StrideCount = 1u << 16,
    StrokeCount = 1u << 17,
    Floors = 1u << 18,
    TotalEnergy = 1u << 19,
    EnergyPerHour = 1u << 20,
    EnergyPerMinute = 1u << 21,
    MetabolicEquivalent = 1u << 22,
    ElapsedTime = 1u << 23,
    RemainingTime = 1u << 24,
    ForceOnBelt = 1u << 25,
    Direction = 1u << 26,
}