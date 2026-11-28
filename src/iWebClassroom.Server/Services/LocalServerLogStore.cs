using System.Text.Json;
using System.Text.Json.Serialization;
using IWebClassroom.Contracts;

namespace IWebClassroom.Server.Services;

public sealed class LocalServerLogStore : ILoggerProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string ComponentVersion =
        typeof(LocalServerLogStore).Assembly.GetName().Version?.ToString() ?? "0.0.0";
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly string _cursorPath;
    private LogCursor _cursor;

    public LocalServerLogStore(string dataDirectory)
    {
        _directory = Path.Combine(dataDirectory, "logs");
        _cursorPath = Path.Combine(_directory, "cloud-upload-cursor.json");
        Directory.CreateDirectory(_directory);
        _cursor = LoadCursor();
        DeleteExpiredFiles();
    }

    public ILogger CreateLogger(string categoryName) => new LocalFileLogger(this, categoryName);

    public void Dispose()
    {
    }

    public Task<LocalLogBatch> ReadCloudDiagnosticBatchAsync(int maximumEntries, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var entries = new List<CloudServerLogEntry>(Math.Clamp(maximumEntries, 1, 500));
            var files = Directory.GetFiles(_directory, "server-*.jsonl").OrderBy(Path.GetFileName, StringComparer.Ordinal).ToArray();
            var end = _cursor;
            var cursorAdvanced = false;
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                if (!string.IsNullOrWhiteSpace(_cursor.FileName) && string.CompareOrdinal(fileName, _cursor.FileName) < 0)
                {
                    continue;
                }

                var lineNumber = 0L;
                foreach (var line in File.ReadLines(file))
                {
                    lineNumber++;
                    if (string.Equals(fileName, _cursor.FileName, StringComparison.Ordinal) && lineNumber <= _cursor.LineNumber)
                    {
                        continue;
                    }

                    end = new LogCursor(fileName, lineNumber);
                    cursorAdvanced = true;
                    try
                    {
                        var entry = JsonSerializer.Deserialize<CloudServerLogEntry>(line, JsonOptions);
                        if (entry is not null && IsCloudDiagnostic(entry))
                        {
                            entries.Add(entry);
                        }
                    }
                    catch (JsonException)
                    {
                        // Skip a partially written or manually damaged line and advance the cursor.
                    }

                    if (entries.Count >= maximumEntries)
                    {
                        return Task.FromResult(new LocalLogBatch(entries, end, cursorAdvanced));
                    }
                }
            }

            return Task.FromResult(new LocalLogBatch(entries, end, cursorAdvanced));
        }
    }

    private static bool IsCloudDiagnostic(CloudServerLogEntry entry)
    {
        if (string.Equals(entry.Level, nameof(LogLevel.Error), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entry.Level, nameof(LogLevel.Critical), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.Equals(entry.Level, nameof(LogLevel.Warning), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(entry.Exception))
        {
            return true;
        }

        var code = entry.EventCode ?? string.Empty;
        return new[] { "failed", "error", "unavailable", "degraded", "retry", "timeout" }
            .Any(marker => code.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    public async Task AcknowledgeAsync(LogCursor cursor, CancellationToken cancellationToken)
    {
        LogCursor snapshot;
        lock (_gate)
        {
            _cursor = cursor;
            snapshot = _cursor;
        }

        var temporaryPath = $"{_cursorPath}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(snapshot, JsonOptions), cancellationToken);
        File.Move(temporaryPath, _cursorPath, true);
    }

    public void AppendAgentEntries(AgentSummary agent, IReadOnlyCollection<AgentLogEntry> entries)
    {
        foreach (var source in entries)
        {
            var entry = new CloudServerLogEntry(
                source.EventId,
                source.OccurredAtUtc,
                source.Level,
                source.ComponentVersion,
                $"Agent.{source.Category}",
                source.EventCode,
                source.Message,
                source.Exception,
                agent.AgentId,
                agent.MachineId,
                agent.DisplayName);
            AppendEntry(entry);
        }
    }

    private void Append(string category, LogLevel level, EventId eventId, string message, Exception? exception)
    {
        var entry = new CloudServerLogEntry(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            level.ToString(),
            ComponentVersion,
            category.Length > 200 ? category[..200] : category,
            eventId.Name ?? (eventId.Id == 0 ? null : eventId.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            message.Length > 4000 ? message[..4000] : message,
            exception?.ToString() is { } details ? details[..Math.Min(details.Length, 12000)] : null);
        AppendEntry(entry);
    }

    private void AppendEntry(CloudServerLogEntry entry)
    {
        // External agent events are appended to today's server file so an existing upload cursor
        // can never skip a late event whose original timestamp belongs to an older day.
        var path = Path.Combine(_directory, $"server-{DateTimeOffset.UtcNow:yyyyMMdd}.jsonl");
        var line = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;
        lock (_gate)
        {
            try
            {
                File.AppendAllText(path, line);
            }
            catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException)
            {
                // Logging must never terminate the Windows service when the disk is unavailable.
            }
        }
    }

    private LogCursor LoadCursor()
    {
        try
        {
            return File.Exists(_cursorPath)
                ? JsonSerializer.Deserialize<LogCursor>(File.ReadAllText(_cursorPath), JsonOptions) ?? LogCursor.Empty
                : LogCursor.Empty;
        }
        catch (Exception)
        {
            return LogCursor.Empty;
        }
    }

    private void DeleteExpiredFiles()
    {
        var cutoff = DateTime.UtcNow.AddDays(-30);
        foreach (var file in Directory.GetFiles(_directory, "server-*.jsonl"))
        {
            if (File.GetLastWriteTimeUtc(file) < cutoff)
            {
                try { File.Delete(file); } catch (IOException) { }
            }
        }
    }

    private sealed class LocalFileLogger(LocalServerLogStore store, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information && logLevel != LogLevel.None;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                store.Append(category, logLevel, eventId, formatter(state, exception), exception);
            }
        }
    }
}

public sealed record CloudServerLogEntry(
    [property: JsonPropertyName("event_id")] Guid EventId,
    [property: JsonPropertyName("occurred_at_utc")] DateTimeOffset OccurredAtUtc,
    [property: JsonPropertyName("level")] string Level,
    [property: JsonPropertyName("component_version")] string? ComponentVersion,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("event_code")] string? EventCode,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("exception")] string? Exception,
    [property: JsonPropertyName("agent_id")] Guid? AgentId = null,
    [property: JsonPropertyName("machine_id")] string? MachineId = null,
    [property: JsonPropertyName("computer_name")] string? ComputerName = null);

public sealed record AgentLogEntry(
    [property: JsonPropertyName("event_id")] Guid EventId,
    [property: JsonPropertyName("occurred_at_utc")] DateTimeOffset OccurredAtUtc,
    [property: JsonPropertyName("level")] string Level,
    [property: JsonPropertyName("component_version")] string? ComponentVersion,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("event_code")] string? EventCode,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("exception")] string? Exception);

public sealed record AgentLogUploadRequest(
    [property: JsonPropertyName("entries")] IReadOnlyCollection<AgentLogEntry> Entries);

public sealed record CloudServerLogUploadRequest(
    [property: JsonPropertyName("entries")] IReadOnlyCollection<CloudServerLogEntry> Entries);
public sealed record LogCursor(string FileName, long LineNumber)
{
    public static LogCursor Empty { get; } = new(string.Empty, 0);
}
public sealed record LocalLogBatch(
    IReadOnlyCollection<CloudServerLogEntry> Entries,
    LogCursor Cursor,
    bool CursorAdvanced);
