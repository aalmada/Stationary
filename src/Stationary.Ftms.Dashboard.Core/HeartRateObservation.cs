namespace Stationary.Ftms.Dashboard.Core;

public readonly record struct HeartRateObservation(DateTimeOffset CapturedAt, ushort BeatsPerMinute);