using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Stationary.Ftms.Dashboard.Core;

public static class RecordedSessionArchiveStorage
{
    private const int FileBufferSize = 64 * 1024;
    private const string CsvHeader = "TimestampUtc,RecordType,PowerWatts,SpeedKilometersPerHour,CadenceRpm,HeartRateBeatsPerMinute,Event";

    public static async Task WriteCsvAsync(string path, RecordedSession archive, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(archive);

        await using var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = FileBufferSize,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
        });
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), FileBufferSize, leaveOpen: true);
        await WriteLineAsync(writer, CsvHeader, cancellationToken).ConfigureAwait(false);

        // Telemetry and events are merged chronologically so training targets sit next to the samples they affected.
        var row = new StringBuilder(128);
        var sampleIndex = 0;
        var eventIndex = 0;
        while (sampleIndex < archive.Samples.Length || eventIndex < archive.Events.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            row.Clear();
            if (eventIndex >= archive.Events.Length
                || (sampleIndex < archive.Samples.Length && archive.Samples[sampleIndex].CapturedAt <= archive.Events[eventIndex].Timestamp))
            {
                var sample = archive.Samples[sampleIndex++];
                row.Append(sample.CapturedAt.ToString("O", CultureInfo.InvariantCulture))
                    .Append(",telemetry,")
                    .Append(FormatCsvNumber(sample.PowerWatts)).Append(',')
                    .Append(FormatCsvNumber(sample.SpeedKilometersPerHour)).Append(',')
                    .Append(FormatCsvNumber(sample.CadenceRpm)).Append(',')
                    .Append(FormatCsvNumber(sample.HeartRateBeatsPerMinute)).Append(',');
            }
            else
            {
                var marker = archive.Events[eventIndex++];
                row.Append(marker.Timestamp.ToString("O", CultureInfo.InvariantCulture))
                    .Append(",event,,,,,")
                    .Append(marker.Type).Append(':')
                    .Append(marker.Label.Replace(',', ';'));
            }

            await WriteLineAsync(writer, row.ToString(), cancellationToken).ConfigureAwait(false);
        }

        await WriteLineAsync(writer, string.Empty, cancellationToken).ConfigureAwait(false);
        await WriteLineAsync(writer, "Metric,Value", cancellationToken).ConfigureAwait(false);
        await WriteLineAsync(writer, $"Distance metres,{archive.Summary.DistanceMeters.ToString("F1", CultureInfo.InvariantCulture)}", cancellationToken).ConfigureAwait(false);
        await WriteLineAsync(writer, $"Elapsed seconds,{archive.Summary.Elapsed.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)}", cancellationToken).ConfigureAwait(false);
        await WriteLineAsync(writer, $"Average power watts,{archive.Summary.AveragePowerWatts.ToString("F1", CultureInfo.InvariantCulture)}", cancellationToken).ConfigureAwait(false);
        await WriteLineAsync(writer, $"Maximum power watts,{archive.Summary.MaximumPowerWatts.ToString("F1", CultureInfo.InvariantCulture)}", cancellationToken).ConfigureAwait(false);
        await WriteLineAsync(writer, $"Work kilojoules,{archive.Summary.CyclingMetrics.TotalWorkKilojoules.ToString("F1", CultureInfo.InvariantCulture)}", cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task WriteJsonAsync(string path, RecordedSession archive, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(archive);

        await using var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = FileBufferSize,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
        });
        await JsonSerializer.SerializeAsync(
            stream,
            archive,
            RecordedSessionJsonContext.Default.RecordedSession,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<RecordedSession?> ReadJsonAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            BufferSize = FileBufferSize,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
        });
        return await JsonSerializer.DeserializeAsync(
            stream,
            RecordedSessionJsonContext.Default.RecordedSession,
            cancellationToken).ConfigureAwait(false);
    }

    private static string FormatCsvNumber(double? value) =>
        value?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty;

    private static Task WriteLineAsync(StreamWriter writer, string value, CancellationToken cancellationToken) =>
        writer.WriteLineAsync(value.AsMemory(), cancellationToken);
}