using System.Text.Json;
using System.Text.Json.Serialization;
using IWebClassroom.Server.Configuration;

namespace IWebClassroom.Server.Services;

public sealed record CloudConnectRequest(string Username, string Password);

public sealed record CloudAdministratorLoginRequest(string Email, string Password);

public sealed record CloudAdministratorResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("school_id")] Guid SchoolId);

public sealed record CloudAuthRequest(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("instance_uid")] Guid InstanceUid,
    [property: JsonPropertyName("server_name")] string ServerName,
    [property: JsonPropertyName("server_version")] string ServerVersion);

public sealed record CloudAuthResponse(
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("server_id")] Guid ServerId,
    [property: JsonPropertyName("school_id")] Guid SchoolId,
    [property: JsonPropertyName("school_name")] string SchoolName,
    [property: JsonPropertyName("configuration")] CloudConfigurationResponse Configuration);

public sealed record CloudSyncRequest(
    [property: JsonPropertyName("instance_uid")] Guid InstanceUid,
    [property: JsonPropertyName("server_version")] string ServerVersion,
    [property: JsonPropertyName("agent_version")] string AgentVersion,
    [property: JsonPropertyName("device_count")] int DeviceCount,
    [property: JsonPropertyName("classroom_count")] int ClassroomCount,
    [property: JsonPropertyName("configuration")] CloudConfigurationRequest Configuration);

public sealed record CloudConfigurationRequest(
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("dirty")] bool Dirty,
    [property: JsonPropertyName("settings")] CloudServerSettings Settings);

public sealed record CloudSyncResponse(
    [property: JsonPropertyName("server_time")] DateTimeOffset ServerTime,
    [property: JsonPropertyName("configuration")] CloudConfigurationResponse Configuration,
    [property: JsonPropertyName("license")] JsonElement? License,
    [property: JsonPropertyName("directory")] CloudDirectoryResponse? Directory = null,
    [property: JsonPropertyName("server_update")] CloudApprovedServerUpdate? ServerUpdate = null);

public sealed record CloudApprovedServerUpdate(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("manifest_url")] string ManifestUrl,
    [property: JsonPropertyName("release_url")] string? ReleaseUrl,
    [property: JsonPropertyName("published_at_utc")] DateTimeOffset? PublishedAtUtc,
    [property: JsonPropertyName("approved_at_utc")] DateTimeOffset ApprovedAtUtc,
    [property: JsonPropertyName("approved_by_user_id")] string? ApprovedByUserId,
    [property: JsonPropertyName("approved_by_name")] string? ApprovedByName);

public sealed record CloudConfigurationResponse(
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("settings")] CloudServerSettings Settings,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset? UpdatedAtUtc,
    [property: JsonPropertyName("source")] string? Source,
    [property: JsonPropertyName("conflict")] bool Conflict = false);

public sealed record CloudSyncStatus(
    bool Enabled,
    bool Connected,
    Guid InstanceUid,
    Guid? ServerId,
    Guid? SchoolId,
    string? SchoolName,
    long ConfigurationRevision,
    string Status,
    DateTimeOffset? LastSyncUtc,
    string? LastError,
    bool Conflict,
    string CloudUrl);

public sealed record CloudRoomAllocationRequest(
    [property: JsonPropertyName("lesson_uid")] Guid LessonUid,
    [property: JsonPropertyName("room_name")] string RoomName,
    [property: JsonPropertyName("teacher_id")] string TeacherId,
    [property: JsonPropertyName("teacher_name")] string TeacherName,
    [property: JsonPropertyName("host_urls")] IReadOnlyCollection<string> HostUrls,
    [property: JsonPropertyName("expires_at_utc")] DateTimeOffset ExpiresAtUtc);

public sealed record CloudRoomAllocationResponse(
    [property: JsonPropertyName("lesson_uid")] Guid LessonUid,
    [property: JsonPropertyName("room_code")] string RoomCode,
    [property: JsonPropertyName("expires_at_utc")] DateTimeOffset ExpiresAtUtc);

public sealed record CloudRoomResolveRequest(
    [property: JsonPropertyName("room_code")] string RoomCode,
    [property: JsonPropertyName("machine_id")] string MachineId,
    [property: JsonPropertyName("computer_name")] string ComputerName,
    [property: JsonPropertyName("student_name")] string StudentName);

public sealed record CloudRoomResolveResponse(
    [property: JsonPropertyName("room_name")] string RoomName,
    [property: JsonPropertyName("host_urls")] IReadOnlyCollection<string> HostUrls,
    [property: JsonPropertyName("handoff_token")] string HandoffToken,
    [property: JsonPropertyName("ticket_expires_at_utc")] DateTimeOffset TicketExpiresAtUtc);

public sealed record CloudRoomTicketRequest(
    [property: JsonPropertyName("handoff_token")] string HandoffToken);

public sealed record CloudRoomTicketClaims(
    [property: JsonPropertyName("lesson_uid")] Guid LessonUid,
    [property: JsonPropertyName("room_code")] string RoomCode,
    [property: JsonPropertyName("machine_id")] string MachineId,
    [property: JsonPropertyName("computer_name")] string ComputerName,
    [property: JsonPropertyName("student_name")] string StudentName,
    [property: JsonPropertyName("expires_at_utc")] DateTimeOffset ExpiresAtUtc);

public sealed record CloudRoomHeartbeatRequest(
    [property: JsonPropertyName("host_urls")] IReadOnlyCollection<string> HostUrls,
    [property: JsonPropertyName("active_lessons")] int ActiveLessons,
    [property: JsonPropertyName("online_agents")] int OnlineAgents,
    [property: JsonPropertyName("working_set_bytes")] long WorkingSetBytes,
    [property: JsonPropertyName("available_memory_bytes")] long AvailableMemoryBytes,
    [property: JsonPropertyName("rooms")] IReadOnlyCollection<CloudRoomHeartbeatEntry> Rooms);

public sealed record CloudRoomHeartbeatEntry(
    [property: JsonPropertyName("lesson_uid")] Guid LessonUid,
    [property: JsonPropertyName("room_code")] string RoomCode,
    [property: JsonPropertyName("room_name")] string RoomName,
    [property: JsonPropertyName("teacher_id")] string TeacherId,
    [property: JsonPropertyName("teacher_name")] string TeacherName,
    [property: JsonPropertyName("expires_at_utc")] DateTimeOffset ExpiresAtUtc,
    [property: JsonPropertyName("participant_count")] int ParticipantCount);
