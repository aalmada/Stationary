using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class RideHistoryTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 14, 30, 0, TimeSpan.Zero);

    [Test]
    public async Task GetFileName_UsesTheUtcStartTime()
    {
        var fileName = RideHistory.GetFileName(new DateTimeOffset(2026, 11, 2, 15, 30, 5, TimeSpan.FromHours(1)));

        await Assert.That(fileName).IsEqualTo("ride-20261102-143005.json");
    }

    [Test]
    public async Task CreateEntry_NamesTheRideAfterTheWorkout()
    {
        var samples = RideAnalyticsTests.CreateSamples(1_201, _ => (200d, 125d));
        var summary = new SessionSummary(0d, TimeSpan.FromMinutes(20), 200d, 200d, new(240d, null, null, null, null, 200d, null, 30d));
        var workout = new RecordedSession(
            RecordedSession.CurrentFormatVersion,
            Start.AddMinutes(21),
            samples,
            [new(Start, SessionEventType.Start, "Start"), new(Start, SessionEventType.Training, "Workout Aerobic Foundation · Start · 30 min")],
            summary);

        var entry = RideHistory.CreateEntry(workout);
        var free = RideHistory.CreateEntry(workout with { Events = [] });

        using (Assert.Multiple())
        {
            await Assert.That(entry.Name).IsEqualTo("Aerobic Foundation");
            await Assert.That(entry.StartedAt).IsEqualTo(Start);
            await Assert.That(entry.AverageHeartRate).IsEqualTo(125d);
            await Assert.That(entry.EfficiencyFactor).IsEqualTo(1.6d);
            await Assert.That(entry.VariabilityIndex).IsEqualTo(1d);
            await Assert.That(entry.TrainingStressScore).IsEqualTo(30d);
            await Assert.That(free.Name).IsEqualTo("Free ride");
        }
    }

    [Test]
    public async Task Upsert_ReplacesTheSameRideAndKeepsChronologicalOrder()
    {
        var first = Entry(Start, 1.5d);
        var second = Entry(Start.AddDays(1), 1.6d);
        var updated = second with { Name = "Updated" };

        var rides = RideHistory.Upsert(RideHistory.Upsert([second], first), updated);

        using (Assert.Multiple())
        {
            await Assert.That(rides).Count().IsEqualTo(2);
            await Assert.That(rides[0]).IsEqualTo(first);
            await Assert.That(rides[1].Name).IsEqualTo("Updated");
        }
    }

    [Test]
    public async Task Upsert_KeepsTheMostRecentRides()
    {
        var rides = Enumerable.Range(0, RideHistory.MaximumRides)
            .Select(day => Entry(Start.AddDays(day), 1.5d))
            .ToArray();

        var updated = RideHistory.Upsert(rides, Entry(Start.AddDays(RideHistory.MaximumRides), 1.5d));

        using (Assert.Multiple())
        {
            await Assert.That(updated).Count().IsEqualTo(RideHistory.MaximumRides);
            await Assert.That(updated[0].StartedAt).IsEqualTo(Start.AddDays(1));
        }
    }

    [Test]
    public async Task CreateTrend_ComparesTheLatestEfficiencyWithPreviousComparableRides()
    {
        RideHistoryEntry[] rides =
        [
            Entry(Start, 1.50d),
            Entry(Start.AddDays(1), 1.50d),
            Entry(Start.AddDays(2), 3.00d) with { Elapsed = TimeSpan.FromMinutes(5) },
            Entry(Start.AddDays(3), 1.65d),
        ];

        var trend = RideHistory.CreateTrend(rides, TimeZoneInfo.Utc);
        var level = RideHistory.CreateTrend([Entry(Start, 1.50d), Entry(Start.AddDays(1), 1.52d)], TimeZoneInfo.Utc);
        var empty = RideHistory.CreateTrend([], TimeZoneInfo.Utc);

        using (Assert.Multiple())
        {
            await Assert.That(trend.Summary).StartsWith($"Efficiency factor {1.65d:F2}, up (+10%) versus your previous 2 rides ({1.5d:F2}).");
            await Assert.That(trend.EfficiencyPoints).Count().IsEqualTo(3);
            await Assert.That(trend.HasEfficiencyTrend).IsTrue();
            await Assert.That(trend.Rows).Count().IsEqualTo(4);
            await Assert.That(trend.Rows[0].Date).IsEqualTo("Thu 5 Nov");
            await Assert.That(trend.Rows[0].Duration).IsEqualTo("30:00");
            await Assert.That(trend.Rows[0].EfficiencyFactor).IsEqualTo($"EF {1.65d:F2}");
            await Assert.That(level.Summary).Contains("about level (+1%) versus your previous 1 ride");
            await Assert.That(empty.Summary).StartsWith("Rides are saved automatically");
            await Assert.That(empty.HasEfficiencyTrend).IsFalse();
        }
    }

    [Test]
    public async Task WriteAsync_RoundTripsTheHistory()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "ride-history.json");
            var missing = await RideHistory.ReadAsync(path);
            RideHistoryEntry[] rides = [Entry(Start, 1.5d), Entry(Start.AddDays(1), 1.6d)];

            await RideHistory.WriteAsync(path, new(rides));
            var restored = await RideHistory.ReadAsync(path);

            using (Assert.Multiple())
            {
                await Assert.That(missing.Rides).IsEmpty();
                await Assert.That(restored.Rides).IsEquivalentTo(rides);
                await Assert.That(File.Exists(path + ".tmp")).IsFalse();
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static RideHistoryEntry Entry(DateTimeOffset startedAt, double efficiency) =>
        new(startedAt, TimeSpan.FromMinutes(30), "Endurance", 150d, 155d, 155d / efficiency, efficiency, 1.03d, 3.2d, 25d);
}