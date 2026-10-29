using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class TrainingCadenceGuidanceTests
{
    [Test]
    [Arguments(46d, 85, 95, TrainingCadenceGuidance.PedalFaster)]
    [Arguments(102d, 85, 95, TrainingCadenceGuidance.PedalSlower)]
    [Arguments(85d, 85, 95, TrainingCadenceGuidance.OnTarget)]
    [Arguments(95d, 85, 95, TrainingCadenceGuidance.OnTarget)]
    [Arguments(120d, 100, null, TrainingCadenceGuidance.OnTarget)]
    [Arguments(60d, null, 70, TrainingCadenceGuidance.OnTarget)]
    [Arguments(80d, null, null, "")]
    [Arguments(null, 85, 95, "")]
    public async Task GetDirectionText_ComparesLiveCadenceWithTarget(double? cadence, int? minimum, int? maximum, string expected)
    {
        await Assert.That(TrainingCadenceGuidance.GetDirectionText(cadence, minimum, maximum)).IsEqualTo(expected);
    }
}