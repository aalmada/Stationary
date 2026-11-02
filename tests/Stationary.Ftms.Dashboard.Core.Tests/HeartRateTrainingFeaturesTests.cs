using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class HeartRateTrainingFeaturesTests
{
    [Test]
    public async Task HeartRateReserveProfile_UsesModerateAndVigorousBoundaries()
    {
        var profile = HeartRateZoneProfile.CreateHeartRateReserve(190, 60);
        var moderate = HeartRateZoneProfile.GetHeartRateReserveRange(190, 60, 40, 59);

        using (Assert.Multiple())
        {
            await Assert.That(profile.GetZone(111).Code).IsEqualTo("Z1");
            await Assert.That(profile.GetZone(112).Code).IsEqualTo("Z2");
            await Assert.That(profile.GetZone(138).Code).IsEqualTo("Z3");
            await Assert.That(profile.GetZone(177).Code).IsEqualTo("Z5");
            await Assert.That(moderate).IsEqualTo(((ushort)112, (ushort)136));
        }
    }

    [Test]
    public async Task MaximumHeartRateProfile_UsesFiveZonesAndAgeEstimateIsLabeledByCaller()
    {
        var profile = HeartRateZoneProfile.CreateMaximumHeartRate(200);
        var estimate = HeartRateZoneProfile.EstimateMaximumHeartRateFromAge(40);

        using (Assert.Multiple())
        {
            await Assert.That(profile.GetZone(119).Code).IsEqualTo("Z1");
            await Assert.That(profile.GetZone(120).Code).IsEqualTo("Z2");
            await Assert.That(profile.GetZone(180).Code).IsEqualTo("Z5");
            await Assert.That(estimate).IsEqualTo((ushort)180);
        }
    }

    [Test]
    public async Task HeartRateZoneWorkout_CompilesTheZoneAndBoundedPowerRange()
    {
        var profile = HeartRateZoneProfile.CreateCyclingLactateThreshold(170);
        var workout = HeartRateWorkoutFactory.CreateZoneWorkouts(profile)
            .Single(candidate => candidate.Id == "heart-rate-zone-2");

        var compiled = TrainingWorkoutCompiler.Compile(workout, 200, new(50, 500, 10), heartRateZones: profile);
        var target = compiled.Segments.Single(segment => segment.TargetMinimumHeartRate is not null);

        using (Assert.Multiple())
        {
            await Assert.That(target.TargetMinimumHeartRate).IsEqualTo((ushort)138);
            await Assert.That(target.TargetMaximumHeartRate).IsEqualTo((ushort)152);
            await Assert.That(target.MinimumTargetWatts).IsEqualTo(80);
            await Assert.That(target.MaximumTargetWatts).IsEqualTo(160);
            await Assert.That(target.TargetWatts).IsEqualTo(120);
        }
    }

    [Test]
    public async Task ZoneWorkout_RequiresAConfiguredHeartRateProfile()
    {
        var workout = new TrainingWorkout(
            "zone-test",
            "Zone test",
            "Zone test",
            TrainingObjective.HeartRate,
            TrainingExperienceLevel.Beginner,
            [
                new(
                    "Target",
                    TimeSpan.FromMinutes(1),
                    60,
                    null,
                    null,
                    "Hold",
                    true,
                    HeartRateTrainingTarget.ForZone("Z2", 40, 80)),
            ]);

        await Assert.That(() => TrainingWorkoutCompiler.Compile(workout, 200, new(50, 500, 10)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ClinicianPowerCap_IsNeverRoundedAboveThePrescribedMaximum()
    {
        var workout = HeartRateWorkoutFactory.CreateClinicianPrescribedWorkout(100, 130, 103, TimeSpan.FromMinutes(20));
        var compiled = TrainingWorkoutCompiler.Compile(workout, 200, new(50, 500, 10));
        var target = compiled.Segments[0];

        using (Assert.Multiple())
        {
            await Assert.That(target.TargetMaximumHeartRate).IsEqualTo((ushort)130);
            await Assert.That(target.MaximumTargetWatts).IsEqualTo(100);
            await Assert.That(target.TargetWatts).IsLessThanOrEqualTo(103);
        }
    }

    [Test]
    public async Task HeartRatePowerController_AdjustsInBoundedPowerSteps()
    {
        var next = HeartRatePowerController.GetNextTargetWatts(
            120,
            50,
            200,
            10,
            currentHeartRate: 155,
            targetMinimumHeartRate: 120,
            targetMaximumHeartRate: 140);
        var ceiling = HeartRatePowerController.GetNextTargetWatts(
            120,
            50,
            200,
            10,
            currentHeartRate: 170,
            targetMinimumHeartRate: 120,
            targetMaximumHeartRate: 160,
            safetyCeilingHeartRate: 165);

        using (Assert.Multiple())
        {
            await Assert.That(next).IsEqualTo(110);
            await Assert.That(ceiling).IsEqualTo(110);
            await Assert.That(HeartRatePowerController.GetNextTargetWatts(
                50,
                50,
                200,
                10,
                currentHeartRate: 170,
                targetMinimumHeartRate: 120,
                targetMaximumHeartRate: 160,
                safetyCeilingHeartRate: 165)).IsEqualTo(50);
        }
    }

    [Test]
    public async Task HeartRateTargetAdherence_CalculatesFreshTimeInRange()
    {
        var start = DateTimeOffset.UnixEpoch;
        RecordedTelemetrySample[] samples = [
            new(start, 100, null, null, 100),
            new(start.AddSeconds(2), 100, null, null, 130),
            new(start.AddSeconds(5), 100, null, null, 140),
            new(start.AddSeconds(10), 100, null, null, 160),
        ];

        var result = HeartRateTargetAdherenceCalculator.Calculate(
            samples,
            120,
            150,
            start,
            start.AddSeconds(10),
            TimeSpan.FromSeconds(5));

        using (Assert.Multiple())
        {
            await Assert.That(result.InRange).IsEqualTo(TimeSpan.FromSeconds(8));
            await Assert.That(result.Observed).IsEqualTo(TimeSpan.FromSeconds(10));
            await Assert.That(result.Percentage).IsEqualTo(80d);
        }
    }

    [Test]
    public async Task WeeklyEasyVolume_CountsOnlyTimeInFirstTwoHeartRateZones()
    {
        var profile = new HeartRateZoneProfile("Test", [
            new("Z1", "Recovery", 0, 129),
            new("Z2", "Endurance", 130, 149),
            new("Z3", "Tempo", 150, null),
        ]);
        var start = DateTimeOffset.UnixEpoch;
        RecordedTelemetrySample[] samples = [
            new(start, 100, null, null, 120),
            new(start.AddSeconds(2), 100, null, null, 120),
            new(start.AddSeconds(4), 100, null, null, 140),
            new(start.AddSeconds(6), 100, null, null, 160),
        ];
        var duration = HeartRateTrainingProgress.CalculateZoneOneAndTwoDuration(
            samples,
            profile,
            start,
            start.AddSeconds(6),
            TimeSpan.FromSeconds(5));
        HeartRateTrainingRide[] rides = [
            new(DateTimeOffset.UtcNow - TimeSpan.FromDays(8), TimeSpan.FromMinutes(30).Ticks),
            new(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(12).Ticks),
        ];

        using (Assert.Multiple())
        {
            await Assert.That(duration).IsEqualTo(TimeSpan.FromSeconds(6));
            await Assert.That(HeartRateTrainingProgress.GetMinutesWithinLastSevenDays(rides, DateTimeOffset.UtcNow)).IsEqualTo(12);
        }
    }
    [Test]
    public async Task BuiltInProfiles_ExposeEquivalentEasyCeilingsAndAerobicCapacityBands()
    {
        var maximum = HeartRateZoneProfile.CreateMaximumHeartRate(200);
        var reserve = HeartRateZoneProfile.CreateHeartRateReserve(190, 60);
        var threshold = HeartRateZoneProfile.CreateCyclingLactateThreshold(170);
        var custom = new HeartRateZoneProfile("Custom", [new("Z1", "Easy", 0, 120), new("Z2", "Hard", 121, null)]);

        using (Assert.Multiple())
        {
            await Assert.That(maximum.EasyCeilings).IsEqualTo(new HeartRateEasyCeilings(139, 159));
            await Assert.That(maximum.AerobicCapacity).IsEqualTo(new HeartRateAerobicCapacity(170, 190, 180));
            await Assert.That(reserve.EasyCeilings).IsEqualTo(new HeartRateEasyCeilings(137, 150));
            await Assert.That(reserve.AerobicCapacity).IsEqualTo(new HeartRateAerobicCapacity(162, 180, 171));
            await Assert.That(threshold.EasyCeilings).IsEqualTo(new HeartRateEasyCeilings(137, 152));
            await Assert.That(threshold.AerobicCapacity!.Value.IntervalMinimum).IsEqualTo(threshold.GetZone(threshold.AerobicCapacity.Value.IntervalMinimum).MinimumBeatsPerMinute);
            await Assert.That(threshold.AerobicCapacity.Value.NearMaximum).IsEqualTo((ushort)170);
            await Assert.That(custom.EasyCeilings).IsNull();
            await Assert.That(custom.AerobicCapacity).IsNull();
        }
    }

    [Test]
    public async Task CeilingSegment_CompilesPowerWithAFloorAndCapOnlyWhenZonesAreKnown()
    {
        var segment = new TrainingSegment("Aerobic", TimeSpan.FromMinutes(10), 70d, null, null, "Ride easy", true, HeartRateCeiling: new(HeartRateCeilingIntensity.Endurance, 50d));
        var workout = new TrainingWorkout("ceiling", "Ceiling", "Ceiling", TrainingObjective.Endurance, TrainingExperienceLevel.Beginner, [segment]);
        var profile = HeartRateZoneProfile.CreateMaximumHeartRate(200);

        var withZones = TrainingWorkoutCompiler.Compile(workout, 200, new(50, 500, 10), heartRateZones: profile).Segments[0];
        var withoutZones = TrainingWorkoutCompiler.Compile(workout, 200, new(50, 500, 10)).Segments[0];

        using (Assert.Multiple())
        {
            await Assert.That(withZones.TargetWatts).IsEqualTo(140);
            await Assert.That(withZones.MinimumTargetWatts).IsEqualTo(100);
            await Assert.That(withZones.MaximumTargetWatts).IsEqualTo(140);
            await Assert.That(withZones.CeilingHeartRate).IsEqualTo((ushort)159);
            await Assert.That(withZones.TargetMinimumHeartRate).IsNull();
            await Assert.That(withoutZones.CeilingHeartRate).IsNull();
            await Assert.That(withoutZones.MinimumTargetWatts).IsEqualTo(140);
        }
    }

    [Test]
    public async Task CeilingSegment_RejectsAFloorAboveTheTargetOrACombinedTarget()
    {
        var floorAbove = new TrainingSegment("Easy", TimeSpan.FromMinutes(10), 50d, null, null, "Easy", true, HeartRateCeiling: new(HeartRateCeilingIntensity.Recovery, 60d));
        var combined = new TrainingSegment("Easy", TimeSpan.FromMinutes(10), 50d, null, null, "Easy", true, HeartRateTrainingTarget.ForRange(100, 120, 40d, 60d), new(HeartRateCeilingIntensity.Recovery, 40d));

        foreach (var segment in new[] { floorAbove, combined })
        {
            var workout = new TrainingWorkout("invalid", "Invalid", "Invalid", TrainingObjective.Recovery, TrainingExperienceLevel.Beginner, [segment]);
            await Assert.That(() => TrainingWorkoutCompiler.Compile(workout, 200, new(50, 500, 10))).Throws<ArgumentException>();
        }
    }

    [Test]
    public async Task Vo2Workout_ReportsTheNearMaximumThresholdOnlyWithAerobicCapacityZones()
    {
        var workout = TrainingCatalog.Load().Workouts.First(candidate => candidate.Objective == TrainingObjective.Vo2Max);
        var tempo = TrainingCatalog.Load().Workouts.First(candidate => candidate.Objective == TrainingObjective.TempoSweetSpot);
        var profile = HeartRateZoneProfile.CreateMaximumHeartRate(200);

        using (Assert.Multiple())
        {
            await Assert.That(TrainingWorkoutCompiler.Compile(workout, 200, new(50, 500, 10), heartRateZones: profile).NearMaximumHeartRate).IsEqualTo((ushort)180);
            await Assert.That(TrainingWorkoutCompiler.Compile(workout, 200, new(50, 500, 10)).NearMaximumHeartRate).IsNull();
            await Assert.That(TrainingWorkoutCompiler.Compile(tempo, 200, new(50, 500, 10), heartRateZones: profile).NearMaximumHeartRate).IsNull();
        }
    }

    [Test]
    public async Task Catalog_CapsSustainedEasyRidingButLeavesHarderWorkPowerLed()
    {
        var workouts = TrainingCatalog.Load().Workouts;
        var recovery = workouts.Where(workout => workout.Objective == TrainingObjective.Recovery).SelectMany(workout => workout.Segments).ToArray();
        var endurance = workouts.Where(workout => workout.Objective == TrainingObjective.Endurance).SelectMany(workout => workout.Segments).ToArray();
        var harder = workouts.Where(workout => workout.Objective is not (TrainingObjective.Recovery or TrainingObjective.Endurance)).SelectMany(workout => workout.Segments);

        using (Assert.Multiple())
        {
            await Assert.That(recovery.Where(segment => segment.CountsTowardAdherence && segment.Duration >= TimeSpan.FromMinutes(3)).All(segment => segment.HeartRateCeiling?.Intensity == HeartRateCeilingIntensity.Recovery)).IsTrue();
            await Assert.That(endurance.Any(segment => segment.HeartRateCeiling?.Intensity == HeartRateCeilingIntensity.Endurance)).IsTrue();
            await Assert.That(endurance.Where(segment => segment.FunctionalThresholdPowerPercentage > 75d).All(segment => segment.HeartRateCeiling is null)).IsTrue();
            await Assert.That(harder.All(segment => segment.HeartRateCeiling is null)).IsTrue();
        }
    }

    [Test]
    public async Task ZoneWorkouts_AddTheHeartRate4x4WhenTheAerobicCapacityBandIsKnown()
    {
        var profile = HeartRateZoneProfile.CreateMaximumHeartRate(200);
        var custom = new HeartRateZoneProfile("Custom", [new("Z1", "Easy", 0, 120), new("Z2", "Steady", 121, 150), new("Z3", "Hard", 151, null)]);

        var intervals = HeartRateWorkoutFactory.CreateZoneWorkouts(profile).Single(workout => workout.Id == "heart-rate-4x4");
        var compiled = TrainingWorkoutCompiler.Compile(intervals, 200, new(50, 500, 10), heartRateZones: profile);
        var work = compiled.Segments.Where(segment => segment.TargetMinimumHeartRate is not null).ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(HeartRateWorkoutFactory.CreateZoneWorkouts(custom).Any(workout => workout.Id == "heart-rate-4x4")).IsFalse();
            await Assert.That(work).Count().IsEqualTo(4);
            await Assert.That(work.All(segment => segment.Definition.Duration == TimeSpan.FromMinutes(4))).IsTrue();
            await Assert.That(work.All(segment => segment.TargetMinimumHeartRate == 170 && segment.TargetMaximumHeartRate == 190)).IsTrue();
            await Assert.That(compiled.NearMaximumHeartRate).IsEqualTo((ushort)180);
            await Assert.That(intervals.Segments.Sum(segment => segment.Duration.TotalMinutes)).IsEqualTo(40d);
        }
    }
}