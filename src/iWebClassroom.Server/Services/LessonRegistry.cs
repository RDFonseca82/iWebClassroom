using System.Collections.Concurrent;
using System.Text.Json;
using IWebClassroom.Contracts;
using IWebClassroom.Server.Configuration;
using Microsoft.Extensions.Options;

namespace IWebClassroom.Server.Services;

public sealed class LessonRegistry(
    AgentRegistry agents,
    IOptions<ClassroomOptions> options,
    IWebHostEnvironment environment,
    ILogger<LessonRegistry> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ConcurrentDictionary<Guid, PersistedLesson> _lessons = new();
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly string _filePath = ResolvePath(environment.ContentRootPath, options.Value.DataDirectory, "lessons.json");

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        if (!File.Exists(_filePath))
        {
            return;
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var lessons = await JsonSerializer.DeserializeAsync<List<PersistedLesson>>(stream, JsonOptions, cancellationToken) ?? [];
            foreach (var lesson in lessons.Where(IsActive))
            {
                _lessons[lesson.LessonId] = lesson;
            }
        }
        catch (JsonException exception)
        {
            logger.LogCritical(exception, "The lesson registry at {RegistryPath} is not valid JSON.", _filePath);
            throw;
        }
    }

    public IReadOnlyCollection<LessonSessionSummary> List()
    {
        return _lessons.Values
            .Where(IsActive)
            .OrderBy(item => item.RoomId, StringComparer.OrdinalIgnoreCase)
            .Select(ToSummary)
            .ToArray();
    }

    public bool TryGet(Guid lessonId, out LessonSessionSummary? lesson)
    {
        if (_lessons.TryGetValue(lessonId, out var persisted) && IsActive(persisted))
        {
            lesson = ToSummary(persisted);
            return true;
        }

        lesson = null;
        return false;
    }

    public bool TryGetForAgent(AgentSummary agent, out LessonSessionSummary? lesson)
    {
        var persisted = _lessons.Values.Where(IsActive).FirstOrDefault(item =>
            item.TeacherAgentId == agent.AgentId ||
            (agent.Role == AgentRole.Student && string.Equals(item.RoomId, agent.ClassroomId, StringComparison.OrdinalIgnoreCase)));
        lesson = persisted is null ? null : ToSummary(persisted);
        return lesson is not null;
    }

    public async Task<LessonSessionSummary?> TryStartAsync(
        string roomId,
        AgentSummary teacher,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (_lessons.Values.Any(item =>
                    IsActive(item) &&
                    (string.Equals(item.RoomId, roomId, StringComparison.OrdinalIgnoreCase) ||
                     item.TeacherAgentId == teacher.AgentId)))
            {
                return null;
            }

            var now = DateTimeOffset.UtcNow;
            var lesson = new PersistedLesson(
                Guid.NewGuid(),
                roomId,
                teacher.AgentId,
                teacher.DisplayName,
                now,
                now.Add(duration));
            _lessons[lesson.LessonId] = lesson;
            await PersistAsync(cancellationToken);
            return ToSummary(lesson);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task<LessonSessionSummary?> EndAsync(Guid lessonId, CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (!_lessons.TryRemove(lessonId, out var lesson))
            {
                return null;
            }

            await PersistAsync(cancellationToken);
            return ToSummary(lesson);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task<IReadOnlyCollection<LessonSessionSummary>> EndExpiredAsync(CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            var expired = _lessons.Values.Where(item => !IsActive(item)).ToArray();
            foreach (var lesson in expired)
            {
                _lessons.TryRemove(lesson.LessonId, out _);
            }

            if (expired.Length > 0)
            {
                await PersistAsync(cancellationToken);
            }

            return expired.Select(ToSummary).ToArray();
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public bool IsMember(Guid lessonId, Guid agentId) =>
        TryGet(lessonId, out var lesson) && lesson!.MemberAgentIds.Contains(agentId);

    private LessonSessionSummary ToSummary(PersistedLesson lesson)
    {
        var members = agents.List()
            .Where(agent => agent.AgentId == lesson.TeacherAgentId ||
                            (agent.Role == AgentRole.Student && string.Equals(agent.ClassroomId, lesson.RoomId, StringComparison.OrdinalIgnoreCase)))
            .Select(agent => agent.AgentId)
            .Distinct()
            .ToArray();
        return new LessonSessionSummary(
            lesson.LessonId,
            lesson.RoomId,
            lesson.TeacherAgentId,
            lesson.TeacherDisplayName,
            lesson.StartedAtUtc,
            lesson.ExpiresAtUtc,
            members);
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, _lessons.Values.OrderBy(item => item.LessonId), JsonOptions, cancellationToken);
        }

        File.Move(temporaryPath, _filePath, true);
    }

    private static bool IsActive(PersistedLesson lesson) => lesson.ExpiresAtUtc > DateTimeOffset.UtcNow;

    private static string ResolvePath(string root, string configuredPath, string fileName)
    {
        var directory = Path.IsPathRooted(configuredPath) ? configuredPath : Path.Combine(root, configuredPath);
        return Path.Combine(directory, fileName);
    }

    private sealed record PersistedLesson(
        Guid LessonId,
        string RoomId,
        Guid TeacherAgentId,
        string TeacherDisplayName,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset ExpiresAtUtc);
}
