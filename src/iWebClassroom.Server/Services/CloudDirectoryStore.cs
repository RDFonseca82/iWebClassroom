using System.Text.Json;
using System.Text.Json.Serialization;
using IWebClassroom.Server.Configuration;
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
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "The synchronized school directory is invalid and will be ignored.");
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
                await JsonSerializer.SerializeAsync(stream, _directory, JsonOptions, cancellationToken);
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
    DateTimeOffset? UpdatedAtUtc)
{
    public static CloudDirectoryResponse Empty { get; } = new([], [], [], null);
}

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
