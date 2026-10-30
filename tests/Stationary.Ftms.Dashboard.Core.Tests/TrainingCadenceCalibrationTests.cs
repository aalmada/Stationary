using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class TrainingCadenceCalibrationTests
{
    [Test]
    public async Task Calibrate_ReturnsMedianOfPedallingSamples()
    {
        double[] samples = [.. Enumerable.Repeat(52d, 40), .. Enumerable.Repeat(0d, 20), 30d];

        var calibrated = TrainingCadenceCalibration.Calibrate(samples, preferredCadenceRpm: 50);

        await Assert.That(calibrated).IsEqualTo(52);
    }

    [Test]
    public async Task Calibrate_LimitsAdjustmentAroundPreferredCadence()
    {
        var calibrated = TrainingCadenceCalibration.Calibrate(Enumerable.Repeat(46d, 60), preferredCadenceRpm: 90);

        await Assert.That(calibrated).IsEqualTo(80);
    }

    [Test]
    public async Task MeasureMedianRpm_ReportsUnlimitedWarmUpCadence()
    {
        var measured = TrainingCadenceCalibration.MeasureMedianRpm(Enumerable.Repeat(63d, 60));

        using (Assert.Multiple())
        {
            await Assert.That(measured).IsEqualTo(63);
            await Assert.That(TrainingCadenceCalibration.Limit(63, 90)).IsEqualTo(80);
        }
    }

    [Test]
    public async Task Calibrate_ReturnsNullWithTooFewSamples()
    {
        var calibrated = TrainingCadenceCalibration.Calibrate(Enumerable.Repeat(60d, TrainingCadenceCalibration.MinimumSampleCount - 1), 60);

        await Assert.That(calibrated).IsNull();
    }

    [Test]
    public async Task IsCalibrationSegment_MatchesOnlyTheOpeningWarmUp()
    {
        var catalog = await TrainingCatalog.LoadAsync();
        var workout = TrainingWorkoutCompiler.Compile(catalog.Workouts[0], 200, new(0, 500, 1));

        using (Assert.Multiple())
        {
            await Assert.That(TrainingCadenceCalibration.IsCalibrationSegment(workout, 0)).IsTrue();
            await Assert.That(TrainingCadenceCalibration.IsCalibrationSegment(workout, 1)).IsFalse();
        }
    }

    [Test]
    public async Task Runner_ReplaceWorkoutKeepsProgress()
    {
        var catalog = await TrainingCatalog.LoadAsync();
        var workout = TrainingWorkoutCompiler.Compile(catalog.Workouts[0], 200, new(0, 500, 1));
        var runner = new TrainingWorkoutRunner(workout);
        var start = DateTimeOffset.UnixEpoch;
        runner.Start();
        runner.ConfirmTargetApplied(start);
        _ = runner.Advance(start + workout.Segments[0].Definition.Duration);

        runner.ReplaceWorkout(TrainingWorkoutCompiler.RescaleCadence(workout, 50, 1));

        using (Assert.Multiple())
        {
            await Assert.That(runner.SegmentIndex).IsEqualTo(1);
            await Assert.That(runner.CurrentSegment!.Definition.MaximumCadenceRpm!.Value).IsLessThan(workout.Segments[1].Definition.MaximumCadenceRpm!.Value);
        }
    }
}