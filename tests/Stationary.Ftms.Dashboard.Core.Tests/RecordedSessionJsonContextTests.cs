using System.Globalization;
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
    public async Task RecordedSession_RoundTripsThroughGeneratedContextAndFileStreams()
    {
        var path = GetTemporaryPath(".json");
        try
        {
            await RecordedSessionArchiveStorage.WriteJsonAsync(path, Session);

            var restored = await RecordedSessionArchiveStorage.ReadJsonAsync(path);

            await Assert.That(restored).IsNotNull();
            await Assert.That(restored!.Samples).IsEquivalentTo(Session.Samples);
            await Assert.That(restored.Events).IsEquivalentTo(Session.Events);
            await Assert.That(restored.Summary).IsEqualTo(Session.Summary);
            await Assert.That(restored.ExportedAt).IsEqualTo(Session.ExportedAt);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task RecordedSession_ReadsLegacyArchivesWrittenByReflectionSerializerFromFileStream()
    {
        var path = GetTemporaryPath(".json");
        try
        {
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, Session);
            }

            var restored = await RecordedSessionArchiveStorage.ReadJsonAsync(path);

            await Assert.That(restored).IsNotNull();
            await Assert.That(restored!.Summary).IsEqualTo(Session.Summary);
            await Assert.That(restored.Events).IsEquivalentTo(Session.Events);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task RecordedSession_WritesCsvIncrementallyInExistingFormat()
    {
        var path = GetTemporaryPath(".csv");
        try
        {
            await RecordedSessionArchiveStorage.WriteCsvAsync(path, Session);

            using var reader = new StreamReader(path);
            await Assert.That(await reader.ReadLineAsync()).IsEqualTo("TimestampUtc,RecordType,PowerWatts,SpeedKilometersPerHour,CadenceRpm,HeartRateBeatsPerMinute,Event");
            await Assert.That(await reader.ReadLineAsync()).IsEqualTo($"{Session.Samples[0].CapturedAt.ToString("O", CultureInfo.InvariantCulture)},telemetry,210,31.5,88,,");
            await Assert.That(await reader.ReadLineAsync()).IsEqualTo($"{Session.Events[0].Timestamp.ToString("O", CultureInfo.InvariantCulture)},event,,,,,Lap:Lap 1");
            await Assert.That(await reader.ReadLineAsync()).IsEqualTo(string.Empty);
            await Assert.That(await reader.ReadLineAsync()).IsEqualTo("Metric,Value");
            await Assert.That(await reader.ReadLineAsync()).IsEqualTo("Distance metres,1200.0");
            await Assert.That(await reader.ReadLineAsync()).IsEqualTo("Elapsed seconds,120");
            await Assert.That(await reader.ReadLineAsync()).IsEqualTo("Average power watts,205.0");
            await Assert.That(await reader.ReadLineAsync()).IsEqualTo("Maximum power watts,410.0");
            await Assert.That(await reader.ReadLineAsync()).IsEqualTo("Work kilojoules,24.6");
            await Assert.That(await reader.ReadLineAsync()).IsNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string GetTemporaryPath(string extension) =>
        Path.Combine(Path.GetTempPath(), $"stationary-session-{Guid.NewGuid():N}{extension}");
}
