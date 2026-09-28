namespace Stationary.Ftms.Dashboard.Core;

public readonly record struct CyclingMetrics(
    double TotalWorkKilojoules,
    double? PeakPower5Seconds,
    double? PeakPower30Seconds,
    double? PeakPower1Minute,
    double? PeakPower5Minutes,
    double? NormalizedPower,
    double? IntensityFactor,
    double? TrainingStressScore);