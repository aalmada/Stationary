using System.Globalization;

using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using ReactiveUI;

using Stationary.Ftms;

namespace Stationary.Ftms.Dashboard.ViewModels;

public sealed class TargetControlViewModel : ReactiveObject, IDisposable
{
    private static readonly TimeSpan AutoApplyDelay = TimeSpan.FromMilliseconds(250);

    private readonly Func<double, CancellationToken, Task<bool>> applyAsync;
    private readonly ISubject<AutoApplyRequest>? autoApplyRequests;
    private readonly IScheduler autoApplyScheduler;
    private readonly ObservableAsPropertyHelper<bool> canApply;
    private readonly CompositeDisposable disposables = [];
    private readonly ObservableAsPropertyHelper<bool> isApplying;
    private readonly string valueFormat;
    private double value;
    private long valueRevision;
    private TargetValueState valueState;

    public TargetControlViewModel(string name, string unit, double minimum, double maximum, double increment, FtmsMachineStatusOpcode statusOpcode, Func<int, double> fromRawValue, Func<double, CancellationToken, Task<bool>> applyAsync, IObservable<bool> canApply, bool autoApplyOnValueChange = false, IScheduler? autoApplyScheduler = null)
    {
        Name = name;
        Unit = unit;
        Minimum = minimum;
        Maximum = maximum;
        Increment = increment;
        valueFormat = $"F{GetDecimalPlaces(increment)}";
        StatusOpcode = statusOpcode;
        value = minimum;
        FromRawValue = fromRawValue;
        this.applyAsync = applyAsync;
        AutomaticallyAppliesValue = autoApplyOnValueChange;
        this.autoApplyScheduler = autoApplyScheduler ?? RxApp.TaskpoolScheduler;
        ApplyCommand = ReactiveCommand.CreateFromTask<object?>(ApplyAsync, canApply);
        this.canApply = canApply
            .DistinctUntilChanged()
            .ToProperty(this, viewModel => viewModel.CanApply);
        DecreaseCommand = ReactiveCommand.Create(() =>
        {
            Value -= Increment;
        }, canApply);
        IncreaseCommand = ReactiveCommand.Create(() =>
        {
            Value += Increment;
        }, canApply);
        ApplyPresetCommand = ReactiveCommand.Create<double>(preset => Value = preset, canApply);
        isApplying = ApplyCommand.IsExecuting
            .DistinctUntilChanged()
            .ToProperty(this, viewModel => viewModel.IsApplying);
        if (AutomaticallyAppliesValue)
        {
            var requests = Subject.Synchronize(new Subject<AutoApplyRequest>());
            autoApplyRequests = requests;
            disposables.Add(requests
                .Throttle(AutoApplyDelay, this.autoApplyScheduler)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Where(request => request.Revision == Interlocked.Read(ref valueRevision))
                .Select(request => ApplyCommand.Execute(request.Value)
                    .Catch<Unit, Exception>(static _ => Observable.Empty<Unit>()))
                .Concat()
                .Subscribe());
        }
    }

    public string Name { get; }

    public string Unit { get; }

    public double Minimum { get; }

    public double Maximum { get; }

    public double Increment { get; }

    public FtmsMachineStatusOpcode StatusOpcode { get; }

    public Func<int, double> FromRawValue { get; }

    public bool AutomaticallyAppliesValue { get; }

    public bool RequiresExplicitApply => !AutomaticallyAppliesValue;

    public bool CanApply => canApply.Value;

    public double Value
    {
        get => value;
        set => SetValue(value, true);
    }

    public string RequestedValueText => $"{ValueText} {Unit}".TrimEnd();

    public string ValueStateText => valueState switch
    {
        TargetValueState.SelectedInDashboard => $"Selected: {RequestedValueText}",
        TargetValueState.AcceptedByBike => $"Accepted: {RequestedValueText}",
        TargetValueState.ReportedByBike => $"Bike reported: {RequestedValueText}",
        TargetValueState.ReportedOutsideSupportedRange => "Bike reported an unsupported value",
        _ => "Not reported by bike",
    };

    public string ValueText
    {
        get => Value.ToString(valueFormat, CultureInfo.CurrentCulture);
        set
        {
            if (double.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed))
            {
                Value = parsed;
            }
        }
    }

    public ReactiveCommand<object?, Unit> ApplyCommand { get; }

    public ReactiveCommand<Unit, Unit> DecreaseCommand { get; }

    public ReactiveCommand<Unit, Unit> IncreaseCommand { get; }

    public ReactiveCommand<double, Unit> ApplyPresetCommand { get; } = null!;

    public bool IsApplying => isApplying.Value;

    public IReadOnlyList<double> Presets => [.. new double[]
    {
        double.Clamp(Value - (Increment * 5), Minimum, Maximum),
        double.Clamp(Value - Increment, Minimum, Maximum),
        double.Clamp(Value + Increment, Minimum, Maximum),
        double.Clamp(Value + (Increment * 5), Minimum, Maximum),
    }.Distinct()];

    public void SetRawValue(int rawValue) => TrySetRawValue(rawValue);

    public bool TrySetRawValue(int rawValue)
    {
        var reportedValue = FromRawValue(rawValue);
        var alignedValue = Minimum + (double.Round((reportedValue - Minimum) / Increment) * Increment);
        if (reportedValue < Minimum || reportedValue > Maximum || double.Abs(reportedValue - alignedValue) > 0.000_001d)
        {
            if (AutomaticallyAppliesValue)
            {
                Interlocked.Increment(ref valueRevision);
            }

            SetValueState(TargetValueState.ReportedOutsideSupportedRange);
            return false;
        }

        SetValue(reportedValue, false, true, TargetValueState.ReportedByBike);
        return true;
    }

    public void Dispose()
    {
        disposables.Dispose();
        ApplyCommand.Dispose();
        canApply.Dispose();
        DecreaseCommand.Dispose();
        IncreaseCommand.Dispose();
        ApplyPresetCommand.Dispose();
        isApplying.Dispose();
    }

    private void SetValue(double requestedValue, bool requestAutoApply, bool invalidatePendingAutoApply = false, TargetValueState? newValueState = null)
    {
        var aligned = Minimum + double.Round((requestedValue - Minimum) / Increment) * Increment;
        var clamped = double.Clamp(aligned, Minimum, Maximum);
        if (value != clamped)
        {
            this.RaiseAndSetIfChanged(ref value, clamped);
            this.RaisePropertyChanged(nameof(RequestedValueText));
            this.RaisePropertyChanged(nameof(ValueText));
            this.RaisePropertyChanged(nameof(Presets));
            this.RaisePropertyChanged(nameof(ValueStateText));
            if (AutomaticallyAppliesValue)
            {
                var revision = Interlocked.Increment(ref valueRevision);
                if (requestAutoApply)
                {
                    autoApplyRequests?.OnNext(new(clamped, revision));
                }
            }
        }
        else if (invalidatePendingAutoApply && AutomaticallyAppliesValue)
        {
            Interlocked.Increment(ref valueRevision);
        }

        if (requestAutoApply)
        {
            SetValueState(TargetValueState.SelectedInDashboard);
        }
        else if (newValueState is { } state)
        {
            SetValueState(state);
        }
    }

    private async Task ApplyAsync(object? requestedValue, CancellationToken cancellationToken)
    {
        var valueToApply = requestedValue is double value ? value : Value;
        if (await applyAsync(valueToApply, cancellationToken) && Value == valueToApply)
        {
            SetValueState(TargetValueState.AcceptedByBike);
        }
    }

    private void SetValueState(TargetValueState state)
    {
        if (valueState != state)
        {
            valueState = state;
            this.RaisePropertyChanged(nameof(ValueStateText));
        }
    }

    private readonly record struct AutoApplyRequest(double Value, long Revision);

    private enum TargetValueState
    {
        Unknown,
        SelectedInDashboard,
        AcceptedByBike,
        ReportedByBike,
        ReportedOutsideSupportedRange,
    }

    private static int GetDecimalPlaces(double increment)
    {
        var places = 0;
        while (places < 4 && double.Abs(increment - double.Round(increment)) > 0.000_001d)
        {
            increment *= 10d;
            places++;
        }

        return places;
    }
}