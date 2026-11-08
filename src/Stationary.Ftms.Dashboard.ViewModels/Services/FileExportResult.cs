namespace Stationary.Ftms.Dashboard.Services;

public sealed record FileExportResult
{
    private FileExportResult(string? destination) => Destination = destination;

    public string? Destination { get; }

    public static FileExportResult Cancelled { get; } = new(destination: null);

    public static FileExportResult Saved(string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        return new(destination);
    }
}