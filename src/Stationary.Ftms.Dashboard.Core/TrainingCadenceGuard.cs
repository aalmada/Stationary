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
    public static readonly TimeSpan StallDuration = TimeSpan.FromSeconds(3);

    private DateTimeOffset? lowCadenceSince;

    public bool IsStalled { get; private set; }

    public static bool IsPedalling(double? cadenceRpm) => cadenceRpm >= PedallingCadenceRpm;

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