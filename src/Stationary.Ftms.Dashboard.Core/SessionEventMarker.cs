namespace Stationary.Ftms.Dashboard.Core;

public enum SessionEventType
{
    Start,
    Pause,
    Resume,
    Stop,
    Lap,
    Disconnect,
}

public readonly record struct SessionEventMarker(
    DateTimeOffset Timestamp,
    SessionEventType Type,
    string Label);