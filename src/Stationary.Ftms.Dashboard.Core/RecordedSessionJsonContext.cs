using System.Text.Json.Serialization;

namespace Stationary.Ftms.Dashboard.Core;

[JsonSerializable(typeof(RecordedSession))]
public sealed partial class RecordedSessionJsonContext : JsonSerializerContext;
