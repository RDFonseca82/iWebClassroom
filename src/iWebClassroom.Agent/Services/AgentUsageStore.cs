using System.Net.Http.Json;
using System.Text.Json;
using IWebClassroom.Agent.Configuration;
using IWebClassroom.Contracts;

namespace IWebClassroom.Agent.Services;

public sealed class AgentUsageStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;
    private AgentUsageDocument _document;

    public AgentUsageStore()
    {
        _path = ResolvePath();
        _document = Load();
    }

    public async Task RecordStartAsync(CancellationToken cancellationToken = default)
    {
        await UpdateTodayAsync(day => day with { ProcessStarts = day.ProcessStarts + 1 }, cancellationToken);
    }

    public async Task RecordConnectedMinuteAsync(bool connected, CancellationToken cancellationToken = default)
    {
        if (!connected)
        {
            return;
        }
        await UpdateTodayAsync(day => day with { ConnectedSeconds = day.ConnectedSeconds + 60 }, cancellationToken);
    }

    public async Task UploadAsync(AgentSettings settings, CancellationToken cancellationToken)
    {
        if (settings.AgentId == Guid.Empty || string.IsNullOrWhiteSpace(settings.AgentToken) ||
            !Uri.TryCreate(settings.ServerUrl, UriKind.Absolute, out var serverUri))
        {
            return;
        }

        AgentDailyUsage[] snapshots;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var cutoff = DateOnly.FromDateTime(DateTime.Today.AddDays(-7));
            snapshots = _document.Days.Values
                .Where(day => DateOnly.TryParse(day.Date, out var date) && date >= cutoff)
                .OrderBy(day => day.Date, StringComparer.Ordinal)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }

        using var client = new HttpClient { BaseAddress = serverUri, Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.Add(HubProtocol.AgentIdHeader, settings.AgentId.ToString());
        client.DefaultRequestHeaders.Add(HubProtocol.AgentTokenHeader, settings.AgentToken);
        foreach (var snapshot in snapshots)
        {
            using var response = await client.PostAsJsonAsync(
                "/api/v1/agent/statistics",
                new AgentUsageSnapshot(snapshot.Date, snapshot.ProcessStarts, snapshot.ConnectedSeconds),
                JsonOptions,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }
        }
    }

    private async Task UpdateTodayAsync(Func<AgentDailyUsage, AgentDailyUsage> update, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var date = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
            var current = _document.Days.GetValueOrDefault(date) ?? new AgentDailyUsage(date, 0, 0);
            _document.Days[date] = update(current);
            var cutoff = DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
            foreach (var old in _document.Days.Keys.Where(key => DateOnly.TryParse(key, out var day) && day < cutoff).ToArray())
            {
                _document.Days.Remove(old);
            }
            await PersistUnsafeAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private AgentUsageDocument Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<AgentUsageDocument>(File.ReadAllText(_path), JsonOptions) ?? AgentUsageDocument.Empty
                : AgentUsageDocument.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return AgentUsageDocument.Empty;
        }
    }

    private async Task PersistUnsafeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(_document, JsonOptions), cancellationToken);
        File.Move(temporary, _path, true);
    }

    private static string ResolvePath()
    {
        var common = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "iWebClassroom", "Agent", "usage-statistics.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(common)!);
            return common;
        }
        catch (UnauthorizedAccessException)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "iWebClassroom", "Agent", "usage-statistics.json");
        }
    }

    private sealed record AgentUsageDocument(Dictionary<string, AgentDailyUsage> Days)
    {
        public static AgentUsageDocument Empty => new(new Dictionary<string, AgentDailyUsage>(StringComparer.Ordinal));
    }

    private sealed record AgentDailyUsage(string Date, int ProcessStarts, long ConnectedSeconds);
}
