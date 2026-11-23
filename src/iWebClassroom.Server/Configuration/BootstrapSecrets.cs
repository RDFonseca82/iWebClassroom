using System.Security.Cryptography;
using System.Text.Json;

namespace IWebClassroom.Server.Configuration;

public static class BootstrapSecrets
{
    private const string FileName = "bootstrap-secrets.json";
    private const string TeacherAccessFileName = "teacher-access.json";

    public static void AddOrCreate(WebApplicationBuilder builder)
    {
        var configuredDirectory = builder.Configuration["Classroom:DataDirectory"] ?? "data";
        var dataDirectory = Path.IsPathRooted(configuredDirectory)
            ? configuredDirectory
            : Path.Combine(builder.Environment.ContentRootPath, configuredDirectory);
        Directory.CreateDirectory(dataDirectory);

        var secretsPath = Path.Combine(dataDirectory, FileName);
        if (!File.Exists(secretsPath) && MissingConfiguredSecrets(builder.Configuration))
        {
            var secrets = new
            {
                Security = new
                {
                    AdminToken = CreateToken(),
                    EnrollmentCode = CreateToken()
                }
            };
            var temporaryPath = $"{secretsPath}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(secrets, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            }));
            File.Move(temporaryPath, secretsPath);
        }

        builder.Configuration.AddJsonFile(secretsPath, optional: true, reloadOnChange: true);
        EnsureTeacherAccessFile(builder, dataDirectory);
        builder.Configuration.AddEnvironmentVariables();
    }

    private static void EnsureTeacherAccessFile(WebApplicationBuilder builder, string dataDirectory)
    {
        var configuredPin = Environment.GetEnvironmentVariable("IWEBCLASSROOM_TEACHER_PIN")
                            ?? builder.Configuration["Security:TeacherPin"];
        var path = Path.Combine(dataDirectory, TeacherAccessFileName);
        if (string.IsNullOrWhiteSpace(configuredPin) && !File.Exists(path))
        {
            var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new
            {
                Security = new { TeacherPin = CreatePin() }
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            File.Move(temporaryPath, path);
        }

        builder.Configuration.AddJsonFile(path, optional: true, reloadOnChange: true);
    }

    private static bool MissingConfiguredSecrets(IConfiguration configuration) =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("IWEBCLASSROOM_ADMIN_TOKEN") ?? configuration["Security:AdminToken"]) ||
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("IWEBCLASSROOM_ENROLLMENT_CODE") ?? configuration["Security:EnrollmentCode"]);

    private static string CreateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(36));

    private static string CreatePin() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
}
