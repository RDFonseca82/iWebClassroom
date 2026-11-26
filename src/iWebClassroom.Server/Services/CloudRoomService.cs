using System.Diagnostics;
using IWebClassroom.Contracts;

namespace IWebClassroom.Server.Services;

public sealed class CloudRoomService(
    CloudSyncStateStore stateStore,
    CloudSyncClient client,
    LocalServerEndpointProvider endpoints,
    LessonRegistry lessons,
    AgentRegistry agents,
    PeerCoordinationService peers,
    CloudSyncStateStore cloudState,
    ILogger<CloudRoomService> logger)
{
    public bool HasActiveLessons => lessons.List().Count > 0;

    public async Task<CloudRoomAllocationResponse?> TryAllocateAsync(
        Guid lessonUid,
        string roomName,
        TeacherIdentity teacher,
        DateTimeOffset expiresAtUtc,
        CancellationToken cancellationToken)
    {
        if (!TryCredentials(out var serverId, out var token))
        {
            return await peers.TryAllocateAsync(lessonUid, roomName, teacher, expiresAtUtc, cancellationToken);
        }

        try
        {
            using var cloudAttempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cloudAttempt.CancelAfter(TimeSpan.FromSeconds(3));
            return await client.AllocateRoomAsync(serverId, token, new CloudRoomAllocationRequest(
                lessonUid, roomName, teacher.TeacherId, teacher.DisplayName, endpoints.HttpsUrls(), expiresAtUtc), cloudAttempt.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "The cloud room allocation timed out; the temporary room will continue locally.");
            return await peers.TryAllocateAsync(lessonUid, roomName, teacher, expiresAtUtc, cancellationToken);
        }
        catch (Exception exception) when (IsTemporaryCloudFailure(exception))
        {
            logger.LogWarning(exception, "The cloud room directory is unavailable; the temporary room will be local-only until a heartbeat succeeds.");
            return await peers.TryAllocateAsync(lessonUid, roomName, teacher, expiresAtUtc, cancellationToken);
        }
    }

    public async Task<CloudRoomResolveResponse> ResolveAsync(
        string roomCode,
        AgentSummary agent,
        string studentName,
        CancellationToken cancellationToken)
    {
        if (!TryCredentials(out var serverId, out var token))
        {
            return await peers.TryResolveAsync(roomCode, agent, studentName, cancellationToken)
                   ?? throw new InvalidOperationException("Neither the cloud nor the temporary local master can resolve this room.");
        }
        try
        {
            return await client.ResolveRoomAsync(serverId, token,
                new CloudRoomResolveRequest(roomCode, agent.MachineId, agent.DisplayName, studentName), cancellationToken);
        }
        catch (CloudApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw;
        }
        catch (Exception exception) when (IsTemporaryCloudFailure(exception))
        {
            logger.LogWarning(exception, "The cloud room lookup is unavailable; the elected local master will be used.");
            return await peers.TryResolveAsync(roomCode, agent, studentName, cancellationToken)
                   ?? throw new InvalidOperationException("Neither the cloud nor the temporary local master can resolve this room.", exception);
        }
    }

    public async Task<CloudRoomTicketClaims?> ValidateTicketAsync(string handoffToken, CancellationToken cancellationToken)
    {
        if (!TryCredentials(out var serverId, out var token) || string.IsNullOrWhiteSpace(handoffToken))
        {
            return peers.ValidateTicket(handoffToken);
        }

        try
        {
            return await client.RedeemRoomTicketAsync(serverId, token, new CloudRoomTicketRequest(handoffToken), cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            logger.LogWarning(exception, "A room roaming ticket could not be validated.");
            return peers.ValidateTicket(handoffToken);
        }
    }

    public async Task EndAsync(Guid lessonUid, CancellationToken cancellationToken)
    {
        peers.End(lessonUid);
        if (!TryCredentials(out var serverId, out var token))
        {
            return;
        }

        try
        {
            using var cloudAttempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cloudAttempt.CancelAfter(TimeSpan.FromSeconds(3));
            await client.EndRoomAsync(serverId, token, lessonUid, cloudAttempt.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Room {LessonUid} could not be removed from the cloud directory before the local timeout.", lessonUid);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            logger.LogWarning(exception, "Room {LessonUid} could not be removed from the cloud directory immediately.", lessonUid);
        }
    }

    public async Task HeartbeatAsync(CancellationToken cancellationToken)
    {
        if (!TryCredentials(out var serverId, out var token))
        {
            return;
        }

        var activeLessons = lessons.List();
        var temporaryRooms = activeLessons.Where(item => item.IsTemporary && item.JoinCode is not null)
            .Select(item => new CloudRoomHeartbeatEntry(
                item.LessonId,
                item.JoinCode!,
                item.RoomName,
                item.TeacherId,
                item.TeacherDisplayName,
                item.ExpiresAtUtc,
                item.Participants.Count))
            .ToArray();
        var agentSnapshot = agents.List();
        var request = new CloudRoomHeartbeatRequest(
            endpoints.HttpsUrls(),
            activeLessons.Count,
            agentSnapshot.Count(item => item.IsOnline),
            Process.GetCurrentProcess().WorkingSet64,
            GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            temporaryRooms);

        try
        {
            await client.SendRoomHeartbeatAsync(serverId, token, request, cancellationToken);
            await cloudState.MarkCloudContactAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            logger.LogWarning(exception, "The room and server load heartbeat could not be sent to the cloud.");
        }
    }

    private bool TryCredentials(out Guid serverId, out string token)
    {
        var state = stateStore.Snapshot();
        var accessToken = stateStore.GetAccessToken();
        serverId = state.ServerId ?? Guid.Empty;
        token = accessToken ?? string.Empty;
        return state.ServerId.HasValue && !string.IsNullOrWhiteSpace(accessToken);
    }

    private static bool IsTemporaryCloudFailure(Exception exception) =>
        exception is InvalidOperationException ||
        exception is HttpRequestException http &&
        (http.StatusCode is null ||
         http.StatusCode is System.Net.HttpStatusCode.NotFound or
             System.Net.HttpStatusCode.MethodNotAllowed or
             System.Net.HttpStatusCode.RequestTimeout or
             System.Net.HttpStatusCode.TooManyRequests ||
         (int)http.StatusCode >= 500);
}
