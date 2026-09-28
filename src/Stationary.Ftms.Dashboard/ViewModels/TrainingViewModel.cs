using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using ReactiveUI;

using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.ViewModels;

public sealed class TrainingViewModel : ReactiveObject, IDisposable
{
    private readonly DashboardViewModel dashboard;
    private readonly CompositeDisposable disposables = [];
    private TrainingWorkout? selectedWorkout;
    private TrainingWorkoutRunner? runner;
    private TrainingPowerCapabilities rampCapabilities;
    private DateTimeOffset? rampStartedAt;
    private DateTimeOffset? segmentStartedAt;
    private RampFtpEstimate? rampEstimate;
    private bool isRampTest;
    private string statusText = "Choose a workout and set FTP in Session.";
    private string adherenceText = "Precision score appears after a work interval.";
    private int currentTargetWatts;

    public TrainingViewModel(DashboardViewModel dashboard)
    {
        this.dashboard = dashboard;
        Workouts = TrainingCatalog.Load().Workouts;
        selectedWorkout = Workouts.FirstOrDefault();
        StartCommand = ReactiveCommand.CreateFromTask(StartAsync, this.WhenAnyValue(viewModel => viewModel.SelectedWorkout).Select(static workout => workout is not null));
        StartRampTestCommand = ReactiveCommand.CreateFromTask(StartRampTestAsync);
        PauseCommand = ReactiveCommand.CreateFromTask(PauseAsync, this.WhenAnyValue(viewModel => viewModel.RunnerState).Select(state => state == TrainingWorkoutState.Running));
        ResumeCommand = ReactiveCommand.CreateFromTask(ResumeAsync, this.WhenAnyValue(viewModel => viewModel.RunnerState).Select(state => state == TrainingWorkoutState.Paused));
        RetryCommand = ReactiveCommand.CreateFromTask(RetryAsync, this.WhenAnyValue(viewModel => viewModel.RunnerState).Select(state => state == TrainingWorkoutState.TargetFailed));
        EndCommand = ReactiveCommand.Create(End, this.WhenAnyValue(viewModel => viewModel.RunnerState).Select(state => state is TrainingWorkoutState.ApplyingTarget or TrainingWorkoutState.Running or TrainingWorkoutState.Paused or TrainingWorkoutState.TargetFailed));
        FinishRampTestCommand = ReactiveCommand.CreateFromTask(FinishRampTestAsync, this.WhenAnyValue(viewModel => viewModel.IsRampTest, viewModel => viewModel.IsActive, (ramp, active) => ramp && active));
        AcceptRampEstimateCommand = ReactiveCommand.Create(AcceptRampEstimate, this.WhenAnyValue(viewModel => viewModel.RampEstimate).Select(estimate => estimate is { IsReady: true, EstimatedFtpWatts: not null }));
        disposables.Add(Observable.Interval(TimeSpan.FromSeconds(1), RxApp.TaskpoolScheduler)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => AdvanceIfDue()));
    }

    public IReadOnlyList<TrainingWorkout> Workouts { get; }

    public TrainingWorkout? SelectedWorkout
    {
        get => selectedWorkout;
        set => this.RaiseAndSetIfChanged(ref selectedWorkout, value);
    }

    public TrainingWorkoutState RunnerState => runner?.State ?? TrainingWorkoutState.Selected;

    public bool IsActive => RunnerState is TrainingWorkoutState.ApplyingTarget or TrainingWorkoutState.Running or TrainingWorkoutState.Paused or TrainingWorkoutState.TargetFailed;

    public bool IsRampTest => isRampTest;

    public RampFtpEstimate? RampEstimate
    {
        get => rampEstimate;
        private set => this.RaiseAndSetIfChanged(ref rampEstimate, value);
    }

    public string RampEstimateText => RampEstimate switch
    {
        { Status: RampFtpEstimateStatus.Ready, EstimatedFtpWatts: { } ftp } => $"Estimated FTP: {ftp} W. Review and accept to update zones.",
        { Status: RampFtpEstimateStatus.DeviceMaximumReached } => "Ramp test was inconclusive because the bike reached its advertised power maximum.",
        { Status: RampFtpEstimateStatus.InsufficientTelemetry } => "Ramp test was inconclusive because a continuous measured minute was not recorded.",
        _ => "",
    };

    public string StatusText
    {
        get => statusText;
        private set => this.RaiseAndSetIfChanged(ref statusText, value);
    }

    public string AdherenceText
    {
        get => adherenceText;
        private set => this.RaiseAndSetIfChanged(ref adherenceText, value);
    }

    public string CurrentSegmentText => runner?.CurrentSegment is { } segment
        ? $"{segment.Definition.Name}: {segment.TargetWatts} W"
        : "No active interval";

    public int CurrentTargetWatts
    {
        get => currentTargetWatts;
        private set => this.RaiseAndSetIfChanged(ref currentTargetWatts, value);
    }

    public ReactiveCommand<Unit, Unit> StartCommand { get; }

    public ReactiveCommand<Unit, Unit> StartRampTestCommand { get; }

    public ReactiveCommand<Unit, Unit> PauseCommand { get; }

    public ReactiveCommand<Unit, Unit> ResumeCommand { get; }

    public ReactiveCommand<Unit, Unit> RetryCommand { get; }

    public ReactiveCommand<Unit, Unit> EndCommand { get; }

    public ReactiveCommand<Unit, Unit> FinishRampTestCommand { get; }

    public ReactiveCommand<Unit, Unit> AcceptRampEstimateCommand { get; }

    public void Dispose()
    {
        disposables.Dispose();
        StartCommand.Dispose();
        StartRampTestCommand.Dispose();
        PauseCommand.Dispose();
        ResumeCommand.Dispose();
        RetryCommand.Dispose();
        EndCommand.Dispose();
        FinishRampTestCommand.Dispose();
        AcceptRampEstimateCommand.Dispose();
    }

    private async Task StartAsync(CancellationToken cancellationToken)
    {
        if (SelectedWorkout is null || !ushort.TryParse(dashboard.Telemetry.FunctionalThresholdPowerText, out var ftp) || ftp == 0)
        {
            StatusText = "Set a positive FTP in Session before starting training.";
            return;
        }

        if (!dashboard.TryGetTrainingPowerCapabilities(out var capabilities))
        {
            StatusText = "This bike does not expose a usable target-power range.";
            return;
        }

        try
        {
            if (!await dashboard.StartTrainingSessionAsync(cancellationToken))
            {
                StatusText = "The bike session could not be started.";
                return;
            }

            isRampTest = false;
            await StartRunnerAsync(TrainingWorkoutCompiler.Compile(SelectedWorkout, ftp, capabilities), cancellationToken);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            runner = null;
            StatusText = exception.Message;
            NotifyRunnerChanged();
        }
    }

    private async Task StartRampTestAsync(CancellationToken cancellationToken)
    {
        if (!dashboard.TryGetTrainingPowerCapabilities(out rampCapabilities))
        {
            StatusText = "This bike does not expose a usable target-power range.";
            return;
        }

        if (!await dashboard.StartTrainingSessionAsync(cancellationToken))
        {
            StatusText = "The bike session could not be started.";
            return;
        }

        isRampTest = true;
        rampStartedAt = DateTimeOffset.UtcNow;
        RampEstimate = null;
        await StartRunnerAsync(RampFtpProtocol.Create(rampCapabilities), cancellationToken);
    }

    private async Task PauseAsync(CancellationToken cancellationToken)
    {
        if (!await dashboard.PauseTrainingSessionAsync(cancellationToken))
        {
            StatusText = "The bike session could not be paused.";
            return;
        }

        runner?.Pause(DateTimeOffset.UtcNow);
        StatusText = "Training timer paused.";
        NotifyRunnerChanged();
    }

    private async Task ResumeAsync(CancellationToken cancellationToken)
    {
        if (!await dashboard.StartTrainingSessionAsync(cancellationToken))
        {
            StatusText = "The bike session could not be resumed.";
            return;
        }

        runner?.Resume(DateTimeOffset.UtcNow);
        StatusText = "Training timer resumed.";
        NotifyRunnerChanged();
    }

    private async Task RetryAsync(CancellationToken cancellationToken)
    {
        runner?.RetryTarget();
        NotifyRunnerChanged();
        await ApplyCurrentTargetAsync(cancellationToken);
    }

    private void End()
    {
        runner?.Abort();
        isRampTest = false;
        StatusText = "Training ended.";
        NotifyRunnerChanged();
    }

    private async Task FinishRampTestAsync(CancellationToken cancellationToken)
    {
        if (!isRampTest || rampStartedAt is not { } startedAt)
        {
            return;
        }

        if (!await dashboard.PauseTrainingSessionAsync(cancellationToken))
        {
            StatusText = "The bike session could not be paused.";
            return;
        }

        runner?.Abort();
        RampEstimate = RampFtpEstimator.Estimate(dashboard.GetRecordedSamplesSince(startedAt), rampCapabilities.MaximumWatts);
        isRampTest = false;
        StatusText = RampEstimateText;
        NotifyRunnerChanged();
    }

    private void AcceptRampEstimate()
    {
        if (RampEstimate is not { IsReady: true, EstimatedFtpWatts: { } ftp })
        {
            return;
        }

        dashboard.Telemetry.FunctionalThresholdPowerText = ftp.ToString(System.Globalization.CultureInfo.InvariantCulture);
        dashboard.Telemetry.ApplyFunctionalThresholdPowerCommand.Execute();
        StatusText = $"FTP updated to {ftp} W.";
    }

    private void AdvanceIfDue()
    {
        var now = DateTimeOffset.UtcNow;
        var completedSegment = runner?.CurrentSegment;
        if (runner?.Advance(now) != true)
        {
            return;
        }

        RecordAdherence(completedSegment, now);
        NotifyRunnerChanged();
        if (runner.State == TrainingWorkoutState.Completed)
        {
            StatusText = "Workout complete.";
            return;
        }

        _ = ApplyCurrentTargetAsync(CancellationToken.None);
    }

    private async Task ApplyCurrentTargetAsync(CancellationToken cancellationToken)
    {
        if (runner?.CurrentSegment is not { } segment)
        {
            return;
        }

        CurrentTargetWatts = segment.TargetWatts;
        StatusText = $"Applying {segment.TargetWatts} W for {segment.Definition.Name}.";
        if (await dashboard.ApplyTrainingTargetAsync(segment.TargetWatts, cancellationToken))
        {
            segmentStartedAt = DateTimeOffset.UtcNow;
            runner.ConfirmTargetApplied(segmentStartedAt.Value);
            StatusText = segment.Definition.Cue;
        }
        else
        {
            runner.RejectTarget();
            StatusText = "The bike did not accept this target. Retry or end training.";
        }

        NotifyRunnerChanged();
    }

    private async Task StartRunnerAsync(CompiledTrainingWorkout workout, CancellationToken cancellationToken)
    {
        runner = new(workout);
        runner.Start();
        NotifyRunnerChanged();
        await ApplyCurrentTargetAsync(cancellationToken);
    }

    private void NotifyRunnerChanged()
    {
        this.RaisePropertyChanged(nameof(RunnerState));
        this.RaisePropertyChanged(nameof(IsActive));
        this.RaisePropertyChanged(nameof(IsRampTest));
        this.RaisePropertyChanged(nameof(CurrentSegmentText));
        this.RaisePropertyChanged(nameof(CurrentTargetWatts));
        this.RaisePropertyChanged(nameof(RampEstimateText));
    }

    private void RecordAdherence(CompiledTrainingSegment? segment, DateTimeOffset completedAt)
    {
        if (segment is not { Definition.CountsTowardAdherence: true } || segmentStartedAt is not { } startedAt || !dashboard.TryGetTrainingPowerCapabilities(out var capabilities))
        {
            return;
        }

        var samples = dashboard.GetRecordedSamplesSince(startedAt)
            .Select(static sample => new TrainingAdherenceSample(sample.CapturedAt, sample.PowerWatts))
            .ToArray();
        var result = TrainingAdherence.Calculate(samples, segment.TargetWatts, capabilities.IncrementWatts, startedAt, completedAt, completed: true);
        AdherenceText = result.Percentage is double percentage
            ? $"Precision: {percentage:F0}% · {result.Stars} star{(result.Stars == 1 ? string.Empty : "s")}"
            : "Precision unavailable: no measured power was recorded.";
    }
}