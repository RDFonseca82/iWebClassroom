using System.Text.Json;
using IWebClassroom.Server.Configuration;
using Microsoft.Extensions.Options;

namespace IWebClassroom.Server.Services;

public sealed class CloudConfigurationStore(
    IConfiguration configuration,
    IOptions<ClassroomOptions> classroomOptions,
    IWebHostEnvironment environment)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _path = ResolvePath(environment.ContentRootPath, classroomOptions.Value.DataDirectory);
    private CloudServerSettings _current = CloudServerSettings.FromConfiguration(configuration);

    public CloudServerSettings Current => _current;

    public async Task ApplyAsync(CloudServerSettings settings, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var persisted = new
            {
                Security = new { settings.Security.RequireHttps },
                Classroom = new
                {
                    settings.Classroom.MaximumFrameBytes,
                    settings.Classroom.MinimumFrameIntervalMilliseconds,
                    settings.Classroom.DefaultLessonDurationMinutes,
                    AllowedApplicationIds = settings.Classroom.AllowedApplicationIds.ToArray()
                },
                Update = new
                {
                    settings.Update.Enabled,
                    settings.Update.ManifestUrl,
                    settings.Update.SignerThumbprint,
                    settings.Update.CheckIntervalHours
                }
            };
            var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(persisted, JsonOptions), cancellationToken);
            File.Move(temporaryPath, _path, true);
            _current = settings;
        }
        finally
        {
            _lock.Release();
        }
    }

    public static string ResolvePath(string contentRootPath, string configuredDataDirectory)
    {
        var directory = Path.IsPathRooted(configuredDataDirectory)
            ? configuredDataDirectory
            : Path.Combine(contentRootPath, configuredDataDirectory);
        return Path.Combine(directory, "cloud-settings.json");
    }
}
