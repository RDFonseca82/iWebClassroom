using System.Text.Json;
using IWebClassroom.Contracts;
using IWebClassroom.Server.Configuration;
using Microsoft.Extensions.Options;

namespace IWebClassroom.Server.Services;

public sealed class UsageStatisticsStore(
    IOptions<ClassroomOptions> options,
    IWebHostEnvironment environment,
    ILogger<UsageStatisticsStore> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path = ResolvePath(environment.ContentRootPath, options.Value.DataDirectory);
    private UsageStatisticsDocument _document = UsageStatisticsDocument.Empty;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        if (!File.Exists(_path))
        {
            return;
        }
        try
        {
            await using var stream = File.OpenRead(_path);
            _document = await JsonSerializer.DeserializeAsync<UsageStatisticsDocument>(stream, JsonOptions, cancellationToken)
                        ?? UsageStatisticsDocument.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogError(exception, "The local usage statistics file is invalid and will be recreated.");
            _document = UsageStatisticsDocument.Empty;
        }
    }

    public Task RecordLessonStartedAsync(LessonSessionSummary lesson, CancellationToken cancellationToken) =>
        UpdateDayAsync(LocalDate(lesson.StartedAtUtc), day =>
        {
            day.LessonsCreated++;
            if (lesson.IsTemporary) day.TemporaryLessons++; else day.FixedLessons++;
            day.UniqueTeacherIds.Add(lesson.TeacherId);
            foreach (var agentId in lesson.MemberAgentIds) day.UniqueAgentIds.Add(agentId);
            var teacher = day.Teachers.GetValueOrDefault(lesson.TeacherId) ?? new TeacherUsage(lesson.TeacherId, lesson.TeacherDisplayName);
            teacher.Name = lesson.TeacherDisplayName;
            teacher.Lessons++;
            day.Teachers[lesson.TeacherId] = teacher;
        }, cancellationToken);

    public Task RecordLessonEndedAsync(LessonSessionSummary lesson, CancellationToken cancellationToken) =>
        UpdateDayAsync(LocalDate(lesson.StartedAtUtc), day =>
        {
            day.CompletedLessons++;
            var minutes = Math.Clamp((long)Math.Ceiling((DateTimeOffset.UtcNow - lesson.StartedAtUtc).TotalMinutes), 0, 1440);
            day.TotalLessonMinutes += minutes;
            var teacher = day.Teachers.GetValueOrDefault(lesson.TeacherId) ?? new TeacherUsage(lesson.TeacherId, lesson.TeacherDisplayName);
            teacher.Name = lesson.TeacherDisplayName;
            teacher.Minutes += minutes;
            day.Teachers[lesson.TeacherId] = teacher;
        }, cancellationToken);

    public Task RecordStudentJoinedAsync(LessonSessionSummary lesson, Guid agentId, CancellationToken cancellationToken) =>
        UpdateDayAsync(DateOnly.FromDateTime(DateTime.Today), day =>
        {
            day.StudentJoins++;
            day.UniqueAgentIds.Add(agentId);
        }, cancellationToken);

    public Task RecordCommandAsync(ClassroomCommandKind kind, CancellationToken cancellationToken) =>
        UpdateDayAsync(DateOnly.FromDateTime(DateTime.Today), day =>
        {
            switch (kind)
            {
                case ClassroomCommandKind.LaunchApplication: day.ApplicationsLaunched++; break;
                case ClassroomCommandKind.StartScreenShare: day.ScreenShares++; break;
                case ClassroomCommandKind.StartBlackout: day.Blackouts++; break;
                case ClassroomCommandKind.StartStudentView: day.StudentViews++; break;
                case ClassroomCommandKind.ShowMedia: day.MediaPresentations++; break;
            }
        }, cancellationToken);

    public async Task SampleOnlineAgentsAsync(IReadOnlyCollection<AgentSummary> agents, CancellationToken cancellationToken)
    {
        var online = agents.Where(agent => agent.IsOnline).ToArray();
        await UpdateDayAsync(DateOnly.FromDateTime(DateTime.Today), day =>
        {
            day.PeakOnlineComputers = Math.Max(day.PeakOnlineComputers, online.Length);
            foreach (var agent in online) day.UniqueAgentIds.Add(agent.AgentId);
        }, cancellationToken, persistOnlyWhenChanged: true);
    }

    public Task RecordAgentSnapshotAsync(Guid agentId, AgentUsageSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParse(snapshot.Date, out var date))
        {
            return Task.CompletedTask;
        }
        return UpdateDayAsync(date, day =>
        {
            var key = $"{snapshot.Date}:{agentId:D}";
            var previous = _document.AgentSnapshots.GetValueOrDefault(key) ?? new AgentSnapshotCursor(0, 0);
            day.AgentStarts += Math.Max(0, snapshot.ProcessStarts - previous.ProcessStarts);
            day.AgentOnlineSeconds += Math.Max(0, snapshot.ConnectedSeconds - previous.ConnectedSeconds);
            day.UniqueAgentIds.Add(agentId);
            _document.AgentSnapshots[key] = new AgentSnapshotCursor(
                Math.Max(previous.ProcessStarts, snapshot.ProcessStarts),
                Math.Max(previous.ConnectedSeconds, snapshot.ConnectedSeconds));
        }, cancellationToken);
    }

    public async Task<IReadOnlyCollection<CloudDailyUsageStatistics>> CompletedDaysAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            return _document.Days.Values
                .Where(day => DateOnly.TryParse(day.Date, out var date) && date < today && date >= today.AddDays(-62))
                .OrderBy(day => day.Date, StringComparer.Ordinal)
                .Select(day => new CloudDailyUsageStatistics(
                    day.Date,
                    day.LessonsCreated,
                    day.FixedLessons,
                    day.TemporaryLessons,
                    day.CompletedLessons,
                    day.TotalLessonMinutes,
                    day.StudentJoins,
                    day.UniqueTeacherIds.Count,
                    day.UniqueAgentIds.Count,
                    day.PeakOnlineComputers,
                    day.AgentStarts,
                    day.AgentOnlineSeconds,
                    day.ApplicationsLaunched,
                    day.ScreenShares,
                    day.Blackouts,
                    day.StudentViews,
                    day.MediaPresentations,
                    day.Teachers.Values.OrderByDescending(item => item.Lessons)
                        .Select(item => new CloudTeacherUsageStatistics(item.TeacherId, item.Name, item.Lessons, item.Minutes))
                        .ToArray()))
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task UpdateDayAsync(
        DateOnly date,
        Action<DailyUsage> update,
        CancellationToken cancellationToken,
        bool persistOnlyWhenChanged = false)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var key = date.ToString("yyyy-MM-dd");
            var day = _document.Days.GetValueOrDefault(key) ?? new DailyUsage { Date = key };
            var before = persistOnlyWhenChanged ? JsonSerializer.Serialize(day, JsonOptions) : string.Empty;
            update(day);
            _document.Days[key] = day;
            RemoveExpired(date);
            if (!persistOnlyWhenChanged || !string.Equals(before, JsonSerializer.Serialize(day, JsonOptions), StringComparison.Ordinal))
            {
                try
                {
                    await PersistUnsafeAsync(cancellationToken);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(exception, "Usage statistics were updated in memory but could not be persisted.");
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void RemoveExpired(DateOnly reference)
    {
        var cutoff = reference.AddDays(-400);
        foreach (var key in _document.Days.Keys.Where(key => DateOnly.TryParse(key, out var date) && date < cutoff).ToArray())
        {
            _document.Days.Remove(key);
        }
        foreach (var key in _document.AgentSnapshots.Keys.Where(key =>
                     key.Length >= 10 && DateOnly.TryParse(key[..10], out var date) && date < cutoff).ToArray())
        {
            _document.AgentSnapshots.Remove(key);
        }
    }

    private async Task PersistUnsafeAsync(CancellationToken cancellationToken)
    {
        var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, _document, JsonOptions, cancellationToken);
        }
        File.Move(temporary, _path, true);
    }

    private static DateOnly LocalDate(DateTimeOffset timestamp) => DateOnly.FromDateTime(timestamp.LocalDateTime);

    private static string ResolvePath(string root, string configured)
    {
        var directory = Path.IsPathRooted(configured) ? configured : Path.Combine(root, configured);
        return Path.Combine(directory, "usage-statistics.json");
    }

    public sealed class UsageStatisticsDocument
    {
        public Dictionary<string, DailyUsage> Days { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, AgentSnapshotCursor> AgentSnapshots { get; set; } = new(StringComparer.Ordinal);
        public static UsageStatisticsDocument Empty => new();
    }

    public sealed class DailyUsage
    {
        public string Date { get; set; } = string.Empty;
        public long LessonsCreated { get; set; }
        public long FixedLessons { get; set; }
        public long TemporaryLessons { get; set; }
        public long CompletedLessons { get; set; }
        public long TotalLessonMinutes { get; set; }
        public long StudentJoins { get; set; }
        public HashSet<string> UniqueTeacherIds { get; set; } = new(StringComparer.Ordinal);
        public HashSet<Guid> UniqueAgentIds { get; set; } = [];
        public int PeakOnlineComputers { get; set; }
        public long AgentStarts { get; set; }
        public long AgentOnlineSeconds { get; set; }
        public long ApplicationsLaunched { get; set; }
        public long ScreenShares { get; set; }
        public long Blackouts { get; set; }
        public long StudentViews { get; set; }
        public long MediaPresentations { get; set; }
        public Dictionary<string, TeacherUsage> Teachers { get; set; } = new(StringComparer.Ordinal);
    }

    public sealed class TeacherUsage(string teacherId, string name)
    {
        public string TeacherId { get; set; } = teacherId;
        public string Name { get; set; } = name;
        public long Lessons { get; set; }
        public long Minutes { get; set; }
    }

    public sealed record AgentSnapshotCursor(int ProcessStarts, long ConnectedSeconds);
}
