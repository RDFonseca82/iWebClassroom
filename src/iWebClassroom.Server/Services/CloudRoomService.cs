using System.Diagnostics;
using IWebClassroom.Contracts;

namespace IWebClassroom.Server.Services;

public sealed class CloudRoomService(
    CloudSyncStateStore stateStore,
    CloudSyncClient client,
    LocalServerEndpointProvider endpoints,
    LessonRegistry lessons,
    AgentRegistry agents,
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
            return null;
        }

        try
        {
            return await client.AllocateRoomAsync(serverId, token, new CloudRoomAllocationRequest(
                lessonUid, roomName, teacher.TeacherId, teacher.DisplayName, endpoints.HttpsUrls(), expiresAtUtc), cancellationToken);
        }
        catch (Exception exception) when (IsTemporaryCloudFailure(exception))
        {
            logger.LogWarning(exception, "The cloud room directory is unavailable; the temporary room will be local-only until a heartbeat succeeds.");
            return null;
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
            throw new InvalidOperationException("This server is not connected to central management.");
        }

        return await client.ResolveRoomAsync(serverId, token,
            new CloudRoomResolveRequest(roomCode, agent.MachineId, agent.DisplayName, studentName), cancellationToken);
    }

    public async Task<CloudRoomTicketClaims?> ValidateTicketAsync(string handoffToken, CancellationToken cancellationToken)
    {
        if (!TryCredentials(out var serverId, out var token) || string.IsNullOrWhiteSpace(handoffToken))
        {
            return null;
        }

        try
        {
            return await client.RedeemRoomTicketAsync(serverId, token, new CloudRoomTicketRequest(handoffToken), cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            logger.LogWarning(exception, "A room roaming ticket could not be validated.");
            return null;
        }
    }

    public async Task EndAsync(Guid lessonUid, CancellationToken cancellationToken)
    {
        if (!TryCredentials(out var serverId, out var token))
        {
            return;
        }

        try
        {
            await client.EndRoomAsync(serverId, token, lessonUid, cancellationToken);
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
        exception is HttpRequestException http &&
        (http.StatusCode is null ||
         http.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.MethodNotAllowed ||
         (int)http.StatusCode >= 500);
}
