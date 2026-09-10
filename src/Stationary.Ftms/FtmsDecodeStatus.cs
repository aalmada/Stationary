namespace Stationary.Ftms;

public enum FtmsDecodeStatus : byte
{
    Success,
    InsufficientData,
    InvalidFlags,
    TrailingData,
}