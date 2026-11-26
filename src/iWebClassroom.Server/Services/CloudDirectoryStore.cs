using System.Text.Json;
using System.Text.Json.Serialization;
using IWebClassroom.Contracts;
using IWebClassroom.Server.Configuration;
using IWebClassroom.Server.Security;
using Microsoft.Extensions.Options;

namespace IWebClassroom.Server.Services;

public sealed class CloudDirectoryStore(
    IOptions<ClassroomOptions> classroomOptions,
    IWebHostEnvironment environment,
    ILogger<CloudDirectoryStore> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _path = ResolvePath(environment.ContentRootPath, classroomOptions.Value.DataDirectory);
    private CloudDirectoryResponse _directory = CloudDirectoryResponse.Empty;

    public CloudDirectoryResponse Current => _directory;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            await using var stream = File.OpenRead(_path);
            _directory = await JsonSerializer.DeserializeAsync<CloudDirectoryResponse>(stream, JsonOptions, cancellationToken)
                         ?? CloudDirectoryResponse.Empty;
            if (_directory.PeerKey?.StartsWith("protected:", StringComparison.Ordinal) == true)
            {
                _directory = _directory with { PeerKey = MachineSecretProtector.Unprotect(_directory.PeerKey[10..]) };
            }
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "The synchronized school directory is invalid and will be ignored.");
            _directory = CloudDirectoryResponse.Empty;
        }
        catch (System.Security.Cryptography.CryptographicException exception)
        {
            logger.LogError(exception, "The protected peer key could not be read on this machine; peer continuity will remain disabled until the next cloud synchronization.");
            _directory = CloudDirectoryResponse.Empty;
        }
    }

    public async Task ApplyAsync(CloudDirectoryResponse? directory, CancellationToken cancellationToken)
    {
        if (directory is null)
        {
            return;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            _directory = directory;
            var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                var persisted = string.IsNullOrWhiteSpace(_directory.PeerKey)
                    ? _directory
                    : _directory with { PeerKey = "protected:" + MachineSecretProtector.Protect(_directory.PeerKey) };
                await JsonSerializer.SerializeAsync(stream, persisted, JsonOptions, cancellationToken);
            }

            File.Move(temporaryPath, _path, true);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static string ResolvePath(string root, string configuredPath)
    {
        var directory = Path.IsPathRooted(configuredPath) ? configuredPath : Path.Combine(root, configuredPath);
        return Path.Combine(directory, "cloud-directory.json");
    }
}

public sealed record CloudDirectoryResponse(
    [property: JsonPropertyName("administrators")]
    IReadOnlyCollection<CloudAdministratorEntry> Administrators,
    [property: JsonPropertyName("teachers")]
    IReadOnlyCollection<CloudTeacherEntry> Teachers,
    [property: JsonPropertyName("classrooms")]
    IReadOnlyCollection<CloudClassroomEntry> Classrooms,
    [property: JsonPropertyName("updated_at_utc")]
    DateTimeOffset? UpdatedAtUtc,
    [property: JsonPropertyName("devices")]
    IReadOnlyCollection<CloudDeviceAssignment>? Devices = null,
    [property: JsonPropertyName("servers")]
    IReadOnlyCollection<CloudPeerServer>? Servers = null,
    [property: JsonPropertyName("peer_key")]
    string? PeerKey = null,
    [property: JsonPropertyName("applications")]
    IReadOnlyCollection<ClassroomApplication>? Applications = null)
{
    public static CloudDirectoryResponse Empty { get; } = new([], [], [], null, [], [], null, DefaultApplications);

    public static IReadOnlyCollection<ClassroomApplication> DefaultApplications { get; } =
    [
        new("notepad", "Bloco de Notas", "%WINDIR%\\System32\\notepad.exe"),
        new("calculator", "Calculadora", "%WINDIR%\\System32\\calc.exe")
    ];
}

public sealed record CloudPeerServer(
    [property: JsonPropertyName("server_id")] Guid ServerId,
    [property: JsonPropertyName("instance_uid")] Guid InstanceUid,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("last_sync_at_utc")] DateTimeOffset? LastSyncAtUtc,
    [property: JsonPropertyName("host_urls")] IReadOnlyCollection<string> HostUrls);

public sealed record CloudAdministratorEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("role")] string Role);

public sealed record CloudTeacherEntry(
    [property: JsonPropertyName("id")]
    string Id,
    [property: JsonPropertyName("name")]
    string Name,
    [property: JsonPropertyName("email")]
    string Email);

public sealed record CloudClassroomEntry(
    [property: JsonPropertyName("id")]
    string Id,
    [property: JsonPropertyName("code")]
    string Code,
    [property: JsonPropertyName("name")]
    string Name);

public sealed record CloudDeviceAssignment(
    [property: JsonPropertyName("machine_id")]
    string MachineId,
    [property: JsonPropertyName("assignment_mode")]
    string AssignmentMode,
    [property: JsonPropertyName("role")]
    string Role,
    [property: JsonPropertyName("classroom_code")]
    string? ClassroomCode,
    [property: JsonPropertyName("shared_zone")]
    string? SharedZone);
