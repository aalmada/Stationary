using System.Text.Json.Serialization;

namespace Stationary.Ftms.Dashboard.Core;

[JsonSerializable(typeof(RecordedSession))]
[JsonSerializable(typeof(HeartRateTrainingProgressDocument))]
[JsonSerializable(typeof(RideHistoryDocument))]
public sealed partial class RecordedSessionJsonContext : JsonSerializerContext;