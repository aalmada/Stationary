using ReactiveUI;

namespace Stationary.Ftms.Dashboard;

public sealed class DeviceCapability(string name, bool isTelemetry) : ReactiveObject
{
    private bool isObserved;
    private bool isUnavailable;
    private bool isStale;

    public string Name { get; } = name;

    public bool IsTelemetry { get; } = isTelemetry;

    public bool IsObserved
    {
        get => isObserved;
        set
        {
            if (this.RaiseAndSetIfChanged(ref isObserved, value))
            {
                this.RaisePropertyChanged(nameof(Status));
                this.RaisePropertyChanged(nameof(IsNotProvided));
            }
        }
    }

    public bool IsStale
    {
        get => isStale;
        set
        {
            if (this.RaiseAndSetIfChanged(ref isStale, value))
            {
                this.RaisePropertyChanged(nameof(Status));
                this.RaisePropertyChanged(nameof(IsNotProvided));
            }
        }
    }

    public bool IsUnavailable
    {
        get => isUnavailable;
        set
        {
            if (this.RaiseAndSetIfChanged(ref isUnavailable, value))
            {
                this.RaisePropertyChanged(nameof(Status));
                this.RaisePropertyChanged(nameof(IsNotProvided));
            }
        }
    }

    public string Status => IsTelemetry
        ? IsUnavailable ? "Advertised, reports no usable value" : !IsObserved ? "Advertised, not observed in this session" : IsStale ? "Observed, not advancing during activity" : "Observed"
        : "Advertised operation";

    public bool IsNotProvided => IsTelemetry && (IsUnavailable || !IsObserved || IsStale);
}