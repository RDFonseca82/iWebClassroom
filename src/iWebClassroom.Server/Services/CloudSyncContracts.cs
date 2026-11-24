using System.Text.Json;
using System.Text.Json.Serialization;
using IWebClassroom.Server.Configuration;

namespace IWebClassroom.Server.Services;

public sealed record CloudConnectRequest(string Username, string Password);

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
    [property: JsonPropertyName("license")] JsonElement? License);

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
