namespace Stationary.Ftms.Dashboard.Core;

public enum TrainingCadenceGuardTransition
{
    None,
    Stalled,
    Recovered,
}

// Detects ERG stalls: a trainer holding power while cadence collapses makes each pedal stroke heavier.
// Uses hysteresis so riders hovering near the stall threshold do not toggle the target repeatedly.
public readonly record struct TrainingCadenceGuard(bool IsStalled, DateTimeOffset? LowCadenceSince)
{
    public const double StallCadenceRpm = 35d;
    public const double PedallingCadenceRpm = 45d;
    // A 3 s window missed a real 3 s stop; the bike then needed 4 s to release ERG resistance and power surged to 190% of target on restart.
    public static readonly TimeSpan StallDuration = TimeSpan.FromSeconds(1.5);

    public static bool IsPedalling(double? cadenceRpm) => cadenceRpm >= PedallingCadenceRpm;

    // Recovery is immediate. After an abrupt stop the test bike held about 3-3.6 W/rpm for 3-5 s of the restart
    // whatever was commanded (0-130 W holds, repeated holds, FTMS Pause and Start), so waiting buys nothing;
    // the guard's value is stopping gradual ERG death spirals, not the restart spike.
    public (TrainingCadenceGuard Guard, TrainingCadenceGuardTransition Transition) Update(DateTimeOffset timestamp, double? cadenceRpm) =>
        cadenceRpm switch
        {
            null => (this, TrainingCadenceGuardTransition.None),
            double cadence when IsStalled => cadence < PedallingCadenceRpm
                ? (this, TrainingCadenceGuardTransition.None)
                : (default, TrainingCadenceGuardTransition.Recovered),
            >= StallCadenceRpm => (default, TrainingCadenceGuardTransition.None),
            _ => (LowCadenceSince ?? timestamp) is var since && timestamp - since < StallDuration
                ? (this with { LowCadenceSince = since }, TrainingCadenceGuardTransition.None)
                : (new(true, since), TrainingCadenceGuardTransition.Stalled),
        };
}