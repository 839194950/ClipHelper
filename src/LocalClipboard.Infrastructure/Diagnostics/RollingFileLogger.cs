using System.Text.Json;

namespace LocalClipboard.Infrastructure.Diagnostics;

public sealed class RollingFileLogger
{
    private readonly string logDirectory;
    private readonly long maximumBytes;
    private readonly SemaphoreSlim writeGate = new(1, 1);

    public RollingFileLogger(string logDirectory, long maximumBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);

        this.logDirectory = Path.GetFullPath(logDirectory);
        this.maximumBytes = maximumBytes;
        Directory.CreateDirectory(this.logDirectory);
    }

    public async Task WriteAsync(string eventName, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentNullException.ThrowIfNull(exception);

        await writeGate.WaitAsync(cancellationToken);
        try
        {
            DeleteOldestUntilAtMost(maximumBytes, keepNewest: false, cancellationToken);

            DateTimeOffset timestamp = DateTimeOffset.UtcNow;
            string path = Path.Combine(logDirectory, $"localclipboard-{timestamp:yyyyMMdd}.log");
            string line = JsonSerializer.Serialize(new LogEntry(
                timestamp,
                eventName,
                exception.GetType().FullName ?? exception.GetType().Name,
                exception.StackTrace,
                exception.HResult));
            await File.AppendAllTextAsync(path, line + Environment.NewLine, cancellationToken);

            long postAppendLimit = maximumBytes > long.MaxValue / 2 ? long.MaxValue : maximumBytes * 2;
            DeleteOldestUntilAtMost(postAppendLimit, keepNewest: true, cancellationToken);
        }
        finally
        {
            writeGate.Release();
        }
    }

    private void DeleteOldestUntilAtMost(long limit, bool keepNewest, CancellationToken cancellationToken)
    {
        List<FileInfo> files = Directory
            .EnumerateFiles(logDirectory, "localclipboard-*.log", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderBy(file => file.LastWriteTimeUtc)
            .ThenBy(file => file.Name, StringComparer.Ordinal)
            .ToList();
        long total = files.Sum(file => file.Length);
        FileInfo? newest = keepNewest ? files.LastOrDefault() : null;

        foreach (FileInfo file in files)
        {
            if (total <= limit) break;
            cancellationToken.ThrowIfCancellationRequested();
            if (newest is not null && string.Equals(file.FullName, newest.FullName, StringComparison.OrdinalIgnoreCase))
                continue;

            long length = file.Length;
            file.Delete();
            total -= length;
        }
    }

    private sealed record LogEntry(
        DateTimeOffset Timestamp,
        string EventName,
        string ExceptionType,
        string? StackTrace,
        int HResult);
}
