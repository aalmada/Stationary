using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class HeartRateZoneGuidanceTests
{
    [Test]
    public async Task Methods_DeclareOnlyTheInputsTheyUse()
    {
        using (Assert.Multiple())
        {
            await Assert.That(HeartRateZoneGuidance.UsesLactateThreshold(HeartRateZoneMethod.CyclingLactateThreshold)).IsTrue();
            await Assert.That(HeartRateZoneGuidance.UsesMaximumHeartRate(HeartRateZoneMethod.CyclingLactateThreshold)).IsFalse();
            await Assert.That(HeartRateZoneGuidance.UsesRestingHeartRate(HeartRateZoneMethod.MaximumHeartRate)).IsFalse();
            await Assert.That(HeartRateZoneGuidance.UsesMaximumHeartRate(HeartRateZoneMethod.HeartRateReserve)).IsTrue();
            await Assert.That(HeartRateZoneGuidance.UsesRestingHeartRate(HeartRateZoneMethod.HeartRateReserve)).IsTrue();
        }
    }

    [Test]
    public async Task Status_SummarizesTheEnduranceZoneAndCeiling()
    {
        var profile = HeartRateZoneProfile.CreateCyclingLactateThreshold(160);

        using (Assert.Multiple())
        {
            await Assert.That(HeartRateZoneGuidance.DescribeStatus(null, null)).StartsWith("Heart-rate zones not set");
            await Assert.That(HeartRateZoneGuidance.DescribeStatus(profile, 170)).Contains("Z2").And.Contains("ceiling 170 bpm");
        }
    }

    [Test]
    public async Task RampPeak_SuggestsMaximumOnlyWhenItIsHigherOrMissing()
    {
        var missing = HeartRateZoneGuidance.ReviewRampPeak(160, null, null);
        var higher = HeartRateZoneGuidance.ReviewRampPeak(190, 185, null);
        var lower = HeartRateZoneGuidance.ReviewRampPeak(160, 185, 160);
        var none = HeartRateZoneGuidance.ReviewRampPeak(null, 185, 160);

        using (Assert.Multiple())
        {
            await Assert.That(missing.SuggestedMaximumHeartRate).IsEqualTo((ushort)160);
            await Assert.That(higher.SuggestedMaximumHeartRate).IsEqualTo((ushort)190);
            await Assert.That(lower.SuggestedMaximumHeartRate).IsNull();
            await Assert.That(lower.Text).Contains("probably too high");
            await Assert.That(none.SuggestedMaximumHeartRate).IsNull();
        }
    }
}