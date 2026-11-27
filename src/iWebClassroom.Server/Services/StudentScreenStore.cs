using System.Collections.Concurrent;

namespace IWebClassroom.Server.Services;

public sealed class StudentScreenStore
{
    private readonly ConcurrentDictionary<Guid, StudentScreenFrame> _frames = new();
    private readonly ConcurrentDictionary<Guid, StudentScreenLease> _leases = new();

    public void Authorize(Guid lessonId, Guid agentId, IReadOnlyCollection<Guid>? viewerAgentIds = null) =>
        _leases[agentId] = new StudentScreenLease(
            lessonId,
            DateTimeOffset.UtcNow.AddSeconds(15),
            viewerAgentIds?.Distinct().ToArray() ?? []);

    public bool Refresh(Guid lessonId, Guid agentId)
    {
        if (!_leases.TryGetValue(agentId, out var lease) || lease.LessonId != lessonId || lease.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            Revoke(agentId);
            return false;
        }

        _leases[agentId] = lease with { ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(15) };
        return true;
    }

    public bool TryAuthorizePublish(
        Guid lessonId,
        Guid agentId,
        IReadOnlyCollection<Guid> requestedViewers,
        out IReadOnlyCollection<Guid> authorizedViewers)
    {
        authorizedViewers = [];
        if (!_leases.TryGetValue(agentId, out var lease) || lease.LessonId != lessonId || lease.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            Revoke(agentId);
            return false;
        }

        var allowed = lease.ViewerAgentIds.ToHashSet();
        authorizedViewers = requestedViewers.Where(allowed.Contains).Distinct().Take(200).ToArray();
        _leases[agentId] = lease with { ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(15) };
        return true;
    }

    public void Update(Guid lessonId, Guid agentId, byte[] jpegFrame) =>
        _frames[agentId] = new StudentScreenFrame(lessonId, agentId, jpegFrame.ToArray(), DateTimeOffset.UtcNow);

    public bool TryGet(Guid lessonId, Guid agentId, out StudentScreenFrame? frame)
    {
        frame = null;
        if (!_frames.TryGetValue(agentId, out var candidate) || candidate.LessonId != lessonId ||
            DateTimeOffset.UtcNow - candidate.CapturedAtUtc > TimeSpan.FromSeconds(5))
        {
            return false;
        }

        frame = candidate;
        return true;
    }

    public void Revoke(Guid agentId)
    {
        _leases.TryRemove(agentId, out _);
        _frames.TryRemove(agentId, out _);
    }

    public void Remove(Guid agentId) => Revoke(agentId);

    public void RemoveLesson(Guid lessonId)
    {
        foreach (var item in _frames.Where(item => item.Value.LessonId == lessonId).ToArray())
        {
            _frames.TryRemove(item.Key, out _);
        }
        foreach (var item in _leases.Where(item => item.Value.LessonId == lessonId).ToArray())
        {
            _leases.TryRemove(item.Key, out _);
        }
    }
}

public sealed record StudentScreenFrame(Guid LessonId, Guid AgentId, byte[] Jpeg, DateTimeOffset CapturedAtUtc);
public sealed record StudentScreenLease(Guid LessonId, DateTimeOffset ExpiresAtUtc, IReadOnlyCollection<Guid> ViewerAgentIds);
