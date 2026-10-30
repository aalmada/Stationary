namespace Stationary.Ftms.Dashboard.Core;

internal static class TrainingWorkoutLibrary
{
    private static readonly TimeSpan CadenceLiftDuration = TimeSpan.FromMinutes(2);
    private const string EaseInName = "Ease in";
    private const string EasyEnduranceName = "Easy endurance";
    private const string EasyFloatName = "Easy float";
    private const string SpinDownName = "Spin down";
    private const string QuickFeetName = "Quick feet";
    private const string WarmUpName = "Warm up";
    private const string CoolDownName = "Cool down";
    private const string FloatName = "Float";
    private const string EnduranceName = "Endurance";
    private const string TempoCloseName = "Tempo close";
    private const string SweetSpotName = "Sweet spot";
    private const string TempoSurgeName = "Tempo surge";
    private const string UnderName = "Under";
    private const string OverName = "Over";
    private const string Vo2Name = "VO2";
    private const string Vo2EffortName = "VO2 effort";
    private const string SetRecoveryName = "Set recovery";
    private const string EasyRollName = "Easy roll";
    private const string SpinUpName = "Spin-up";
    private const string TorqueName = "Torque";
    private const string CadenceCloseName = "Cadence close";
    private const string SeatedClimbName = "Seated climb";
    private const string HighCadenceName = "High cadence";
    private const string LadderStepName = "Ladder step";
    private const string PyramidStepName = "Pyramid step";
    private const string BurstName = "Burst";
    private const string CadencePreparationName = "Cadence build";
    private const string CadenceLiftName = "Cadence lift";
    private const string CadenceLiftCue = "Raise cadence gradually toward the work range.";
    private const int BroadWarmUpMinimumCadenceRpm = 60;
    private const int BroadWarmUpMaximumCadenceRpm = 100;
    private const int CadenceLiftMinimumCadenceRpm = 75;
    private const int CadenceLiftMaximumCadenceRpm = 95;
    private const string StartWithLightSpin = "Start with a light spin.";
    private const string FinishFresher = "Finish fresher than you started.";
    private const string BuildSmoothly = "Build smoothly.";
    private const string LightenGear = "Ease the effort and keep the cadence relaxed.";
    private const string LetBreathingSettle = "Let your breathing settle.";
    private const string FinishControlled = "Finish controlled.";
    private const string EaseOffGradually = "Ease off gradually.";
    private const string FinishWithReserve = "Finish with reserve.";
    private const string RecoverBeforeRepeating = "Recover fully before repeating.";
    private const string RecoverBeforeSecondSet = "Recover fully before the second set.";
    private const string SettleBelowThreshold = "Settle below threshold.";
    private const string LiftPowerWithoutRushingCadence = "Lift power without rushing cadence.";
    private const string ReturnToSteadyControl = "Return to steady control.";
    private const string PushFirmly = "Push firmly, stay smooth.";
    private const string KeepPedalingLightly = "Keep pedaling lightly.";
    private const string RepeatSamePressure = "Repeat the same pressure.";
    private const string FinishFirstSetControlled = "Finish the first set controlled.";
    private const string KeepPedalsMoving = "Keep the pedals moving.";
    private const string ReturnToRelaxedRhythm = "Return to a relaxed rhythm.";
    private const string PressSmoothly = "Press smoothly through the full stroke.";
    private const string RepeatSameAcceleration = "Repeat the same acceleration.";
    private const string AccelerateSharply = "Accelerate sharply.";
    private const string KeepMovingNoCoast = "Keep moving, do not coast.";
    private const string RecoverCompletelyBeforeSecondSet = "Recover completely before the second set.";
    private const string FinishSharp = "Finish sharp, not all-out.";
    private const string BuildCadenceBeforeBurst = "Build smoothly into the effort cadence before the target changes.";
    private const string SlowerCadenceSamePower = "Same power, slower legs: let cadence settle lower and press harder as the trainer adds resistance.";
    private const string SpinUpSamePower = "Same power, faster legs: let cadence rise and the trainer will lighten resistance.";
    private const string ReturnToCadenceSamePower = "Same power: return to your normal cadence and smooth pressure.";
    private const string HoldRepeatableEffort = "Hold a hard, repeatable effort; this is not an all-out sprint.";

    public static IReadOnlyList<TrainingWorkout> Create() =>
    [
        Recovery(TrainingExperienceLevel.Beginner),
        Recovery(TrainingExperienceLevel.Developing),
        Recovery(TrainingExperienceLevel.Intermediate),
        Recovery(TrainingExperienceLevel.Experienced),
        Recovery(TrainingExperienceLevel.Advanced),
        Endurance(TrainingExperienceLevel.Beginner),
        Endurance(TrainingExperienceLevel.Developing),
        Endurance(TrainingExperienceLevel.Intermediate),
        Endurance(TrainingExperienceLevel.Experienced),
        Endurance(TrainingExperienceLevel.Advanced),
        TempoSweetSpot(TrainingExperienceLevel.Beginner),
        TempoSweetSpot(TrainingExperienceLevel.Developing),
        TempoSweetSpot(TrainingExperienceLevel.Intermediate),
        TempoSweetSpot(TrainingExperienceLevel.Experienced),
        TempoSweetSpot(TrainingExperienceLevel.Advanced),
        Threshold(TrainingExperienceLevel.Beginner),
        Threshold(TrainingExperienceLevel.Developing),
        Threshold(TrainingExperienceLevel.Intermediate),
        Threshold(TrainingExperienceLevel.Experienced),
        Threshold(TrainingExperienceLevel.Advanced),
        Vo2Max(TrainingExperienceLevel.Beginner),
        Vo2Max(TrainingExperienceLevel.Developing),
        Vo2Max(TrainingExperienceLevel.Intermediate),
        Vo2Max(TrainingExperienceLevel.Experienced),
        Vo2Max(TrainingExperienceLevel.Advanced),
        CadenceSkills(TrainingExperienceLevel.Beginner),
        CadenceSkills(TrainingExperienceLevel.Developing),
        CadenceSkills(TrainingExperienceLevel.Intermediate),
        CadenceSkills(TrainingExperienceLevel.Experienced),
        CadenceSkills(TrainingExperienceLevel.Advanced),
        Anaerobic(TrainingExperienceLevel.Beginner),
        Anaerobic(TrainingExperienceLevel.Developing),
        Anaerobic(TrainingExperienceLevel.Intermediate),
        Anaerobic(TrainingExperienceLevel.Experienced),
        Anaerobic(TrainingExperienceLevel.Advanced),
    ];

    private static TrainingWorkout Recovery(TrainingExperienceLevel level) => level switch
    {
        TrainingExperienceLevel.Beginner => Workout("recovery-beginner", "Reset and Roll", "Short aerobic recovery with light spin-ups at the same easy power.", TrainingObjective.Recovery, level,
            M(EaseInName, 5, 45, StartWithLightSpin, false),
            M("Smooth roll 1", 5, 50, "Relax your shoulders and pedal circles.", true, 85, 95),
            M(Numbered(QuickFeetName, 1), 1, 50, SpinUpSamePower, true, 95, 105),
            M("Smooth roll 2", 4, 50, ReturnToCadenceSamePower, true, 85, 95),
            M(EasyFloatName, 2, 42, "Let the effort fall away.", false),
            M("Smooth roll 3", 5, 52, "Keep the pedals light.", true, 85, 95),
            M(SpinDownName, 4, 45, FinishFresher, false)),
        TrainingExperienceLevel.Developing => Workout("recovery-developing", "Reset Flow", "Controlled recovery volume with spin-ups that change cadence, not power.", TrainingObjective.Recovery, level,
            M(EaseInName, 5, 45, StartWithLightSpin, false),
            M(Numbered(EasyEnduranceName, 1), 6, 52, "Stay conversational.", true, 85, 95),
            M(Numbered(SpinUpName, 1), 1, 52, SpinUpSamePower, true, 95, 105),
            M(Numbered(EasyEnduranceName, 2), 4, 52, ReturnToCadenceSamePower, true, 85, 95),
            M(EasyFloatName, 2, 42, "Return to an effortless rhythm.", false),
            M(Numbered(EasyEnduranceName, 3), 6, 54, "Keep breathing calm.", true, 85, 95),
            M(Numbered(SpinUpName, 2), 1, 54, SpinUpSamePower, true, 98, 108),
            M(Numbered(EasyEnduranceName, 4), 2, 54, ReturnToCadenceSamePower, true, 85, 95),
            M(SpinDownName, 5, 45, FinishFresher, false)),
        TrainingExperienceLevel.Intermediate => Workout("recovery-intermediate", "Calm Cadence", "Easy aerobic work with quick-feet refreshers at the same low power.", TrainingObjective.Recovery, level,
            M(EaseInName, 6, 45, StartWithLightSpin, false),
            M(Numbered(EasyEnduranceName, 1), 7, 54, "Relax through the full pedal stroke.", true, 85, 95),
            M(Numbered(QuickFeetName, 1), 1, 54, SpinUpSamePower, true, 98, 108),
            M(Numbered(EasyEnduranceName, 2), 4, 54, ReturnToCadenceSamePower, true, 85, 95),
            M(EasyFloatName, 2, 45, "Let your heart rate settle.", false),
            M(Numbered(EasyEnduranceName, 3), 7, 55, "Keep the pressure deliberately low.", true, 85, 95),
            M(Numbered(QuickFeetName, 2), 1, 55, SpinUpSamePower, true, 98, 108),
            M(Numbered(EasyEnduranceName, 4), 3, 55, ReturnToCadenceSamePower, true, 85, 95),
            M(SpinDownName, 5, 45, FinishFresher, false)),
        TrainingExperienceLevel.Experienced => Workout("recovery-experienced", "Restoration Ride", "Longer recovery ride that rehearses fast, light pedaling without adding power.", TrainingObjective.Recovery, level,
            M(EaseInName, 6, 45, StartWithLightSpin, false),
            M(Numbered(EasyEnduranceName, 1), 8, 55, "Stay conversational.", true, 85, 95),
            M(Numbered(QuickFeetName, 1), 2, 55, SpinUpSamePower, true, 100, 110),
            M(Numbered(EasyEnduranceName, 2), 5, 55, ReturnToCadenceSamePower, true, 85, 95),
            M(EasyFloatName, 3, 45, "Let the effort fall away.", false),
            M(Numbered(EasyEnduranceName, 3), 8, 56, "Keep effort easy and cadence relaxed.", true, 85, 95),
            M(Numbered(QuickFeetName, 2), 2, 56, SpinUpSamePower, true, 100, 110),
            M(Numbered(EasyEnduranceName, 4), 5, 56, ReturnToCadenceSamePower, true, 85, 95),
            M(SpinDownName, 6, 45, FinishFresher, false)),
        TrainingExperienceLevel.Advanced => Workout("recovery-advanced", "Long Reset", "Low-stress recovery volume with long quick-feet blocks at constant power.", TrainingObjective.Recovery, level,
            M(EaseInName, 8, 45, StartWithLightSpin, false),
            M(Numbered(EasyEnduranceName, 1), 10, 56, "Keep the effort light and rhythmic.", true, 85, 95),
            M(Numbered(QuickFeetName, 1), 2, 56, SpinUpSamePower, true, 100, 112),
            M(Numbered(EasyEnduranceName, 2), 6, 56, ReturnToCadenceSamePower, true, 85, 95),
            M(EasyFloatName, 3, 45, "Let the effort fall away.", false),
            M(Numbered(EasyEnduranceName, 3), 10, 58, "Stay conversational.", true, 85, 95),
            M(Numbered(QuickFeetName, 2), 2, 58, SpinUpSamePower, true, 100, 112),
            M(Numbered(EasyEnduranceName, 4), 6, 58, ReturnToCadenceSamePower, true, 85, 95),
            M(SpinDownName, 7, 45, FinishFresher, false)),
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    private static TrainingWorkout Endurance(TrainingExperienceLevel level) => level switch
    {
        TrainingExperienceLevel.Beginner => Workout("endurance-beginner", "Aerobic Ladder", "Progressive aerobic blocks that practise climbing and spinning cadences at the same power.", TrainingObjective.Endurance, level,
            M(WarmUpName, 6, 50, BuildSmoothly, false),
            M(Numbered(EnduranceName, 1), 6, 62, "Settle into an easy rhythm.", true, 85, 95),
            M(FloatName, 2, 50, LightenGear, false),
            M(Numbered(EnduranceName, 2), 5, 65, "Breathe steadily.", true, 85, 95),
            M(Numbered(SeatedClimbName, 1), 3, 65, SlowerCadenceSamePower, true, 75, 85),
            M(FloatName, 2, 50, LightenGear, false),
            M(Numbered(EnduranceName, 3), 4, 68, "Hold the rhythm.", true, 85, 95),
            M(Numbered(HighCadenceName, 1), 2, 68, SpinUpSamePower, true, 95, 105),
            M(CoolDownName, 5, 45, EaseOffGradually, false)),
        TrainingExperienceLevel.Developing => Workout("endurance-developing", "Aerobic Cruise", "Aerobic blocks with seated-climb and high-cadence sections at constant power.", TrainingObjective.Endurance, level,
            M(WarmUpName, 7, 50, BuildSmoothly, false),
            M(Numbered(EnduranceName, 1), 8, 65, "Stay relaxed at a natural cadence.", true, 85, 95),
            M(Numbered(SeatedClimbName, 1), 3, 65, SlowerCadenceSamePower, true, 72, 82),
            M(FloatName, 2, 52, LetBreathingSettle, false),
            M(Numbered(EnduranceName, 2), 8, 68, "Keep pressure smooth.", true, 85, 95),
            M(Numbered(HighCadenceName, 1), 3, 68, SpinUpSamePower, true, 95, 105),
            M(FloatName, 2, 52, LightenGear, false),
            M(Numbered(EnduranceName, 3), 6, 70, "Finish with quiet shoulders.", true, 85, 95),
            M(Numbered(SeatedClimbName, 2), 2, 70, SlowerCadenceSamePower, true, 72, 82),
            M(CoolDownName, 6, 45, EaseOffGradually, false)),
        TrainingExperienceLevel.Intermediate => Workout("endurance-intermediate", "Endurance Steps", "Aerobic durability with torque and spin sections and a controlled tempo finish.", TrainingObjective.Endurance, level,
            M(WarmUpName, 8, 50, BuildSmoothly, false),
            M(Numbered(EnduranceName, 1), 8, 68, "Stay patient and economical.", true, 85, 95),
            M(Numbered(SeatedClimbName, 1), 4, 68, SlowerCadenceSamePower, true, 70, 80),
            M(FloatName, 2, 52, "Let the effort reset.", false),
            M(Numbered(EnduranceName, 2), 8, 72, "Hold a firm but sustainable rhythm.", true, 85, 95),
            M(Numbered(HighCadenceName, 1), 3, 72, SpinUpSamePower, true, 95, 105),
            M(Numbered(EnduranceName, 3), 3, 72, ReturnToCadenceSamePower, true, 85, 95),
            M(TempoCloseName, 5, 78, "Lift power and keep the same cadence.", true, 85, 95),
            M(FloatName, 3, 52, "Return to easy pedaling.", false),
            M(Numbered(EnduranceName, 4), 6, 70, FinishControlled, true, 85, 95),
            M(CoolDownName, 7, 45, EaseOffGradually, false)),
        TrainingExperienceLevel.Experienced => Workout("endurance-experienced", "Durability Blocks", "Long aerobic blocks with low-cadence torque, high-cadence economy, and a tempo finish.", TrainingObjective.Endurance, level,
            M(WarmUpName, 10, 50, BuildSmoothly, false),
            M(Numbered(EnduranceName, 1), 12, 70, "Remain patient and economical.", true, 85, 95),
            M(Numbered(SeatedClimbName, 1), 5, 70, SlowerCadenceSamePower, true, 65, 75),
            M(FloatName, 3, 52, LightenGear, false),
            M(Numbered(EnduranceName, 2), 12, 74, "Keep the pressure even.", true, 85, 95),
            M(Numbered(HighCadenceName, 1), 4, 74, SpinUpSamePower, true, 95, 105),
            M(Numbered(EnduranceName, 3), 3, 74, ReturnToCadenceSamePower, true, 85, 95),
            M(TempoCloseName, 6, 82, "Build power only; keep the same cadence.", true, 85, 95),
            M(FloatName, 3, 52, "Let the effort reset.", false),
            M(Numbered(EnduranceName, 4), 8, 72, FinishControlled, true, 85, 95),
            M(Numbered(SeatedClimbName, 2), 3, 72, SlowerCadenceSamePower, true, 65, 75),
            M(CoolDownName, 8, 45, EaseOffGradually, false)),
        TrainingExperienceLevel.Advanced => Workout("endurance-advanced", "Aerobic Progression", "Extended endurance with long torque and spin sections that finishes just below tempo.", TrainingObjective.Endurance, level,
            M(WarmUpName, 12, 50, BuildSmoothly, false),
            M(Numbered(EnduranceName, 1), 14, 70, "Stay patient and economical.", true, 85, 95),
            M(Numbered(SeatedClimbName, 1), 6, 70, SlowerCadenceSamePower, true, 65, 75),
            M(FloatName, 3, 52, LightenGear, false),
            M(Numbered(EnduranceName, 2), 15, 75, "Hold a firm aerobic rhythm.", true, 85, 95),
            M(Numbered(HighCadenceName, 1), 5, 75, SpinUpSamePower, true, 95, 105),
            M(Numbered(EnduranceName, 3), 3, 75, ReturnToCadenceSamePower, true, 85, 95),
            M(TempoCloseName, 8, 84, "Progress power only if form remains quiet.", true, 85, 95),
            M(FloatName, 3, 52, "Let the effort reset.", false),
            M(Numbered(EnduranceName, 4), 10, 73, FinishControlled, true, 85, 95),
            M(Numbered(SeatedClimbName, 2), 4, 73, SlowerCadenceSamePower, true, 65, 75),
            M(CoolDownName, 8, 45, EaseOffGradually, false)),
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    private static TrainingWorkout TempoSweetSpot(TrainingExperienceLevel level) => level switch
    {
        TrainingExperienceLevel.Beginner => Workout("tempo-beginner", "Sweet Spot Steps", "Introductory tempo intervals with a seated-climb section and full recovery.", TrainingObjective.TempoSweetSpot, level,
            M(WarmUpName, 8, 52, BuildSmoothly, false),
            M("Tempo 1", 6, 82, "Controlled pressure, no strain.", true, 85, 95),
            M(FloatName, 3, 52, "Recover fully before the next block.", false),
            M("Tempo 2", 4, 85, "Stay tall and relaxed.", true, 85, 95),
            M(Numbered(SeatedClimbName, 1), 3, 85, SlowerCadenceSamePower, true, 72, 82),
            M(FloatName, 3, 52, LetBreathingSettle, false),
            M("Tempo 3", 5, 88, FinishControlled, true, 85, 95),
            M(CoolDownName, 6, 45, EaseOffGradually, false)),
        TrainingExperienceLevel.Developing => Workout("tempo-developing", "Sweet Spot Build", "Progressive sweet spot blocks with a low-cadence torque section.", TrainingObjective.TempoSweetSpot, level,
            M(WarmUpName, 9, 52, BuildSmoothly, false),
            M(Numbered(SweetSpotName, 1), 8, 86, "Find a sustainable rhythm.", true, 85, 95),
            M(FloatName, 3, 52, "Recover fully before the next block.", false),
            M(Numbered(SweetSpotName, 2), 5, 89, "Keep pressure smooth.", true, 85, 95),
            M(Numbered(TorqueName, 1), 3, 89, SlowerCadenceSamePower, true, 70, 80),
            M(FloatName, 3, 52, LetBreathingSettle, false),
            M(Numbered(SweetSpotName, 3), 7, 92, FinishWithReserve, true, 85, 95),
            M(CoolDownName, 7, 45, EaseOffGradually, false)),
        TrainingExperienceLevel.Intermediate => Workout("tempo-intermediate", "Sweet Spot Overlaps", "Sweet spot blocks with low-cadence torque and short power surges.", TrainingObjective.TempoSweetSpot, level,
            M(WarmUpName, 10, 52, BuildSmoothly, false),
            M(Numbered(SweetSpotName, 1), 8, 88, "Settle into firm pressure.", true, 85, 95),
            M(TempoSurgeName, 2, 95, "Raise power and keep the same cadence.", true, 85, 95),
            M(FloatName, 4, 52, "Recover fully before the next set.", false),
            M(Numbered(SweetSpotName, 2), 6, 90, "Keep the effort controlled.", true, 85, 95),
            M(Numbered(TorqueName, 1), 4, 90, SlowerCadenceSamePower, true, 65, 75),
            M(Numbered(SweetSpotName, 3), 3, 90, ReturnToCadenceSamePower, true, 85, 95),
            M(TempoSurgeName, 2, 97, "Raise power and keep the same cadence.", true, 85, 95),
            M(FloatName, 4, 52, LetBreathingSettle, false),
            M(Numbered(SweetSpotName, 4), 8, 92, FinishWithReserve, true, 85, 95),
            M(CoolDownName, 7, 45, EaseOffGradually, false)),
        TrainingExperienceLevel.Experienced => Workout("tempo-experienced", "Sweet Spot Waves", "Long sweet spot sets with low-cadence torque blocks at the same power.", TrainingObjective.TempoSweetSpot, level,
            M(WarmUpName, 12, 52, BuildSmoothly, false),
            M(Numbered(SweetSpotName, 1), 12, 88, "Find a sustainable rhythm.", true, 85, 95),
            M(Numbered(TorqueName, 1), 4, 88, SlowerCadenceSamePower, true, 65, 75),
            M(Numbered(SweetSpotName, 2), 10, 88, ReturnToCadenceSamePower, true, 85, 95),
            M(TempoSurgeName, 3, 96, "Lift power, keep cadence calm.", true, 85, 95),
            M(FloatName, 4, 52, "Recover fully before the next set.", false),
            M(Numbered(SweetSpotName, 3), 12, 90, "Stay focused and quiet.", true, 85, 95),
            M(Numbered(TorqueName, 2), 4, 90, SlowerCadenceSamePower, true, 65, 75),
            M(Numbered(SweetSpotName, 4), 10, 90, ReturnToCadenceSamePower, true, 85, 95),
            M(CoolDownName, 8, 45, EaseOffGradually, false)),
        TrainingExperienceLevel.Advanced => Workout("tempo-advanced", "Sweet Spot Cascade", "Extended sweet spot work with long torque blocks and a controlled surge.", TrainingObjective.TempoSweetSpot, level,
            M(WarmUpName, 12, 52, BuildSmoothly, false),
            M(Numbered(SweetSpotName, 1), 15, 90, "Find a sustainable rhythm.", true, 85, 95),
            M(Numbered(TorqueName, 1), 5, 90, SlowerCadenceSamePower, true, 65, 75),
            M(Numbered(SweetSpotName, 2), 12, 90, ReturnToCadenceSamePower, true, 85, 95),
            M(TempoSurgeName, 3, 100, "Lift power without chasing cadence.", true, 85, 95),
            M(FloatName, 4, 52, "Recover fully before the next set.", false),
            M(Numbered(SweetSpotName, 3), 15, 92, "Stay focused and quiet.", true, 85, 95),
            M(Numbered(TorqueName, 2), 5, 92, SlowerCadenceSamePower, true, 65, 75),
            M(Numbered(SweetSpotName, 4), 12, 92, FinishWithReserve, true, 85, 95),
            M(CoolDownName, 8, 45, EaseOffGradually, false)),
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    private static TrainingWorkout Threshold(TrainingExperienceLevel level) => level switch
    {
        TrainingExperienceLevel.Beginner => Workout("threshold-beginner", "Threshold Primer", "Short threshold work with a seated-climb section and generous recovery.", TrainingObjective.Threshold, level,
            M(WarmUpName, 10, 55, BuildSmoothly, false),
            M("Threshold 1", 5, 95, "Hold a strong, controlled rhythm.", true, 85, 95),
            M(FloatName, 4, 52, RecoverBeforeRepeating, false),
            M("Threshold 2", 3, 95, "Hold form, not tension.", true, 85, 95),
            M(Numbered(SeatedClimbName, 1), 2, 95, SlowerCadenceSamePower, true, 75, 85),
            M(FloatName, 4, 52, RecoverBeforeRepeating, false),
            M("Threshold 3", 4, 98, FinishControlled, true, 85, 95),
            M(CoolDownName, 7, 45, EaseOffGradually, false)),
        TrainingExperienceLevel.Developing => Workout("threshold-developing", "Threshold Sets", "Classic threshold sets with a seated-climb section at the same power.", TrainingObjective.Threshold, level,
            M(WarmUpName, 10, 55, BuildSmoothly, false),
            M("Threshold 1", 7, 97, "Settle into a repeatable effort.", true, 85, 95),
            M(FloatName, 4, 52, RecoverBeforeRepeating, false),
            M("Threshold 2", 4, 100, "Keep power stable.", true, 85, 95),
            M(Numbered(SeatedClimbName, 1), 3, 100, SlowerCadenceSamePower, true, 75, 85),
            M(FloatName, 4, 52, RecoverBeforeRepeating, false),
            M("Threshold 3", 6, 102, FinishControlled, true, 85, 95),
            M(CoolDownName, 7, 45, EaseOffGradually, false)),
        TrainingExperienceLevel.Intermediate => Workout("threshold-intermediate", "Threshold Over-Unders", "Threshold over-unders at a steady cadence, each set closing with a seated climb.", TrainingObjective.Threshold, level,
            M(WarmUpName, 12, 55, BuildSmoothly, false),
            M(Numbered(UnderName, 1), 4, 94, "Settle just below threshold.", true, 85, 95),
            M(Numbered(OverName, 1), 2, 104, LiftPowerWithoutRushingCadence, true, 85, 95),
            M(Numbered(UnderName, 2), 4, 94, "Keep breathing deliberate.", true, 85, 95),
            M(Numbered(SeatedClimbName, 1), 2, 94, SlowerCadenceSamePower, true, 75, 85),
            M(FloatName, 5, 52, RecoverBeforeSecondSet, false),
            M(Numbered(UnderName, 3), 4, 96, "Settle just below threshold.", true, 85, 95),
            M(Numbered(OverName, 2), 2, 106, LiftPowerWithoutRushingCadence, true, 85, 95),
            M(Numbered(UnderName, 4), 4, 96, "Keep breathing deliberate.", true, 85, 95),
            M(Numbered(SeatedClimbName, 2), 2, 96, SlowerCadenceSamePower, true, 75, 85),
            M(CoolDownName, 8, 45, EaseOffGradually, false)),
        TrainingExperienceLevel.Experienced => Workout("threshold-experienced", "Threshold Control", "Long over-unders at a steady cadence with seated climbs at threshold power.", TrainingObjective.Threshold, level,
            M(WarmUpName, 12, 55, BuildSmoothly, false),
            M(Numbered(UnderName, 1), 8, 96, SettleBelowThreshold, true, 85, 95),
            M(Numbered(OverName, 1), 2, 106, LiftPowerWithoutRushingCadence, true, 85, 95),
            M(Numbered(UnderName, 2), 8, 96, ReturnToSteadyControl, true, 85, 95),
            M(Numbered(SeatedClimbName, 1), 3, 96, SlowerCadenceSamePower, true, 75, 85),
            M(FloatName, 5, 52, RecoverBeforeSecondSet, false),
            M(Numbered(UnderName, 3), 8, 98, SettleBelowThreshold, true, 85, 95),
            M(Numbered(OverName, 2), 2, 108, "Lift power without sprinting.", true, 85, 95),
            M(Numbered(UnderName, 4), 8, 98, ReturnToSteadyControl, true, 85, 95),
            M(Numbered(SeatedClimbName, 2), 3, 98, SlowerCadenceSamePower, true, 75, 85),
            M(CoolDownName, 8, 45, EaseOffGradually, false)),
        TrainingExperienceLevel.Advanced => Workout("threshold-advanced", "Threshold Cascade", "Extended over-unders for sustained race pressure, with seated climbs at threshold power.", TrainingObjective.Threshold, level,
            M(WarmUpName, 14, 55, BuildSmoothly, false),
            M(Numbered(UnderName, 1), 10, 98, SettleBelowThreshold, true, 85, 95),
            M(Numbered(OverName, 1), 2, 108, LiftPowerWithoutRushingCadence, true, 85, 95),
            M(Numbered(UnderName, 2), 10, 98, ReturnToSteadyControl, true, 85, 95),
            M(Numbered(SeatedClimbName, 1), 3, 98, SlowerCadenceSamePower, true, 75, 85),
            M(FloatName, 5, 52, RecoverBeforeSecondSet, false),
            M(Numbered(UnderName, 3), 10, 100, SettleBelowThreshold, true, 85, 95),
            M(Numbered(OverName, 2), 2, 110, "Hold the surge with composure.", true, 85, 95),
            M(Numbered(UnderName, 4), 10, 100, ReturnToSteadyControl, true, 85, 95),
            M(Numbered(SeatedClimbName, 2), 3, 100, SlowerCadenceSamePower, true, 75, 85),
            M(CoolDownName, 8, 45, EaseOffGradually, false)),
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    private static TrainingWorkout Vo2Max(TrainingExperienceLevel level) => level switch
    {
        TrainingExperienceLevel.Beginner => Workout("vo2-beginner", "Oxygen Sparks", "Short VO2 efforts with complete recovery.", TrainingObjective.Vo2Max, level,
            M(WarmUpName, 10, 55, BuildSmoothly, false),
            M(Numbered(Vo2EffortName, 1), 2, 108, PushFirmly, true, 95, 105),
            M(FloatName, 3, 50, RecoverBeforeRepeating, false),
            M("VO2 effort 2", 2, 110, "Hold a repeatable hard effort.", true, 95, 105),
            M(FloatName, 3, 50, RecoverBeforeRepeating, false),
            M("VO2 effort 3", 2, 112, FinishControlled, true, 95, 105),
            M(CoolDownName, 7, 45, EaseOffGradually, false)),
        TrainingExperienceLevel.Developing => Workout("vo2-developing", "Oxygen Ladders", "Progressive VO2 intervals that build time at high oxygen uptake.", TrainingObjective.Vo2Max, level,
            M(WarmUpName, 11, 55, BuildSmoothly, false),
            M(Numbered(Vo2EffortName, 1), 3, 110, PushFirmly, true, 95, 105),
            M(FloatName, 3, 50, RecoverBeforeRepeating, false),
            M("VO2 effort 2", 3, 112, "Keep pressure high but repeatable.", true, 95, 105),
            M(FloatName, 3, 50, RecoverBeforeRepeating, false),
            M("VO2 effort 3", 3, 114, FinishControlled, true, 95, 105),
            M(CoolDownName, 7, 45, EaseOffGradually, false)),
        TrainingExperienceLevel.Intermediate => Workout("vo2-intermediate", "VO2 Micro-Blocks", "Two sets of short VO2 efforts with floating recovery.", TrainingObjective.Vo2Max, level,
            M(WarmUpName, 12, 55, BuildSmoothly, false),
            M(Numbered(Vo2Name, 1), 2, 114, PushFirmly, true, 95, 105),
            M(Numbered(FloatName, 1), 1, 60, KeepPedalingLightly, false),
            M(Numbered(Vo2Name, 2), 2, 114, RepeatSamePressure, true, 95, 105),
            M(Numbered(FloatName, 2), 1, 60, KeepPedalingLightly, false),
            M(Numbered(Vo2Name, 3), 2, 116, FinishFirstSetControlled, true, 95, 105),
            M(SetRecoveryName, 5, 50, RecoverBeforeSecondSet, false),
            M(Numbered(Vo2Name, 4), 2, 116, PushFirmly, true, 95, 105),
            M(Numbered(FloatName, 3), 1, 60, KeepPedalingLightly, false),
            M(Numbered(Vo2Name, 5), 2, 118, RepeatSamePressure, true, 95, 105),
            M(Numbered(FloatName, 4), 1, 60, KeepPedalingLightly, false),
            M(Numbered(Vo2Name, 6), 2, 118, FinishControlled, true, 95, 105),
            M(CoolDownName, 8, 45, EaseOffGradually, false)),
        TrainingExperienceLevel.Experienced => Workout("vo2-experienced", "VO2 On-Offs", "Longer VO2 sets with fast floating recoveries.", TrainingObjective.Vo2Max, level,
            M(WarmUpName, 14, 55, BuildSmoothly, false),
            M(Numbered(Vo2Name, 1), 3, 116, PushFirmly, true, 95, 105),
            M(Numbered(FloatName, 1), 1, 62, KeepPedalsMoving, false),
            M(Numbered(Vo2Name, 2), 3, 116, RepeatSamePressure, true, 95, 105),
            M(Numbered(FloatName, 2), 1, 62, KeepPedalsMoving, false),
            M(Numbered(Vo2Name, 3), 3, 118, FinishFirstSetControlled, true, 95, 105),
            M(SetRecoveryName, 5, 50, RecoverBeforeSecondSet, false),
            M(Numbered(Vo2Name, 4), 3, 118, PushFirmly, true, 95, 105),
            M(Numbered(FloatName, 3), 1, 62, KeepPedalsMoving, false),
            M(Numbered(Vo2Name, 5), 3, 120, RepeatSamePressure, true, 95, 105),
            M(Numbered(FloatName, 4), 1, 62, KeepPedalsMoving, false),
            M(Numbered(Vo2Name, 6), 3, 120, FinishControlled, true, 95, 105),
            M(CoolDownName, 8, 45, EaseOffGradually, false)),
        TrainingExperienceLevel.Advanced => Workout("vo2-advanced", "VO2 Capacity", "Dense VO2 intervals with deliberately incomplete recovery.", TrainingObjective.Vo2Max, level,
            M(WarmUpName, 15, 55, BuildSmoothly, false),
            M(Numbered(Vo2Name, 1), 3, 118, PushFirmly, true, 95, 105),
            M(Numbered(FloatName, 1), 1, 62, KeepPedalsMoving, false),
            M(Numbered(Vo2Name, 2), 3, 120, RepeatSamePressure, true, 95, 105),
            M(Numbered(FloatName, 2), 1, 62, KeepPedalsMoving, false),
            M(Numbered(Vo2Name, 3), 3, 120, FinishFirstSetControlled, true, 95, 105),
            M(SetRecoveryName, 5, 50, RecoverBeforeSecondSet, false),
            M(Numbered(Vo2Name, 4), 3, 120, PushFirmly, true, 95, 105),
            M(Numbered(FloatName, 3), 1, 62, KeepPedalsMoving, false),
            M(Numbered(Vo2Name, 5), 3, 122, RepeatSamePressure, true, 95, 105),
            M(Numbered(FloatName, 4), 1, 62, KeepPedalsMoving, false),
            M(Numbered(Vo2Name, 6), 3, 122, FinishControlled, true, 95, 105),
            M(CoolDownName, 8, 45, EaseOffGradually, false)),
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    private static TrainingWorkout CadenceSkills(TrainingExperienceLevel level) => level switch
    {
        TrainingExperienceLevel.Beginner => Workout("cadence-beginner", "Smooth Pedals", "A cadence ladder and torque-to-spin contrast at fixed power.", TrainingObjective.CadenceSkills, level,
            M(WarmUpName, 6, 50, BuildSmoothly, false),
            M(Numbered(LadderStepName, 1), 2, 60, "Start the ladder at a steady, easy cadence.", true, 80, 90),
            M(Numbered(LadderStepName, 2), 2, 60, SpinUpSamePower, true, 90, 100),
            M(Numbered(LadderStepName, 3), 2, 60, SpinUpSamePower, true, 98, 108),
            M(EasyRollName, 3, 50, ReturnToRelaxedRhythm, false),
            M(Numbered(TorqueName, 1), 3, 62, "Hold the low cadence target; let the trainer set resistance.", true, 70, 80),
            M(Numbered(SpinUpName, 1), 2, 62, SpinUpSamePower, true, 95, 105),
            M(CoolDownName, 5, 45, EaseOffGradually, false)),
        TrainingExperienceLevel.Developing => Workout("cadence-developing", "Cadence Control", "A cadence ladder, then torque and spin-up pairs at the same power.", TrainingObjective.CadenceSkills, level,
            M(WarmUpName, 8, 50, BuildSmoothly, false),
            M(Numbered(LadderStepName, 1), 2, 65, "Begin below your normal cadence and press smoothly.", true, 75, 85),
            M(Numbered(LadderStepName, 2), 2, 65, SpinUpSamePower, true, 85, 95),
            M(Numbered(LadderStepName, 3), 2, 65, SpinUpSamePower, true, 95, 105),
            M(EasyRollName, 3, 50, ReturnToRelaxedRhythm, false),
            M(Numbered(TorqueName, 1), 4, 70, PressSmoothly, true, 65, 75),
            M(Numbered(SpinUpName, 1), 2, 70, SpinUpSamePower, true, 98, 108),
            M(Numbered(TorqueName, 2), 3, 70, SlowerCadenceSamePower, true, 65, 75),
            M(CoolDownName, 6, 45, EaseOffGradually, false)),
        TrainingExperienceLevel.Intermediate => Workout("cadence-intermediate", "Torque and Spin", "Torque-spin contrast and a cadence pyramid, each at fixed power.", TrainingObjective.CadenceSkills, level,
            M(WarmUpName, 10, 50, BuildSmoothly, false),
            M(Numbered(TorqueName, 1), 5, 75, PressSmoothly, true, 60, 70),
            M(Numbered(SpinUpName, 1), 2, 75, SpinUpSamePower, true, 100, 110),
            M(Numbered(TorqueName, 2), 4, 75, SlowerCadenceSamePower, true, 60, 70),
            M(EasyRollName, 3, 50, ReturnToRelaxedRhythm, false),
            M(Numbered(PyramidStepName, 1), 2, 70, "Start the pyramid below your normal cadence.", true, 75, 85),
            M(Numbered(PyramidStepName, 2), 2, 70, SpinUpSamePower, true, 90, 100),
            M(Numbered(PyramidStepName, 3), 2, 70, SpinUpSamePower, true, 100, 110),
            M(Numbered(PyramidStepName, 4), 2, 70, SlowerCadenceSamePower, true, 90, 100),
            M(Numbered(PyramidStepName, 5), 2, 70, SlowerCadenceSamePower, true, 75, 85),
            M(CadenceCloseName, 4, 70, "Choose the most efficient rhythm.", true, 85, 95),
            M(CoolDownName, 7, 45, EaseOffGradually, false)),
        TrainingExperienceLevel.Experienced => Workout("cadence-experienced", "Cadence Range", "Longer torque-spin pairs and a wide cadence pyramid at fixed power.", TrainingObjective.CadenceSkills, level,
            M(WarmUpName, 12, 50, BuildSmoothly, false),
            M(Numbered(TorqueName, 1), 6, 78, PressSmoothly, true, 60, 70),
            M(Numbered(SpinUpName, 1), 3, 78, SpinUpSamePower, true, 100, 110),
            M(Numbered(TorqueName, 2), 5, 78, SlowerCadenceSamePower, true, 60, 70),
            M(EasyRollName, 3, 50, ReturnToRelaxedRhythm, false),
            M(Numbered(PyramidStepName, 1), 2, 72, "Start the pyramid well below your normal cadence.", true, 70, 80),
            M(Numbered(PyramidStepName, 2), 2, 72, SpinUpSamePower, true, 85, 95),
            M(Numbered(PyramidStepName, 3), 2, 72, SpinUpSamePower, true, 100, 110),
            M(Numbered(PyramidStepName, 4), 2, 72, SlowerCadenceSamePower, true, 85, 95),
            M(Numbered(PyramidStepName, 5), 2, 72, SlowerCadenceSamePower, true, 70, 80),
            M(EasyRollName, 2, 50, ReturnToRelaxedRhythm, false),
            M(Numbered(TorqueName, 3), 5, 80, "Hold pressure without grinding.", true, 60, 70),
            M(CadenceCloseName, 5, 80, ReturnToCadenceSamePower, true, 85, 95),
            M(CoolDownName, 8, 45, EaseOffGradually, false)),
        TrainingExperienceLevel.Advanced => Workout("cadence-advanced", "Pedaling Economy", "Extended torque-spin contrast and a wide cadence pyramid for efficient force production.", TrainingObjective.CadenceSkills, level,
            M(WarmUpName, 12, 50, BuildSmoothly, false),
            M(Numbered(TorqueName, 1), 7, 80, PressSmoothly, true, 60, 70),
            M(Numbered(SpinUpName, 1), 3, 80, SpinUpSamePower, true, 100, 110),
            M(Numbered(TorqueName, 2), 6, 80, SlowerCadenceSamePower, true, 60, 70),
            M(Numbered(SpinUpName, 2), 3, 80, SpinUpSamePower, true, 102, 112),
            M(EasyRollName, 3, 50, ReturnToRelaxedRhythm, false),
            M(Numbered(PyramidStepName, 1), 2, 75, "Start the pyramid well below your normal cadence.", true, 70, 80),
            M(Numbered(PyramidStepName, 2), 2, 75, SpinUpSamePower, true, 85, 95),
            M(Numbered(PyramidStepName, 3), 2, 75, SpinUpSamePower, true, 100, 110),
            M(Numbered(PyramidStepName, 4), 2, 75, SlowerCadenceSamePower, true, 85, 95),
            M(Numbered(PyramidStepName, 5), 2, 75, SlowerCadenceSamePower, true, 70, 80),
            M(EasyRollName, 2, 50, ReturnToRelaxedRhythm, false),
            M(Numbered(TorqueName, 3), 6, 84, "Hold pressure without grinding.", true, 60, 70),
            M(CadenceCloseName, 6, 84, ReturnToCadenceSamePower, true, 85, 95),
            M(CoolDownName, 8, 45, EaseOffGradually, false)),
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    private static TrainingWorkout Anaerobic(TrainingExperienceLevel level) => level switch
    {
        TrainingExperienceLevel.Beginner => Workout("anaerobic-beginner", "Burst Primer", "ERG-paced repeatable efforts with full recovery and a cadence build before each burst.", TrainingObjective.Anaerobic, level,
            M(WarmUpName, 10, 55, BuildSmoothly, false),
            S(CadencePreparationName, 30, 55, BuildCadenceBeforeBurst, false, 90, 105),
            S(Numbered(BurstName, 1), 30, 122, HoldRepeatableEffort, true, 90, 105),
            M("Full recovery", 3, 48, "Recover completely.", false),
            S(CadencePreparationName, 30, 55, BuildCadenceBeforeBurst, false, 90, 105),
            S(Numbered(BurstName, 2), 30, 124, HoldRepeatableEffort, true, 90, 105),
            M("Full recovery", 3, 48, "Recover completely.", false),
            S(CadencePreparationName, 30, 55, BuildCadenceBeforeBurst, false, 90, 105),
            S(Numbered(BurstName, 3), 30, 126, FinishSharp, true, 90, 105),
            M(CoolDownName, 8, 45, EaseOffGradually, false)),
        TrainingExperienceLevel.Developing => Workout("anaerobic-developing", "Controlled Burst Starter", "Two sets of short ERG-paced efforts with extended recovery and cadence builds.", TrainingObjective.Anaerobic, level,
            M(WarmUpName, 11, 55, BuildSmoothly, false),
            S(CadencePreparationName, 30, 55, BuildCadenceBeforeBurst, false, 92, 108),
            S(Numbered(BurstName, 1), 30, 126, HoldRepeatableEffort, true, 92, 108),
            S(Numbered(FloatName, 1), 90, 55, "Recover easily before the next cadence build.", false, 85, 95),
            S(CadencePreparationName, 30, 55, BuildCadenceBeforeBurst, false, 92, 108),
            S(Numbered(BurstName, 2), 30, 126, RepeatSamePressure, true, 92, 108),
            S(Numbered(FloatName, 2), 90, 55, "Recover easily before the next cadence build.", false, 85, 95),
            S(CadencePreparationName, 30, 55, BuildCadenceBeforeBurst, false, 92, 108),
            S(Numbered(BurstName, 3), 30, 128, FinishFirstSetControlled, true, 92, 108),
            M(SetRecoveryName, 4, 48, RecoverCompletelyBeforeSecondSet, false),
            S(CadencePreparationName, 30, 55, BuildCadenceBeforeBurst, false, 92, 108),
            S(Numbered(BurstName, 4), 30, 128, HoldRepeatableEffort, true, 92, 108),
            S(Numbered(FloatName, 3), 90, 55, "Recover easily before the next cadence build.", false, 85, 95),
            S(CadencePreparationName, 30, 55, BuildCadenceBeforeBurst, false, 92, 108),
            S(Numbered(BurstName, 5), 30, 130, RepeatSamePressure, true, 92, 108),
            S(Numbered(FloatName, 4), 90, 55, "Recover easily before the next cadence build.", false, 85, 95),
            S(CadencePreparationName, 30, 55, BuildCadenceBeforeBurst, false, 92, 108),
            S(Numbered(BurstName, 6), 30, 130, FinishSharp, true, 92, 108),
            M(CoolDownName, 8, 45, EaseOffGradually, false)),
        TrainingExperienceLevel.Intermediate => Workout("anaerobic-intermediate", "Anaerobic Repeatability", "Controlled ERG-paced efforts with 3:1 recoveries and a cadence build before each burst.", TrainingObjective.Anaerobic, level,
            M(WarmUpName, 12, 55, BuildSmoothly, false),
            S(CadencePreparationName, 30, 55, BuildCadenceBeforeBurst, false, 94, 110),
            S(Numbered(BurstName, 1), 40, 130, HoldRepeatableEffort, true, 94, 110),
            S(Numbered(FloatName, 1), 2 * 60, 55, "Recover easily before the next cadence build.", false, 85, 95),
            S(CadencePreparationName, 30, 55, BuildCadenceBeforeBurst, false, 94, 110),
            S(Numbered(BurstName, 2), 40, 130, RepeatSamePressure, true, 94, 110),
            S(Numbered(FloatName, 2), 2 * 60, 55, "Recover easily before the next cadence build.", false, 85, 95),
            S(CadencePreparationName, 30, 55, BuildCadenceBeforeBurst, false, 94, 110),
            S(Numbered(BurstName, 3), 40, 132, FinishFirstSetControlled, true, 94, 110),
            M(SetRecoveryName, 5, 48, RecoverCompletelyBeforeSecondSet, false),
            S(CadencePreparationName, 30, 55, BuildCadenceBeforeBurst, false, 94, 110),
            S(Numbered(BurstName, 4), 40, 132, HoldRepeatableEffort, true, 94, 110),
            S(Numbered(FloatName, 3), 2 * 60, 55, "Recover easily before the next cadence build.", false, 85, 95),
            S(CadencePreparationName, 30, 55, BuildCadenceBeforeBurst, false, 94, 110),
            S(Numbered(BurstName, 5), 40, 134, RepeatSamePressure, true, 94, 110),
            S(Numbered(FloatName, 4), 2 * 60, 55, "Recover easily before the next cadence build.", false, 85, 95),
            S(CadencePreparationName, 30, 55, BuildCadenceBeforeBurst, false, 94, 110),
            S(Numbered(BurstName, 6), 40, 134, FinishSharp, true, 94, 110),
            M(CoolDownName, 8, 45, EaseOffGradually, false)),
        TrainingExperienceLevel.Experienced => Workout("anaerobic-experienced", "Punchy Repeatability", "Controlled ERG-paced efforts with generous recovery and a cadence build before each burst.", TrainingObjective.Anaerobic, level,
            M(WarmUpName, 14, 55, BuildSmoothly, false),
            S(CadencePreparationName, 30, 55, BuildCadenceBeforeBurst, false, 94, 110),
            S(Numbered(BurstName, 1), 45, 134, HoldRepeatableEffort, true, 94, 110),
            S(Numbered(FloatName, 1), 2 * 60, 58, "Recover easily before the next cadence build.", false, 85, 95),
            S(CadencePreparationName, 30, 55, BuildCadenceBeforeBurst, false, 94, 110),
            S(Numbered(BurstName, 2), 45, 134, RepeatSamePressure, true, 94, 110),
            S(Numbered(FloatName, 2), 2 * 60, 58, "Recover easily before the next cadence build.", false, 85, 95),
            S(CadencePreparationName, 30, 55, BuildCadenceBeforeBurst, false, 94, 110),
            S(Numbered(BurstName, 3), 45, 136, FinishFirstSetControlled, true, 94, 110),
            M(SetRecoveryName, 5, 48, RecoverCompletelyBeforeSecondSet, false),
            S(CadencePreparationName, 30, 55, BuildCadenceBeforeBurst, false, 94, 110),
            S(Numbered(BurstName, 4), 45, 136, HoldRepeatableEffort, true, 94, 110),
            S(Numbered(FloatName, 3), 2 * 60, 58, "Recover easily before the next cadence build.", false, 85, 95),
            S(CadencePreparationName, 30, 55, BuildCadenceBeforeBurst, false, 94, 110),
            S(Numbered(BurstName, 5), 45, 138, RepeatSamePressure, true, 94, 110),
            S(Numbered(FloatName, 4), 2 * 60, 58, "Recover easily before the next cadence build.", false, 85, 95),
            S(CadencePreparationName, 30, 55, BuildCadenceBeforeBurst, false, 94, 110),
            S(Numbered(BurstName, 6), 45, 138, FinishSharp, true, 94, 110),
            M(CoolDownName, 8, 45, EaseOffGradually, false)),
        TrainingExperienceLevel.Advanced => Workout("anaerobic-advanced", "Anaerobic Capacity", "Controlled ERG-paced efforts with 3:1 recovery and a cadence build before each burst; not all-out sprints.", TrainingObjective.Anaerobic, level,
            M(WarmUpName, 15, 55, BuildSmoothly, false),
            S(CadencePreparationName, 30, 55, BuildCadenceBeforeBurst, false, 94, 110),
            M(Numbered(BurstName, 1), 1, 138, HoldRepeatableEffort, true, 94, 110),
            M(Numbered(FloatName, 1), 3, 60, "Recover easily before the next cadence build.", false, 85, 95),
            S(CadencePreparationName, 30, 55, BuildCadenceBeforeBurst, false, 94, 110),
            M(Numbered(BurstName, 2), 1, 138, RepeatSamePressure, true, 94, 110),
            M(Numbered(FloatName, 2), 3, 60, "Recover easily before the next cadence build.", false, 85, 95),
            S(CadencePreparationName, 30, 55, BuildCadenceBeforeBurst, false, 94, 110),
            M(Numbered(BurstName, 3), 1, 140, FinishFirstSetControlled, true, 94, 110),
            M(SetRecoveryName, 6, 48, RecoverCompletelyBeforeSecondSet, false),
            S(CadencePreparationName, 30, 55, BuildCadenceBeforeBurst, false, 94, 110),
            M(Numbered(BurstName, 4), 1, 140, HoldRepeatableEffort, true, 94, 110),
            M(Numbered(FloatName, 3), 3, 60, "Recover easily before the next cadence build.", false, 85, 95),
            S(CadencePreparationName, 30, 55, BuildCadenceBeforeBurst, false, 94, 110),
            M(Numbered(BurstName, 5), 1, 142, RepeatSamePressure, true, 94, 110),
            M(Numbered(FloatName, 4), 3, 60, "Recover easily before the next cadence build.", false, 85, 95),
            S(CadencePreparationName, 30, 55, BuildCadenceBeforeBurst, false, 94, 110),
            M(Numbered(BurstName, 6), 1, 142, FinishSharp, true, 94, 110),
            M(CoolDownName, 8, 45, EaseOffGradually, false)),
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    private static TrainingWorkout Workout(
        string id,
        string name,
        string description,
        TrainingObjective objective,
        TrainingExperienceLevel level,
        params TrainingSegment[] segments) => new(id, name, description, objective, level, AddWarmUpCadenceGuidance(segments));

    // Opening segments start with a broad cadence range, then lift toward the first work range so riders arrive prepared.
    private static TrainingSegment[] AddWarmUpCadenceGuidance(TrainingSegment[] segments)
    {
        if (segments is not [{ Name: EaseInName or WarmUpName, MinimumCadenceRpm: null, MaximumCadenceRpm: null } opening, ..]
            || opening.Duration <= CadenceLiftDuration)
        {
            return segments;
        }

        return
        [
            opening with
            {
                Duration = opening.Duration - CadenceLiftDuration,
                MinimumCadenceRpm = BroadWarmUpMinimumCadenceRpm,
                MaximumCadenceRpm = BroadWarmUpMaximumCadenceRpm,
            },
            new(CadenceLiftName, CadenceLiftDuration, opening.FunctionalThresholdPowerPercentage,
                CadenceLiftMinimumCadenceRpm, CadenceLiftMaximumCadenceRpm, CadenceLiftCue, false),
            .. segments[1..],
        ];
    }

    private static string Numbered(string name, int number) => $"{name} {number}";

    private static TrainingSegment M(
        string name,
        int minutes,
        double target,
        string cue,
        bool countsTowardAdherence,
        int? minimumCadenceRpm = null,
        int? maximumCadenceRpm = null) => new(name, TimeSpan.FromMinutes(minutes), target, minimumCadenceRpm, maximumCadenceRpm, cue, countsTowardAdherence);

    private static TrainingSegment S(
        string name,
        int seconds,
        double target,
        string cue,
        bool countsTowardAdherence,
        int? minimumCadenceRpm = null,
        int? maximumCadenceRpm = null) => new(name, TimeSpan.FromSeconds(seconds), target, minimumCadenceRpm, maximumCadenceRpm, cue, countsTowardAdherence);
}