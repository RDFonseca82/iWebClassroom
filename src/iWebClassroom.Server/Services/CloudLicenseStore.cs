using System.Text.Json;
using IWebClassroom.Server.Configuration;
using Microsoft.Extensions.Options;

namespace IWebClassroom.Server.Services;

public sealed class CloudLicenseStore(
    IOptions<ClassroomOptions> classroomOptions,
    IWebHostEnvironment environment,
    ILogger<CloudLicenseStore> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _path = ResolvePath(environment.ContentRootPath, classroomOptions.Value.DataDirectory);
    private CloudLicenseState _state = CloudLicenseState.Empty;

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
            _state = await JsonSerializer.DeserializeAsync<CloudLicenseState>(stream, JsonOptions, cancellationToken)
                     ?? CloudLicenseState.Empty;
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "The locally cached cloud license is invalid and will be ignored.");
            _state = CloudLicenseState.Empty;
        }
    }

    public CloudLicenseState Snapshot() => _state;

    public async Task ApplyAsync(JsonElement? license, CancellationToken cancellationToken)
    {
        if (license is null || license.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            _state = new CloudLicenseState(license.Value.Clone(), DateTimeOffset.UtcNow);
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
        return Path.Combine(directory, "cloud-license.json");
    }
}

public sealed record CloudLicenseState(JsonElement? License, DateTimeOffset? ReceivedAtUtc)
{
    public static CloudLicenseState Empty { get; } = new(null, null);
}
