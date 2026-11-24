using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace IWebClassroom.Server.Security;

public sealed class AdminSessionService
{
    public const string HeaderName = "X-Admin-Session";
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _sessions = new(StringComparer.Ordinal);

    public AdminSession Create()
    {
        RemoveExpired();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expiresAtUtc = DateTimeOffset.UtcNow.Add(Lifetime);
        _sessions[Hash(token)] = expiresAtUtc;
        return new AdminSession(token, expiresAtUtc);
    }

    public bool IsValid(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
        {
            return false;
        }

        var key = Hash(token);
        if (!_sessions.TryGetValue(key, out var expiresAtUtc))
        {
            return false;
        }

        if (expiresAtUtc > DateTimeOffset.UtcNow)
        {
            return true;
        }

        _sessions.TryRemove(key, out _);
        return false;
    }

    private void RemoveExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var session in _sessions.Where(item => item.Value <= now))
        {
            _sessions.TryRemove(session.Key, out _);
        }
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
}

public sealed record AdminSession(string Token, DateTimeOffset ExpiresAtUtc);
