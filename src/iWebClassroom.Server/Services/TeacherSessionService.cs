using System.Collections.Concurrent;
using System.Security.Cryptography;
using IWebClassroom.Contracts;
using IWebClassroom.Server.Security;

namespace IWebClassroom.Server.Services;

public sealed class TeacherSessionService(CloudDirectoryStore directory)
{
    public const string HeaderName = HubProtocol.TeacherSessionHeader;
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);
    private static readonly TimeSpan OnlineWindow = TimeSpan.FromSeconds(75);
    private readonly ConcurrentDictionary<string, Session> _sessions = new(StringComparer.Ordinal);

    public TeacherSessionResponse? Create(string pin)
    {
        if (pin is not { Length: 6 } || pin.Any(character => !char.IsAsciiDigit(character)))
        {
            return null;
        }

        var matches = directory.Current.Teachers
            .Where(teacher => PinHasher.Verify(pin, teacher.PinSalt, teacher.PinHash))
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
        {
            return null;
        }

        var teacher = matches[0];
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var now = DateTimeOffset.UtcNow;
        var session = new Session(
            SecretComparer.Hash(token),
            teacher.Id,
            teacher.Name,
            now.Add(Lifetime),
            now);
        _sessions[token] = session;
        Cleanup(now);
        return new TeacherSessionResponse(token, teacher.Id, teacher.Name, session.ExpiresAtUtc);
    }

    public bool TryGet(string? token, out TeacherIdentity? teacher)
    {
        teacher = null;
        if (string.IsNullOrWhiteSpace(token) || !_sessions.TryGetValue(token, out var session))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (session.ExpiresAtUtc <= now || !SecretComparer.Equals(SecretComparer.Hash(token), session.TokenHash))
        {
            _sessions.TryRemove(token, out _);
            return false;
        }

        if (!directory.Current.Teachers.Any(item => item.Id == session.TeacherId))
        {
            _sessions.TryRemove(token, out _);
            return false;
        }

        _sessions[token] = session with { LastSeenUtc = now };
        teacher = new TeacherIdentity(session.TeacherId, session.TeacherName);
        return true;
    }

    public bool IsOnline(string teacherId, DateTimeOffset now) =>
        _sessions.Values.Any(session =>
            session.TeacherId == teacherId &&
            session.ExpiresAtUtc > now &&
            session.LastSeenUtc >= now.Subtract(OnlineWindow));

    private void Cleanup(DateTimeOffset now)
    {
        foreach (var item in _sessions.Where(item => item.Value.ExpiresAtUtc <= now))
        {
            _sessions.TryRemove(item.Key, out _);
        }
    }

    private sealed record Session(
        string TokenHash,
        string TeacherId,
        string TeacherName,
        DateTimeOffset ExpiresAtUtc,
        DateTimeOffset LastSeenUtc);
}
