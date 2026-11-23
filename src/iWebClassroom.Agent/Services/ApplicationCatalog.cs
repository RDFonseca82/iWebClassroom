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

    public void Launch(string applicationId)
    {
        if (!_entries.TryGetValue(applicationId, out var entry))
        {
            throw new InvalidOperationException(AgentStrings.Format(nameof(AgentStrings.AppNotAllowed), applicationId));
        }

        var fileName = Environment.ExpandEnvironmentVariables(entry.FileName);
        if (!Path.IsPathFullyQualified(fileName) || !File.Exists(fileName))
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
