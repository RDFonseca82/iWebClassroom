using System.Text.Json;
using IWebClassroom.Contracts;
using IWebClassroom.Server.Configuration;
using Microsoft.Extensions.Options;

namespace IWebClassroom.Server.Services;

public sealed class UpdateCatalog(IOptions<ClassroomOptions> options, IWebHostEnvironment environment)
{
    private readonly string _directory = Path.IsPathRooted(options.Value.ReleaseDirectory)
        ? options.Value.ReleaseDirectory
        : Path.Combine(environment.ContentRootPath, options.Value.ReleaseDirectory);

    public async Task<UpdateManifest?> FindAsync(string component, string channel, CancellationToken cancellationToken)
    {
        if (!IsSafeName(component) || !IsSafeName(channel))
        {
            return null;
        }

        var path = Path.Combine(_directory, $"{component}-{channel}.json");
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, JsonSerializerOptions.Web, cancellationToken);
    }

    private static bool IsSafeName(string value) =>
        value.Length is > 0 and <= 32 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
