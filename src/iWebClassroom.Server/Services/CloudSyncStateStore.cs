using System.Text.Json;
using IWebClassroom.Server.Configuration;
using IWebClassroom.Server.Security;
using Microsoft.Extensions.Options;

namespace IWebClassroom.Server.Services;

public sealed class CloudSyncStateStore(
    IOptions<ClassroomOptions> classroomOptions,
    IWebHostEnvironment environment,
    ILogger<CloudSyncStateStore> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _path = ResolvePath(environment.ContentRootPath, classroomOptions.Value.DataDirectory, "cloud-sync-state.json");
    private CloudSyncState _state = CloudSyncState.Empty;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        if (File.Exists(_path))
        {
            try
            {
                await using var stream = File.OpenRead(_path);
                _state = await JsonSerializer.DeserializeAsync<CloudSyncState>(stream, JsonOptions, cancellationToken) ?? CloudSyncState.Empty;
            }
            catch (JsonException exception)
            {
                logger.LogError(exception, "The cloud synchronization state is invalid; a new local identity will be created.");
                _state = CloudSyncState.Empty;
            }
        }

        if (_state.InstanceUid == Guid.Empty)
        {
            _state = _state with { InstanceUid = Guid.NewGuid(), Status = "disconnected" };
            await PersistAsync(cancellationToken);
        }
    }

    public CloudSyncState Snapshot() => _state;

    public string? GetAccessToken()
    {
        if (string.IsNullOrWhiteSpace(_state.ProtectedAccessToken))
        {
            return null;
        }

        try
        {
            return MachineSecretProtector.Unprotect(_state.ProtectedAccessToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The cloud synchronization token could not be decrypted.");
            return null;
        }
    }

    public async Task SaveAuthenticationAsync(CloudAuthResponse response, string configurationHash, CancellationToken cancellationToken)
    {
        await UpdateAsync(state => state with
        {
            ServerId = response.ServerId,
            SchoolId = response.SchoolId,
            SchoolName = response.SchoolName,
            ProtectedAccessToken = MachineSecretProtector.Protect(response.AccessToken),
            ConfigurationRevision = response.Configuration.Revision,
            ConfigurationHash = configurationHash,
            Status = "connected",
            LastCloudContactUtc = DateTimeOffset.UtcNow,
            LastError = null,
            Conflict = false
        }, cancellationToken);
    }

    public Task UpdateSynchronizationAsync(long revision, string configurationHash, bool conflict, CancellationToken cancellationToken) =>
        UpdateAsync(state => state with
        {
            ConfigurationRevision = revision,
            ConfigurationHash = configurationHash,
            Status = conflict ? "conflict" : "synchronized",
            LastSyncUtc = DateTimeOffset.UtcNow,
            LastCloudContactUtc = DateTimeOffset.UtcNow,
            LastError = null,
            Conflict = conflict
        }, cancellationToken);

    public Task MarkCloudContactAsync(CancellationToken cancellationToken) =>
        UpdateAsync(state => state with { LastCloudContactUtc = DateTimeOffset.UtcNow }, cancellationToken);

    public Task SetErrorAsync(string error, CancellationToken cancellationToken) =>
        UpdateAsync(state => state with
        {
            Status = "error",
            LastError = error.Length > 1000 ? error[..1000] : error
        }, cancellationToken);

    public Task DisconnectAsync(CancellationToken cancellationToken) =>
        UpdateAsync(state => state with
        {
            ServerId = null,
            SchoolId = null,
            SchoolName = null,
            ProtectedAccessToken = null,
            ConfigurationRevision = 0,
            ConfigurationHash = string.Empty,
            Status = "disconnected",
            LastSyncUtc = null,
            LastCloudContactUtc = null,
            LastError = null,
            Conflict = false
        }, cancellationToken);

    private async Task UpdateAsync(Func<CloudSyncState, CloudSyncState> update, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            _state = update(_state);
            await PersistUnsafeAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await PersistUnsafeAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task PersistUnsafeAsync(CancellationToken cancellationToken)
    {
        var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, _state, JsonOptions, cancellationToken);
        }
        File.Move(temporaryPath, _path, true);
    }

    private static string ResolvePath(string root, string configuredPath, string fileName)
    {
        var directory = Path.IsPathRooted(configuredPath) ? configuredPath : Path.Combine(root, configuredPath);
        return Path.Combine(directory, fileName);
    }
}

public sealed record CloudSyncState(
    Guid InstanceUid,
    Guid? ServerId,
    Guid? SchoolId,
    string? SchoolName,
    string? ProtectedAccessToken,
    long ConfigurationRevision,
    string ConfigurationHash,
    string Status,
    DateTimeOffset? LastSyncUtc,
    string? LastError,
    bool Conflict,
    DateTimeOffset? LastCloudContactUtc = null)
{
    public static CloudSyncState Empty { get; } = new(
        Guid.Empty, null, null, null, null, 0, string.Empty, "disconnected", null, null, false, null);
}
