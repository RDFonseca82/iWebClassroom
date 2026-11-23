using System.Text.Json;
using System.ComponentModel;
using IWebClassroom.Agent.Security;

namespace IWebClassroom.Agent.Configuration;

public sealed class AgentSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "iWebClassroom",
        "Agent",
        "agent.json");
    private readonly string _legacyPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "iWebClassroom",
        "agent.json");

    public async Task<AgentSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(_path))
        {
            return await LoadFromAsync(_path, machineScope: true, cancellationToken);
        }

        if (!File.Exists(_legacyPath))
        {
            return AgentSettings.Empty;
        }

        var legacy = await LoadFromAsync(_legacyPath, machineScope: false, cancellationToken);
        if (legacy.AgentId != Guid.Empty)
        {
            try
            {
                await SaveAsync(legacy, cancellationToken);
            }
            catch (UnauthorizedAccessException)
            {
                // The updated MSI creates the shared data directory and its ACLs.
            }
        }

        return legacy;
    }

    public async Task SaveAsync(AgentSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            var persisted = new PersistedSettings(
                settings.ServerUrl,
                settings.AgentId,
                UserSecretProtector.Protect(settings.AgentToken),
                settings.DisplayName,
                settings.Role,
                settings.ClassroomId);
            await JsonSerializer.SerializeAsync(stream, persisted, JsonOptions, cancellationToken);
        }

        File.Move(temporaryPath, _path, true);
    }

    private static async Task<AgentSettings> LoadFromAsync(string path, bool machineScope, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var persisted = await JsonSerializer.DeserializeAsync<PersistedSettings>(stream, JsonOptions, cancellationToken);
            return persisted is null
                ? AgentSettings.Empty
                : new AgentSettings(
                    persisted.ServerUrl,
                    persisted.AgentId,
                    machineScope
                        ? UserSecretProtector.Unprotect(persisted.ProtectedAgentToken)
                        : UserSecretProtector.UnprotectForCurrentUser(persisted.ProtectedAgentToken),
                    persisted.DisplayName,
                    persisted.Role,
                    persisted.ClassroomId);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or Win32Exception or IOException or UnauthorizedAccessException)
        {
            return AgentSettings.Empty;
        }
    }

    private sealed record PersistedSettings(
        string ServerUrl,
        Guid AgentId,
        string ProtectedAgentToken,
        string DisplayName,
        IWebClassroom.Contracts.AgentRole Role,
        string ClassroomId);
}
