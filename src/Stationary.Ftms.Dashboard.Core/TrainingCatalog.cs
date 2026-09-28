using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stationary.Ftms.Dashboard.Core;

public sealed record TrainingCatalogDocument(IReadOnlyList<TrainingWorkout> Workouts);

public static class TrainingCatalog
{
    private const string ResourceName = "Stationary.Ftms.Dashboard.Core.TrainingCatalog.json";

    public static TrainingCatalogDocument Load()
    {
        using var stream = typeof(TrainingCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The built-in training catalog could not be found.");
        var catalog = JsonSerializer.Deserialize(stream, TrainingCatalogJsonContext.Default.TrainingCatalogDocument)
            ?? throw new InvalidOperationException("The built-in training catalog is empty.");
        Validate(catalog);
        return catalog;
    }

    public static void Validate(TrainingCatalogDocument catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.Workouts.Count != 35)
        {
            throw new InvalidOperationException("The training catalog must include one workout for each objective and experience level.");
        }

        var expected = Enum.GetValues<TrainingObjective>()
            .SelectMany(static objective => Enum.GetValues<TrainingExperienceLevel>().Select(level => (objective, level)))
            .ToHashSet();
        var actual = new HashSet<(TrainingObjective, TrainingExperienceLevel)>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var workout in catalog.Workouts)
        {
            if (string.IsNullOrWhiteSpace(workout.Id) || !ids.Add(workout.Id))
            {
                throw new InvalidOperationException("Training workout identifiers must be unique.");
            }

            if (!actual.Add((workout.Objective, workout.ExperienceLevel)))
            {
                throw new InvalidOperationException("The training catalog has duplicate objective and experience-level variants.");
            }

            _ = TrainingWorkoutCompiler.Compile(workout, 200, new(0, 500, 1));
        }

        if (!expected.SetEquals(actual))
        {
            throw new InvalidOperationException("The training catalog is missing an objective and experience-level variant.");
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(TrainingCatalogDocument))]
internal sealed partial class TrainingCatalogJsonContext : JsonSerializerContext;