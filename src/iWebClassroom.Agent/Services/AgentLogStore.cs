using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using IWebClassroom.Agent.Configuration;
using IWebClassroom.Contracts;

namespace IWebClassroom.Agent.Services;

public sealed class AgentLogStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly object _gate = new();
    private readonly SemaphoreSlim _uploadGate = new(1, 1);
    private readonly string _directory;
    private readonly string _cursorPath;
    private AgentLogCursor _cursor;

    public static AgentLogStore Current { get; } = new();

    private AgentLogStore()
    {
        _directory = CreateLogDirectory();
        _cursorPath = Path.Combine(_directory, "server-upload-cursor.json");
        _cursor = LoadCursor();
        DeleteExpiredFiles();
    }

    public void Information(string category, string message, string? eventCode = null) =>
        Append("Information", category, message, null, eventCode);

    public void Warning(string category, string message, Exception? exception = null, string? eventCode = null) =>
        Append("Warning", category, message, exception, eventCode);

    public void Error(string category, string message, Exception exception, string? eventCode = null) =>
        Append("Error", category, message, exception, eventCode);

    public void Critical(string category, string message, Exception? exception = null, string? eventCode = null) =>
        Append("Critical", category, message, exception, eventCode);

    public async Task UploadAsync(AgentSettings settings, CancellationToken cancellationToken)
    {
        if (settings.AgentId == Guid.Empty || string.IsNullOrWhiteSpace(settings.AgentToken) ||
            !Uri.TryCreate(settings.ServerUrl, UriKind.Absolute, out var serverUri) ||
            !await _uploadGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            for (var page = 0; page < 3; page++)
            {
                var batch = ReadBatch(200);
                if (batch.Entries.Count == 0)
                {
                    return;
                }

                using var client = new HttpClient { BaseAddress = serverUri, Timeout = TimeSpan.FromSeconds(15) };
                client.DefaultRequestHeaders.Add(HubProtocol.AgentIdHeader, settings.AgentId.ToString());
                client.DefaultRequestHeaders.Add(HubProtocol.AgentTokenHeader, settings.AgentToken);
                client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(CultureInfo.CurrentUICulture.Name));
                using var response = await client.PostAsJsonAsync(
                    "/api/v1/agent/logs",
                    new AgentLogUploadRequest(batch.Entries),
                    JsonOptions,
                    cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return;
                }

                await AcknowledgeAsync(batch.Cursor, cancellationToken);
                if (batch.Entries.Count < 200)
                {
                    return;
                }
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or TaskCanceledException)
        {
            // Keep the cursor unchanged. The same batch is retried after the next interval.
        }
        finally
        {
            _uploadGate.Release();
        }
    }

    private void Append(string level, string category, string message, Exception? exception, string? eventCode)
    {
        var entry = new AgentLogEntry(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            level,
            Limit(category, 200),
            string.IsNullOrWhiteSpace(eventCode) ? null : Limit(eventCode, 100),
            Limit(message, 4000),
            exception is null ? null : Limit(exception.ToString(), 12000));
        var line = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;
        var path = Path.Combine(_directory, $"agent-{DateTimeOffset.UtcNow:yyyyMMdd}.jsonl");
        lock (_gate)
        {
            try
            {
                File.AppendAllText(path, line);
            }
            catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException)
            {
                // Logging must never terminate the agent when storage is unavailable.
            }
        }
    }

    private AgentLogBatch ReadBatch(int maximumEntries)
    {
        lock (_gate)
        {
            var entries = new List<AgentLogEntry>(maximumEntries);
            var end = _cursor;
            foreach (var file in Directory.GetFiles(_directory, "agent-*.jsonl")
                         .OrderBy(Path.GetFileName, StringComparer.Ordinal))
            {
                var fileName = Path.GetFileName(file);
                if (!string.IsNullOrWhiteSpace(_cursor.FileName) &&
                    string.CompareOrdinal(fileName, _cursor.FileName) < 0)
                {
                    continue;
                }

                var lineNumber = 0L;
                foreach (var line in File.ReadLines(file))
                {
                    lineNumber++;
                    if (string.Equals(fileName, _cursor.FileName, StringComparison.Ordinal) &&
                        lineNumber <= _cursor.LineNumber)
                    {
                        continue;
                    }

                    end = new AgentLogCursor(fileName, lineNumber);
                    try
                    {
                        var entry = JsonSerializer.Deserialize<AgentLogEntry>(line, JsonOptions);
                        if (entry is not null)
                        {
                            entries.Add(entry);
                        }
                    }
                    catch (JsonException)
                    {
                        // Advance past a partially written or manually damaged entry.
                    }

                    if (entries.Count >= maximumEntries)
                    {
                        return new AgentLogBatch(entries, end);
                    }
                }
            }

            return new AgentLogBatch(entries, end);
        }
    }

    private async Task AcknowledgeAsync(AgentLogCursor cursor, CancellationToken cancellationToken)
    {
        var temporaryPath = $"{_cursorPath}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(cursor, JsonOptions), cancellationToken);
        File.Move(temporaryPath, _cursorPath, true);
        lock (_gate)
        {
            _cursor = cursor;
        }
    }

    private AgentLogCursor LoadCursor()
    {
        try
        {
            return File.Exists(_cursorPath)
                ? JsonSerializer.Deserialize<AgentLogCursor>(File.ReadAllText(_cursorPath), JsonOptions) ?? AgentLogCursor.Empty
                : AgentLogCursor.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return AgentLogCursor.Empty;
        }
    }

    private void DeleteExpiredFiles()
    {
        var cutoff = DateTime.UtcNow.AddDays(-30);
        foreach (var file in Directory.GetFiles(_directory, "agent-*.jsonl"))
        {
            if (File.GetLastWriteTimeUtc(file) < cutoff)
            {
                try { File.Delete(file); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private static string CreateLogDirectory()
    {
        var primary = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "iWebClassroom", "Agent", "logs");
        try
        {
            Directory.CreateDirectory(primary);
            return primary;
        }
        catch (UnauthorizedAccessException)
        {
            var fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "iWebClassroom", "Agent", "logs");
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }

    private static string Limit(string? value, int maximum) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Length <= maximum ? value : value[..maximum];

    private sealed record AgentLogEntry(
        [property: JsonPropertyName("event_id")] Guid EventId,
        [property: JsonPropertyName("occurred_at_utc")] DateTimeOffset OccurredAtUtc,
        [property: JsonPropertyName("level")] string Level,
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("event_code")] string? EventCode,
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("exception")] string? Exception);

    private sealed record AgentLogUploadRequest(
        [property: JsonPropertyName("entries")] IReadOnlyCollection<AgentLogEntry> Entries);

    private sealed record AgentLogCursor(string FileName, long LineNumber)
    {
        public static AgentLogCursor Empty { get; } = new(string.Empty, 0);
    }

    private sealed record AgentLogBatch(IReadOnlyCollection<AgentLogEntry> Entries, AgentLogCursor Cursor);
}
