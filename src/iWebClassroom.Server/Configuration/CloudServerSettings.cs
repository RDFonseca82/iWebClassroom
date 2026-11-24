using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IWebClassroom.Server.Configuration;

public sealed record CloudServerSettings(
    [property: JsonPropertyName("security")] CloudSecuritySettings Security,
    [property: JsonPropertyName("classroom")] CloudClassroomSettings Classroom,
    [property: JsonPropertyName("update")] CloudUpdateSettings Update)
{
    public static CloudServerSettings FromConfiguration(IConfiguration configuration) => new(
        new CloudSecuritySettings(configuration.GetValue("Security:RequireHttps", true)),
        new CloudClassroomSettings(
            configuration.GetValue("Classroom:MaximumFrameBytes", 1_500_000),
            configuration.GetValue("Classroom:MinimumFrameIntervalMilliseconds", 150),
            configuration.GetValue("Classroom:DefaultLessonDurationMinutes", 180),
            configuration.GetSection("Classroom:AllowedApplicationIds").Get<string[]>() ?? ["notepad", "calculator"]),
        new CloudUpdateSettings(
            configuration.GetValue("Update:Enabled", false),
            configuration["Update:ManifestUrl"] ?? string.Empty,
            configuration["Update:SignerThumbprint"] ?? string.Empty,
            configuration.GetValue("Update:CheckIntervalHours", 6)));

    public string Hash()
    {
        var json = JsonSerializer.Serialize(this, JsonSerializerOptions.Web);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
}

public sealed record CloudSecuritySettings(
    [property: JsonPropertyName("require_https")] bool RequireHttps);

public sealed record CloudClassroomSettings(
    [property: JsonPropertyName("maximum_frame_bytes")] int MaximumFrameBytes,
    [property: JsonPropertyName("minimum_frame_interval_milliseconds")] int MinimumFrameIntervalMilliseconds,
    [property: JsonPropertyName("default_lesson_duration_minutes")] int DefaultLessonDurationMinutes,
    [property: JsonPropertyName("allowed_application_ids")] IReadOnlyCollection<string> AllowedApplicationIds);

public sealed record CloudUpdateSettings(
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("manifest_url")] string ManifestUrl,
    [property: JsonPropertyName("signer_thumbprint")] string SignerThumbprint,
    [property: JsonPropertyName("check_interval_hours")] int CheckIntervalHours);
