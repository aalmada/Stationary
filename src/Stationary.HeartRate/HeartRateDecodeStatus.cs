namespace Stationary.HeartRate;

public enum HeartRateDecodeStatus : byte
{
    Success,
    InsufficientData,
    TrailingData,
}