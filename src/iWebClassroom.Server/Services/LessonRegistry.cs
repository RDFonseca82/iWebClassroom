using System.Collections.Concurrent;
using System.Text.Json;
using IWebClassroom.Contracts;
using IWebClassroom.Server.Configuration;
using IWebClassroom.Server.Security;
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

    public bool TryGetByJoinCode(string joinCode, out LessonSessionSummary? lesson)
    {
        var persisted = _lessons.Values.FirstOrDefault(item =>
            IsActive(item) && item.IsTemporary &&
            string.Equals(item.JoinCode, joinCode, StringComparison.Ordinal));
        lesson = persisted is null ? null : ToSummary(persisted);
        return lesson is not null;
    }

    public bool TryGetForAgent(AgentSummary agent, out LessonSessionSummary? lesson)
    {
        var persisted = _lessons.Values.Where(IsActive).FirstOrDefault(item =>
            item.TeacherAgentId == agent.AgentId ||
            (agent.Role == AgentRole.Student &&
             ((item.MemberAgentIds?.Contains(agent.AgentId) ?? false) ||
              (!item.IsTemporary && !agent.IsShared && string.Equals(item.RoomId, agent.ClassroomId, StringComparison.OrdinalIgnoreCase)))));
        lesson = persisted is null ? null : ToSummary(persisted);
        return lesson is not null;
    }

    public async Task<LessonSessionSummary?> TryStartAsync(
        string roomId,
        AgentSummary teacher,
        TeacherIdentity teacherIdentity,
        bool isTemporary,
        string? roomName,
        TimeSpan duration,
        CancellationToken cancellationToken,
        Guid? lessonId = null,
        string? requestedJoinCode = null,
        bool isCloudRouted = false)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (_lessons.Values.Any(item =>
                    IsActive(item) &&
                    (string.Equals(item.RoomId, roomId, StringComparison.OrdinalIgnoreCase) ||
                     item.TeacherAgentId == teacher.AgentId ||
                     string.Equals(item.TeacherId, teacherIdentity.TeacherId, StringComparison.Ordinal))))
            {
                return null;
            }

            var now = DateTimeOffset.UtcNow;
            var joinCode = isTemporary
                ? string.IsNullOrWhiteSpace(requestedJoinCode) ? CreateUniqueJoinCode() : requestedJoinCode.Trim()
                : null;
            if (joinCode is { Length: not 6 } || (joinCode is not null && joinCode.Any(character => !char.IsAsciiDigit(character))))
            {
                throw new ArgumentException("A temporary room code must contain exactly six digits.", nameof(requestedJoinCode));
            }
            var pin = joinCode is null ? (Salt: (string?)null, Hash: (string?)null) : PinHasher.Create(joinCode);
            var members = agents.List()
                .Where(agent => agent.AgentId == teacher.AgentId ||
                                (!isTemporary && agent.Role == AgentRole.Student && !agent.IsShared &&
                                 string.Equals(agent.ClassroomId, roomId, StringComparison.OrdinalIgnoreCase)))
                .Select(agent => agent.AgentId)
                .Distinct()
                .ToArray();
            var lesson = new PersistedLesson(
                lessonId ?? Guid.NewGuid(),
                roomId,
                teacher.AgentId,
                teacherIdentity.DisplayName,
                now,
                now.Add(duration),
                teacherIdentity.TeacherId,
                string.IsNullOrWhiteSpace(roomName) ? roomId : roomName.Trim(),
                isTemporary,
                joinCode,
                pin.Salt,
                pin.Hash,
                members,
                null,
                isCloudRouted);
            _lessons[lesson.LessonId] = lesson;
            try
            {
                await PersistAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A storage incident must not prevent a teacher from starting an otherwise valid
                // local lesson. Keep it in memory and surface the durability problem in the logs.
                logger.LogError(exception, "Lesson {LessonId} started in memory but could not be saved to {RegistryPath}.",
                    lesson.LessonId, _filePath);
            }
            return ToSummary(lesson);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task<LessonSessionSummary?> TryJoinAsync(
        Guid lessonId,
        AgentSummary student,
        string pin,
        string studentName,
        CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (!_lessons.TryGetValue(lessonId, out var lesson) ||
                !IsActive(lesson) ||
                !lesson.IsTemporary ||
                student.Role != AgentRole.Student ||
                !student.IsShared ||
                string.IsNullOrWhiteSpace(lesson.JoinPinSalt) ||
                string.IsNullOrWhiteSpace(lesson.JoinPinHash) ||
                !PinHasher.Verify(pin, lesson.JoinPinSalt, lesson.JoinPinHash))
            {
                return null;
            }

            var members = (lesson.MemberAgentIds ?? []).Append(student.AgentId).Distinct().ToArray();
            var participants = new Dictionary<Guid, string>(lesson.ParticipantNames ?? new Dictionary<Guid, string>())
            {
                [student.AgentId] = studentName.Trim()
            };
            lesson = lesson with { MemberAgentIds = members, ParticipantNames = participants };
            _lessons[lessonId] = lesson;
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

    public async Task<LessonSessionSummary?> ExtendAsync(Guid lessonId, TimeSpan extension, CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (!_lessons.TryGetValue(lessonId, out var lesson) || !IsActive(lesson) ||
                extension < TimeSpan.FromMinutes(5) || extension > TimeSpan.FromHours(4))
            {
                return null;
            }

            lesson = lesson with { ExpiresAtUtc = lesson.ExpiresAtUtc.Add(extension) };
            _lessons[lessonId] = lesson;
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

    public async Task<IReadOnlyCollection<LessonSessionSummary>> EndTemporaryForOfflineTeachersAsync(
        TeacherSessionService sessions,
        CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var ended = _lessons.Values
                .Where(item => item.IsTemporary && IsActive(item) && !sessions.IsOnline(item.TeacherId, now))
                .ToArray();
            foreach (var lesson in ended)
            {
                _lessons.TryRemove(lesson.LessonId, out _);
            }

            if (ended.Length > 0)
            {
                await PersistAsync(cancellationToken);
            }

            return ended.Select(ToSummary).ToArray();
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
        var persistedMembers = (lesson.MemberAgentIds ?? []).ToHashSet();
        var members = agents.List()
            .Where(agent => agent.AgentId == lesson.TeacherAgentId || persistedMembers.Contains(agent.AgentId) ||
                            (!lesson.IsTemporary && agent.Role == AgentRole.Student && !agent.IsShared &&
                             string.Equals(agent.ClassroomId, lesson.RoomId, StringComparison.OrdinalIgnoreCase)))
            .Select(agent => agent.AgentId)
            .Distinct()
            .ToArray();
        return new LessonSessionSummary(
            lesson.LessonId,
            lesson.RoomId,
            lesson.TeacherAgentId,
            string.IsNullOrWhiteSpace(lesson.TeacherId) ? $"agent:{lesson.TeacherAgentId:N}" : lesson.TeacherId,
            lesson.TeacherDisplayName,
            string.IsNullOrWhiteSpace(lesson.RoomName) ? lesson.RoomId : lesson.RoomName,
            lesson.IsTemporary,
            lesson.JoinCode,
            lesson.IsCloudRouted,
            lesson.StartedAtUtc,
            lesson.ExpiresAtUtc,
            members,
            (lesson.ParticipantNames ?? new Dictionary<Guid, string>())
                .Where(item => members.Contains(item.Key))
                .OrderBy(item => item.Value, StringComparer.CurrentCultureIgnoreCase)
                .Select(item => new LessonParticipantSummary(item.Key, item.Value))
                .ToArray());
    }

    private string CreateUniqueJoinCode()
    {
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var candidate = System.Security.Cryptography.RandomNumberGenerator
                .GetInt32(0, 1_000_000)
                .ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
            if (!_lessons.Values.Any(item => IsActive(item) && string.Equals(item.JoinCode, candidate, StringComparison.Ordinal)))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("A unique temporary room code could not be generated.");
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
        DateTimeOffset ExpiresAtUtc,
        string TeacherId = "",
        string RoomName = "",
        bool IsTemporary = false,
        string? JoinCode = null,
        string? JoinPinSalt = null,
        string? JoinPinHash = null,
        IReadOnlyCollection<Guid>? MemberAgentIds = null,
        IReadOnlyDictionary<Guid, string>? ParticipantNames = null,
        bool IsCloudRouted = false);
}
