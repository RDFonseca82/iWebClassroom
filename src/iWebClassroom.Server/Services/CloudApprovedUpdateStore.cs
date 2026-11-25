using System.Text.Json;
using IWebClassroom.Server.Configuration;
using Microsoft.Extensions.Options;

namespace IWebClassroom.Server.Services;

public sealed class CloudApprovedUpdateStore(
    IOptions<ClassroomOptions> classroomOptions,
    IWebHostEnvironment environment,
    ILogger<CloudApprovedUpdateStore> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _path = ResolvePath(environment.ContentRootPath, classroomOptions.Value.DataDirectory);
    private CloudApprovedUpdateState _state = CloudApprovedUpdateState.Empty;

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
            _state = await JsonSerializer.DeserializeAsync<CloudApprovedUpdateState>(stream, JsonOptions, cancellationToken)
                     ?? CloudApprovedUpdateState.Empty;
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "The cached cloud update approval is invalid and will be ignored.");
            _state = CloudApprovedUpdateState.Empty;
        }
    }

    public CloudApprovedUpdateState Snapshot() => _state;

    public async Task ApplyAsync(CloudApprovedServerUpdate? approval, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            _state = new CloudApprovedUpdateState(approval, DateTimeOffset.UtcNow);
            var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, _state, JsonOptions, cancellationToken);
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
        return Path.Combine(directory, "cloud-approved-update.json");
    }
}

public sealed record CloudApprovedUpdateState(CloudApprovedServerUpdate? Approval, DateTimeOffset? ReceivedAtUtc)
{
    public static CloudApprovedUpdateState Empty { get; } = new(null, null);
}
