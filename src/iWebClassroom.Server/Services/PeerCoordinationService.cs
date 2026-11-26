using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IWebClassroom.Contracts;

namespace IWebClassroom.Server.Services;

public sealed class PeerCoordinationService(
    CloudDirectoryStore directory,
    CloudSyncStateStore stateStore,
    LessonRegistry lessons,
    LocalServerEndpointProvider endpoints,
    IHttpClientFactory httpClientFactory,
    ILogger<PeerCoordinationService> logger)
{
    public const string SourceHeader = "X-iWebClassroom-Peer";
    public const string TimestampHeader = "X-iWebClassroom-Peer-Time";
    public const string NonceHeader = "X-iWebClassroom-Peer-Nonce";
    public const string SignatureHeader = "X-iWebClassroom-Peer-Signature";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<Guid, PeerObservation> _observations = new();
    private readonly ConcurrentDictionary<Guid, PeerRoomDescriptor> _rooms = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _usedNonces = new();
    private Guid? _lastReportedMaster;

    public bool IsFailoverMode
    {
        get
        {
            var state = stateStore.Snapshot();
            return !string.IsNullOrWhiteSpace(directory.Current.PeerKey) &&
                   state.ServerId.HasValue &&
                   (state.LastCloudContactUtc is null || state.LastCloudContactUtc < DateTimeOffset.UtcNow.AddSeconds(-90));
        }
    }

    public Guid? MasterServerId()
    {
        if (!IsFailoverMode)
        {
            return null;
        }

        var state = stateStore.Snapshot();
        if (!state.ServerId.HasValue)
        {
            return null;
        }

        var candidates = new List<PeerObservation>
        {
            new(state.ServerId.Value, CurrentVersion(), state.LastCloudContactUtc, endpoints.HttpsUrls(), DateTimeOffset.UtcNow)
        };
        candidates.AddRange(_observations.Values.Where(item => item.ObservedAtUtc > DateTimeOffset.UtcNow.AddSeconds(-35)));
        return candidates
            .OrderByDescending(item => ParseVersion(item.Version))
            .ThenByDescending(item => item.LastCloudContactUtc ?? DateTimeOffset.MinValue)
            .ThenBy(item => item.ServerId)
            .First().ServerId;
    }

    public async Task SendHeartbeatAsync(CancellationToken cancellationToken)
    {
        var state = stateStore.Snapshot();
        if (!state.ServerId.HasValue || string.IsNullOrWhiteSpace(directory.Current.PeerKey))
        {
            return;
        }

        var request = new PeerHeartbeatRequest(
            state.ServerId.Value,
            CurrentVersion(),
            state.LastCloudContactUtc,
            endpoints.HttpsUrls(),
            lessons.List().Where(item => item.IsTemporary && item.JoinCode is not null).Select(item => new PeerRoomDescriptor(
                item.LessonId, state.ServerId.Value, item.JoinCode!, item.RoomName, item.TeacherId,
                item.TeacherDisplayName, endpoints.HttpsUrls(), item.ExpiresAtUtc)).ToArray());
        Observe(request);

        var peers = directory.Current.Servers ?? [];
        foreach (var peer in peers.Where(item => item.ServerId != state.ServerId.Value))
        {
            try
            {
                await PostAsync<PeerHeartbeatRequest, JsonElement>(peer, "/api/v1/peer/heartbeat", request, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                logger.LogDebug(exception, "Peer heartbeat to {PeerServerId} failed.", peer.ServerId);
            }
        }

        var master = MasterServerId();
        if (master != _lastReportedMaster)
        {
            _lastReportedMaster = master;
            if (master.HasValue)
            {
                logger.LogWarning("Cloud continuity mode is active. Server {MasterServerId} is the elected temporary master.", master);
            }
            else
            {
                logger.LogInformation("Cloud authority is active; temporary peer master mode is disabled.");
            }
        }
    }

    public void Observe(PeerHeartbeatRequest request)
    {
        _observations[request.ServerId] = new PeerObservation(
            request.ServerId, request.Version, request.LastCloudContactUtc, request.HostUrls, DateTimeOffset.UtcNow);
        foreach (var existing in _rooms.Values.Where(item => item.HostServerId == request.ServerId).ToArray())
        {
            _rooms.TryRemove(existing.LessonUid, out _);
        }
        foreach (var room in request.Rooms.Where(item => item.ExpiresAtUtc > DateTimeOffset.UtcNow))
        {
            _rooms[room.LessonUid] = room;
        }
    }

    public async Task<CloudRoomAllocationResponse?> TryAllocateAsync(
        Guid lessonUid,
        string roomName,
        TeacherIdentity teacher,
        DateTimeOffset expiresAtUtc,
        CancellationToken cancellationToken)
    {
        var state = stateStore.Snapshot();
        var masterId = MasterServerId();
        if (!state.ServerId.HasValue || !masterId.HasValue)
        {
            return null;
        }

        var request = new PeerRoomAllocationRequest(
            lessonUid, state.ServerId.Value, roomName, teacher.TeacherId, teacher.DisplayName,
            endpoints.HttpsUrls(), expiresAtUtc);
        if (masterId == state.ServerId.Value)
        {
            return Allocate(request);
        }

        var master = FindPeer(masterId.Value);
        return master is null ? null : await PostAsync<PeerRoomAllocationRequest, CloudRoomAllocationResponse>(
            master, "/api/v1/peer/rooms/allocate", request, cancellationToken);
    }

    public CloudRoomAllocationResponse Allocate(PeerRoomAllocationRequest request)
    {
        var existing = _rooms.Values.FirstOrDefault(item => item.LessonUid == request.LessonUid);
        if (existing is not null)
        {
            return new CloudRoomAllocationResponse(existing.LessonUid, existing.RoomCode, existing.ExpiresAtUtc);
        }

        string code;
        do
        {
            code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        } while (_rooms.Values.Any(item => item.ExpiresAtUtc > DateTimeOffset.UtcNow && item.RoomCode == code) ||
                 lessons.List().Any(item => item.JoinCode == code));

        var room = new PeerRoomDescriptor(request.LessonUid, request.HostServerId, code, request.RoomName,
            request.TeacherId, request.TeacherName, request.HostUrls, request.ExpiresAtUtc);
        _rooms[room.LessonUid] = room;
        return new CloudRoomAllocationResponse(room.LessonUid, room.RoomCode, room.ExpiresAtUtc);
    }

    public async Task<CloudRoomResolveResponse?> TryResolveAsync(
        string roomCode,
        AgentSummary agent,
        string studentName,
        CancellationToken cancellationToken)
    {
        var state = stateStore.Snapshot();
        var masterId = MasterServerId();
        if (!state.ServerId.HasValue || !masterId.HasValue)
        {
            return null;
        }

        var request = new PeerRoomResolveRequest(roomCode, agent.MachineId, agent.DisplayName, studentName);
        if (masterId == state.ServerId.Value)
        {
            return Resolve(request);
        }

        var master = FindPeer(masterId.Value);
        return master is null ? null : await PostAsync<PeerRoomResolveRequest, CloudRoomResolveResponse>(
            master, "/api/v1/peer/rooms/resolve", request, cancellationToken);
    }

    public CloudRoomResolveResponse? Resolve(PeerRoomResolveRequest request)
    {
        var room = _rooms.Values.FirstOrDefault(item => item.RoomCode == request.RoomCode && item.ExpiresAtUtc > DateTimeOffset.UtcNow);
        if (room is null)
        {
            return null;
        }

        var expires = DateTimeOffset.UtcNow.AddMinutes(2);
        var claims = new PeerRoomTicketClaims(room.LessonUid, room.HostServerId, room.RoomCode,
            request.MachineId, request.ComputerName, request.StudentName, expires);
        return new CloudRoomResolveResponse(room.RoomName, room.HostUrls, SignTicket(claims), expires);
    }

    public CloudRoomTicketClaims? ValidateTicket(string token)
    {
        var key = directory.Current.PeerKey;
        var state = stateStore.Snapshot();
        if (string.IsNullOrWhiteSpace(key) || !state.ServerId.HasValue)
        {
            return null;
        }

        var parts = token.Split('.', 2);
        if (parts.Length != 2)
        {
            return null;
        }

        try
        {
            var payload = Base64UrlDecode(parts[0]);
            var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), payload);
            if (!CryptographicOperations.FixedTimeEquals(expected, Base64UrlDecode(parts[1])))
            {
                return null;
            }
            var claims = JsonSerializer.Deserialize<PeerRoomTicketClaims>(payload, JsonOptions);
            if (claims is null || claims.HostServerId != state.ServerId.Value || claims.ExpiresAtUtc < DateTimeOffset.UtcNow)
            {
                return null;
            }
            return new CloudRoomTicketClaims(claims.LessonUid, claims.RoomCode, claims.MachineId,
                claims.ComputerName, claims.StudentName, claims.ExpiresAtUtc);
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return null;
        }
    }

    public bool Authenticate(HttpRequest request, object body)
    {
        var key = directory.Current.PeerKey;
        if (string.IsNullOrWhiteSpace(key) ||
            !Guid.TryParse(request.Headers[SourceHeader], out var source) ||
            !Guid.TryParse(request.Headers[NonceHeader], out var nonce) ||
            !long.TryParse(request.Headers[TimestampHeader], out var timestamp) ||
            Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - timestamp) > 120 ||
            !(directory.Current.Servers ?? []).Any(item => item.ServerId == source))
        {
            return false;
        }

        var bodyJson = JsonSerializer.Serialize(body, JsonOptions);
        var expected = Signature(key, source, timestamp, nonce, bodyJson);
        try
        {
            var valid = CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(expected),
                Convert.FromBase64String(request.Headers[SignatureHeader].ToString()));
            if (!valid || !_usedNonces.TryAdd(nonce, DateTimeOffset.UtcNow))
            {
                return false;
            }
            foreach (var expired in _usedNonces.Where(item => item.Value < DateTimeOffset.UtcNow.AddMinutes(-3)).ToArray())
            {
                _usedNonces.TryRemove(expired.Key, out _);
            }
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public void End(Guid lessonUid) => _rooms.TryRemove(lessonUid, out _);

    private async Task<TResponse?> PostAsync<TRequest, TResponse>(
        CloudPeerServer peer, string path, TRequest request, CancellationToken cancellationToken)
    {
        var baseUrl = peer.HostUrls.FirstOrDefault(url => Uri.TryCreate(url, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps);
        var state = stateStore.Snapshot();
        var key = directory.Current.PeerKey;
        if (baseUrl is null || !state.ServerId.HasValue || string.IsNullOrWhiteSpace(key))
        {
            return default;
        }

        var body = JsonSerializer.Serialize(request, JsonOptions);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var nonce = Guid.NewGuid();
        using var message = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        message.Headers.Add(SourceHeader, state.ServerId.Value.ToString());
        message.Headers.Add(TimestampHeader, timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
        message.Headers.Add(NonceHeader, nonce.ToString());
        message.Headers.Add(SignatureHeader, Signature(key, state.ServerId.Value, timestamp, nonce, body));
        var client = httpClientFactory.CreateClient(nameof(PeerCoordinationService));
        using var response = await client.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return default;
        }
        return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken);
    }

    private CloudPeerServer? FindPeer(Guid id) => (directory.Current.Servers ?? []).FirstOrDefault(item => item.ServerId == id);

    private string SignTicket(PeerRoomTicketClaims claims)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(claims, JsonOptions);
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(directory.Current.PeerKey!), payload);
        return Base64UrlEncode(payload) + "." + Base64UrlEncode(signature);
    }

    private static string Signature(string key, Guid source, long timestamp, Guid nonce, string body)
    {
        var input = Encoding.UTF8.GetBytes($"{source:D}\n{timestamp}\n{nonce:D}\n{body}");
        return Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), input));
    }

    private static string Base64UrlEncode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
    private static Version ParseVersion(string value) => Version.TryParse(value, out var version) ? version : new Version(0, 0);
    private static string CurrentVersion() => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
}

public sealed class PeerCoordinationWorker(PeerCoordinationService peers, ILogger<PeerCoordinationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await peers.SendHeartbeatAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception exception) { logger.LogError(exception, "The local server peer coordination worker stopped unexpectedly."); }
    }
}

public sealed record PeerHeartbeatRequest(Guid ServerId, string Version, DateTimeOffset? LastCloudContactUtc,
    IReadOnlyCollection<string> HostUrls, IReadOnlyCollection<PeerRoomDescriptor> Rooms);
public sealed record PeerObservation(Guid ServerId, string Version, DateTimeOffset? LastCloudContactUtc,
    IReadOnlyCollection<string> HostUrls, DateTimeOffset ObservedAtUtc);
public sealed record PeerRoomDescriptor(Guid LessonUid, Guid HostServerId, string RoomCode, string RoomName,
    string TeacherId, string TeacherName, IReadOnlyCollection<string> HostUrls, DateTimeOffset ExpiresAtUtc);
public sealed record PeerRoomAllocationRequest(Guid LessonUid, Guid HostServerId, string RoomName, string TeacherId,
    string TeacherName, IReadOnlyCollection<string> HostUrls, DateTimeOffset ExpiresAtUtc);
public sealed record PeerRoomResolveRequest(string RoomCode, string MachineId, string ComputerName, string StudentName);
public sealed record PeerRoomTicketClaims(Guid LessonUid, Guid HostServerId, string RoomCode, string MachineId,
    string ComputerName, string StudentName, DateTimeOffset ExpiresAtUtc);
