namespace Stationary.Ftms.Dashboard.Core;

// Short diagnostic sessions for checking how the bike follows ERG targets; kept outside the validated training catalog.
public static class TrainingDebugCatalog
{
    public const string PlanId = "debug-power-cadence-plan";

    private const string SpinUpName = "Spin up";
    private const string SpinUpCue = "Find a steady, comfortable cadence; the targets adapt to it.";
    private const string CoolDownName = "Cool down";
    private const string CoolDownCue = "Ease off and export the CSV after the session.";
    private const string HoldCadenceCue = "Keep cadence in the band; watch power follow the target line.";
    private const int BroadMinimumCadenceRpm = 60;
    private const int BroadMaximumCadenceRpm = 100;
    private const int WorkMinimumCadenceRpm = 85;
    private const int WorkMaximumCadenceRpm = 95;

    public static IReadOnlyList<TrainingWorkout> Workouts { get; } =
    [
        Workout("debug-power-steps", "Debug: Power Steps", "Cadence band stays fixed while power steps up and down every 45 s. Check that the bike follows each target.",
            Segment("Step 1", 45, 50, HoldCadenceCue),
            Segment("Step 2", 45, 65, HoldCadenceCue),
            Segment("Step 3", 45, 80, HoldCadenceCue),
            Segment("Step 4", 45, 55, HoldCadenceCue),
            Segment("Step 5", 45, 90, HoldCadenceCue),
            Segment("Step 6", 45, 70, HoldCadenceCue),
            Segment("Step 7", 45, 50, HoldCadenceCue)),
        Workout("debug-cadence-sweep", "Debug: Cadence Sweep", "Power stays fixed while the cadence band moves every minute. In ERG mode power should stay flat as cadence changes.",
            Segment("Middle band", 60, 60, "Settle into the middle band.", 85, 95),
            Segment("Lower band", 60, 60, "Slow down into the lower band; power should not drop.", 75, 85),
            Segment("Upper band", 60, 60, "Speed up into the upper band; power should not rise.", 95, 105),
            Segment("Low band", 60, 60, "Slow down further; resistance should increase.", 70, 80),
            Segment("High band", 60, 60, "Speed up again; resistance should ease.", 90, 100),
            Segment("Middle band again", 60, 60, "Return to the middle band.", 85, 95)),
        Workout("debug-big-jumps", "Debug: Big Jumps", "Power alternates between 45% and 85% FTP every 30 s. Check ramping and how quickly the bike responds.",
            Segment("Low 1", 30, 45, HoldCadenceCue),
            Segment("High 1", 30, 85, HoldCadenceCue),
            Segment("Low 2", 30, 45, HoldCadenceCue),
            Segment("High 2", 30, 85, HoldCadenceCue),
            Segment("Low 3", 30, 45, HoldCadenceCue),
            Segment("High 3", 30, 85, HoldCadenceCue),
            Segment("Low 4", 30, 45, HoldCadenceCue),
            Segment("High 4", 30, 85, HoldCadenceCue)),
        Workout("debug-stall-recover", "Debug: Stall and Recover", "Steady power with two deliberate stops to check the stall guard drops to minimum power and ramps back once you pedal.",
            Segment("Hold 1", 60, 65, HoldCadenceCue),
            Segment("Stop 1", 45, 65, "Stop pedalling now and wait until the panel says resistance eased, then spin back above 45 rpm.", null, null, false),
            Segment("Hold 2", 60, 65, "Back in the band; power should ramp back to the target."),
            Segment("Stop 2", 45, 65, "Stop pedalling again now and wait until the panel says resistance eased, then spin back above 45 rpm.", null, null, false),
            Segment("Hold 3", 60, 65, "Back in the band; power should ramp back to the target.")),
    ];

    public static TrainingPlan Plan { get; } = new(
        PlanId,
        "Debug: Power vs Cadence",
        "Short sessions for checking how the bike, the ERG target, and the power vs cadence map respond. Not a training plan.",
        TrainingObjective.Diagnostics,
        TrainingExperienceLevel.Beginner,
        [
            new(1, TrainingPlanPhase.Diagnostics, 0, 0, 0, [.. Workouts.Select(static workout => workout.Id)],
                "Ride the sessions in order and export the CSV after each one."),
        ]);

    private static TrainingWorkout Workout(string id, string name, string description, params TrainingSegment[] work) => new(
        id,
        name,
        description,
        TrainingObjective.Diagnostics,
        TrainingExperienceLevel.Beginner,
        [
            new(SpinUpName, TimeSpan.FromSeconds(90), 45, BroadMinimumCadenceRpm, BroadMaximumCadenceRpm, SpinUpCue, false),
            .. work,
            new(CoolDownName, TimeSpan.FromSeconds(60), 40, null, null, CoolDownCue, false),
        ]);

    private static TrainingSegment Segment(
        string name,
        int seconds,
        double target,
        string cue,
        int? minimumCadenceRpm = WorkMinimumCadenceRpm,
        int? maximumCadenceRpm = WorkMaximumCadenceRpm,
        bool countsTowardAdherence = true) =>
        new(name, TimeSpan.FromSeconds(seconds), target, minimumCadenceRpm, maximumCadenceRpm, cue, countsTowardAdherence);
}