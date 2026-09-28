using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class TrainingCatalogTests
{
    [Test]
    public async Task Load_ReturnsEveryObjectiveAndExperienceLevel()
    {
        var catalog = TrainingCatalog.Load();

        using (Assert.Multiple())
        {
            await Assert.That(catalog.Workouts.Count).IsEqualTo(35);
            await Assert.That(catalog.Workouts.Select(static workout => workout.Id).Distinct().Count()).IsEqualTo(35);
        }
    }
}