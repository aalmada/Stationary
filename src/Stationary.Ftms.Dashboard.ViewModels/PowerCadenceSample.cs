namespace Stationary.Ftms.Dashboard;

public readonly record struct PowerCadenceSample(DateTimeOffset CapturedAt, double PowerWatts, double CadenceRpm);