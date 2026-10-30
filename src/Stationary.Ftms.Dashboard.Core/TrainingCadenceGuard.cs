namespace Stationary.Ftms.Dashboard.Core;

public enum TrainingCadenceGuardTransition
{
    None,
    Stalled,
    Recovered,
}

// Detects ERG stalls: a trainer holding power while cadence collapses makes each pedal stroke heavier.
// Uses hysteresis so riders hovering near the stall threshold do not toggle the target repeatedly.
public sealed class TrainingCadenceGuard
{
    public const double StallCadenceRpm = 35d;
    public const double PedallingCadenceRpm = 45d;
    // A 3 s window missed a real 3 s stop; the bike then needed 4 s to release ERG resistance and power surged to 190% of target on restart.
    public static readonly TimeSpan StallDuration = TimeSpan.FromSeconds(1.5);

    private DateTimeOffset? lowCadenceSince;

    public bool IsStalled { get; private set; }

    public static bool IsPedalling(double? cadenceRpm) => cadenceRpm >= PedallingCadenceRpm;

    // Recovery is immediate. After an abrupt stop the test bike held about 3-3.6 W/rpm for 3-5 s of the restart
    // whatever was commanded (0-130 W holds, repeated holds, FTMS Pause and Start), so waiting buys nothing;
    // the guard's value is stopping gradual ERG death spirals, not the restart spike.
    public TrainingCadenceGuardTransition Update(DateTimeOffset timestamp, double? cadenceRpm)
    {
        if (cadenceRpm is not double cadence)
        {
            return TrainingCadenceGuardTransition.None;
        }

        if (IsStalled)
        {
            if (cadence < PedallingCadenceRpm)
            {
                return TrainingCadenceGuardTransition.None;
            }

            IsStalled = false;
            lowCadenceSince = null;
            return TrainingCadenceGuardTransition.Recovered;
        }

        if (cadence >= StallCadenceRpm)
        {
            lowCadenceSince = null;
            return TrainingCadenceGuardTransition.None;
        }

        lowCadenceSince ??= timestamp;
        if (timestamp - lowCadenceSince < StallDuration)
        {
            return TrainingCadenceGuardTransition.None;
        }

        IsStalled = true;
        return TrainingCadenceGuardTransition.Stalled;
    }

    public void Reset()
    {
        IsStalled = false;
        lowCadenceSince = null;
    }
}