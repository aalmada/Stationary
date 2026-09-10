using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class TelemetryFieldObservationTests
{
    [Test]
    public async Task Observe_PreservesUnavailableStateWhenNextFragmentOmitsField()
    {
        var unavailable = TelemetryFieldObservation.Empty.Observe(
            new(DateTimeOffset.UnixEpoch, true, null, false),
            TimeSpan.FromSeconds(15));

        var omitted = unavailable.Observe(
            new(DateTimeOffset.UnixEpoch.AddSeconds(1), false, 42, false),
            TimeSpan.FromSeconds(15));

        using (Assert.Multiple())
        {
            await Assert.That(omitted.IsUnavailable).IsTrue();
            await Assert.That(omitted.IsObserved).IsFalse();
        }
    }

    [Test]
    public async Task Observe_FlagsUnchangedProgressDuringActivity()
    {
        var initial = TelemetryFieldObservation.Empty.Observe(
            new(DateTimeOffset.UnixEpoch, true, 100, true),
            TimeSpan.FromSeconds(15));

        var stalled = initial.Observe(
            new(DateTimeOffset.UnixEpoch.AddSeconds(15), true, 100, true),
            TimeSpan.FromSeconds(15));

        using (Assert.Multiple())
        {
            await Assert.That(stalled.IsObserved).IsTrue();
            await Assert.That(stalled.IsStalled).IsTrue();
        }
    }

    [Test]
    public async Task Observe_DoesNotFlagStableSampleAndClearsStallWhenProgressResumes()
    {
        var initial = TelemetryFieldObservation.Empty.Observe(
            new(DateTimeOffset.UnixEpoch, true, 100, false),
            TimeSpan.FromSeconds(15));
        var stableSample = initial.Observe(
            new(DateTimeOffset.UnixEpoch.AddMinutes(1), true, 100, false),
            TimeSpan.FromSeconds(15));
        var stalled = initial.Observe(
            new(DateTimeOffset.UnixEpoch.AddSeconds(15), true, 100, true),
            TimeSpan.FromSeconds(15));
        var recovered = stalled.Observe(
            new(DateTimeOffset.UnixEpoch.AddSeconds(16), true, 101, true),
            TimeSpan.FromSeconds(15));

        using (Assert.Multiple())
        {
            await Assert.That(stableSample.IsStalled).IsFalse();
            await Assert.That(recovered.IsStalled).IsFalse();
            await Assert.That(recovered.LastValue).IsEqualTo(101);
        }
    }
}