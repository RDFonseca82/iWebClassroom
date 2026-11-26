using System.Diagnostics;
using System.Text.Json;
using IWebClassroom.Agent.Localization;

namespace IWebClassroom.Agent.Services;

public sealed class ApplicationCatalog
{
    private readonly IReadOnlyDictionary<string, ApplicationEntry> _entries;

    public ApplicationCatalog()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "apps.json");
        var entries = File.Exists(path)
            ? JsonSerializer.Deserialize<List<ApplicationEntry>>(File.ReadAllText(path), JsonSerializerOptions.Web) ?? []
            : [];
        _entries = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
            .GroupBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    public void Launch(string applicationId, string? synchronizedFileName = null, string? synchronizedArguments = null)
    {
        ApplicationEntry? entry = null;
        if (!string.IsNullOrWhiteSpace(synchronizedFileName))
        {
            entry = new ApplicationEntry(applicationId, applicationId, synchronizedFileName, synchronizedArguments);
        }
        else if (_entries.TryGetValue(applicationId, out var localEntry))
        {
            entry = localEntry;
        }
        if (entry is null)
        {
            throw new InvalidOperationException(AgentStrings.Format(nameof(AgentStrings.AppNotAllowed), applicationId));
        }

        var fileName = Environment.ExpandEnvironmentVariables(entry.FileName);
        if (!Path.IsPathFullyQualified(fileName) || !File.Exists(fileName) ||
            !string.Equals(Path.GetExtension(fileName), ".exe", StringComparison.OrdinalIgnoreCase) ||
            (entry.Arguments?.Length ?? 0) > 500)
        {
            throw new InvalidOperationException(AgentStrings.Format(nameof(AgentStrings.AppNotFound), entry.Id));
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = entry.Arguments ?? string.Empty,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(fileName)!
        });
    }

    private sealed record ApplicationEntry(string Id, string DisplayName, string FileName, string? Arguments);
}
