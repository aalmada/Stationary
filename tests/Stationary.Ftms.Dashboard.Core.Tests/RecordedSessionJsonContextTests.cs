using System.Text.Json;
using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class RecordedSessionJsonContextTests
{
    private static readonly RecordedSession Session = new(
        1,
        DateTimeOffset.UnixEpoch.AddHours(1),
        [new RecordedTelemetrySample(DateTimeOffset.UnixEpoch, 210d, 31.5d, 88d, null)],
        [new SessionEventMarker(DateTimeOffset.UnixEpoch, SessionEventType.Lap, "Lap 1")],
        new SessionSummary(1200d, TimeSpan.FromMinutes(2), 205d, 410d, new CyclingMetrics(24.6d, 400d, null, null, null, 215d, 0.86d, 2.5d)));

    [Test]
    public async Task RecordedSession_RoundTripsThroughGeneratedContext()
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(Session, RecordedSessionJsonContext.Default.RecordedSession);

        var restored = JsonSerializer.Deserialize(json, RecordedSessionJsonContext.Default.RecordedSession);

        await Assert.That(restored).IsNotNull();
        await Assert.That(restored!.Samples).IsEquivalentTo(Session.Samples);
        await Assert.That(restored.Events).IsEquivalentTo(Session.Events);
        await Assert.That(restored.Summary).IsEqualTo(Session.Summary);
        await Assert.That(restored.ExportedAt).IsEqualTo(Session.ExportedAt);
    }

    [Test]
    public async Task RecordedSession_ReadsArchivesWrittenByReflectionSerializer()
    {
        var legacyJson = JsonSerializer.Serialize(Session);

        var restored = JsonSerializer.Deserialize(legacyJson, RecordedSessionJsonContext.Default.RecordedSession);

        await Assert.That(restored).IsNotNull();
        await Assert.That(restored!.Summary).IsEqualTo(Session.Summary);
        await Assert.That(restored.Events).IsEquivalentTo(Session.Events);
    }
}
