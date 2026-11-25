using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using IWebClassroom.Contracts;
using IWebClassroom.Server.Configuration;
using IWebClassroom.Server.Security;
using Microsoft.Extensions.Options;

namespace IWebClassroom.Server.Services;

public sealed class AgentRegistry(
    IOptions<ClassroomOptions> options,
    IWebHostEnvironment environment,
    ILogger<AgentRegistry> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ConcurrentDictionary<Guid, PersistedAgent> _agents = new();
    private readonly ConcurrentDictionary<string, Guid> _connections = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _persistenceLock = new(1, 1);
    private readonly string _filePath = ResolvePath(environment.ContentRootPath, options.Value.DataDirectory, "agents.json");

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        if (!File.Exists(_filePath))
        {
            return;
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var agents = await JsonSerializer.DeserializeAsync<List<PersistedAgent>>(stream, JsonOptions, cancellationToken)
                         ?? [];
            foreach (var agent in agents)
            {
                _agents[agent.AgentId] = agent with { IsOnline = false };
            }
        }
        catch (JsonException exception)
        {
            logger.LogCritical(exception, "The agent registry at {RegistryPath} is not valid JSON.", _filePath);
            throw;
        }
    }

    public async Task<EnrollmentResponse> EnrollAsync(EnrollmentRequest request, CancellationToken cancellationToken)
    {
        var existing = _agents.Values.FirstOrDefault(item =>
            string.Equals(item.MachineId, request.MachineId, StringComparison.OrdinalIgnoreCase));
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var now = DateTimeOffset.UtcNow;
        var agent = new PersistedAgent(
            existing?.AgentId ?? Guid.NewGuid(),
            request.MachineId,
            request.DisplayName,
            request.Role,
            request.ClassroomId,
            request.IsShared,
            SecretComparer.Hash(token),
            false,
            now);

        _agents[agent.AgentId] = agent;
        await PersistAsync(cancellationToken);
        return new EnrollmentResponse(
            agent.AgentId,
            token,
            typeof(AgentRegistry).Assembly.GetName().Version?.ToString() ?? "0.0.0");
    }

    public bool TryAuthenticate(Guid agentId, string? token, out AgentSummary? agent)
    {
        agent = null;
        if (!_agents.TryGetValue(agentId, out var persisted) ||
            string.IsNullOrWhiteSpace(token) ||
            !SecretComparer.Equals(SecretComparer.Hash(token), persisted.TokenHash))
        {
            return false;
        }

        agent = ToSummary(persisted);
        return true;
    }

    public async Task AttachConnectionAsync(Guid agentId, string connectionId, CancellationToken cancellationToken)
    {
        if (!_agents.TryGetValue(agentId, out var agent))
        {
            return;
        }

        _connections[connectionId] = agentId;
        _agents[agentId] = agent with { IsOnline = true, LastSeenUtc = DateTimeOffset.UtcNow };
        await PersistAsync(cancellationToken);
    }

    public async Task DetachConnectionAsync(string connectionId, CancellationToken cancellationToken)
    {
        if (!_connections.TryRemove(connectionId, out var agentId) || !_agents.TryGetValue(agentId, out var agent))
        {
            return;
        }

        var stillOnline = _connections.Values.Any(value => value == agentId);
        _agents[agentId] = agent with { IsOnline = stillOnline, LastSeenUtc = DateTimeOffset.UtcNow };
        await PersistAsync(cancellationToken);
    }

    public bool TryGetByConnection(string connectionId, out AgentSummary? agent)
    {
        agent = null;
        if (!_connections.TryGetValue(connectionId, out var agentId) || !_agents.TryGetValue(agentId, out var persisted))
        {
            return false;
        }

        agent = ToSummary(persisted);
        return true;
    }

    public IReadOnlyCollection<string> GetConnectionIds(Guid agentId) =>
        _connections
            .Where(item => item.Value == agentId)
            .Select(item => item.Key)
            .ToArray();

    public IReadOnlyCollection<AgentSummary> List() =>
        _agents.Values
            .OrderBy(item => item.ClassroomId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(ToSummary)
            .ToArray();

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        await _persistenceLock.WaitAsync(cancellationToken);
        try
        {
            var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, _agents.Values.OrderBy(item => item.AgentId), JsonOptions, cancellationToken);
            }

            File.Move(temporaryPath, _filePath, true);
        }
        finally
        {
            _persistenceLock.Release();
        }
    }

    private static AgentSummary ToSummary(PersistedAgent agent) =>
        new(agent.AgentId, agent.MachineId, agent.DisplayName, agent.Role, agent.ClassroomId, agent.IsShared, agent.IsOnline, agent.LastSeenUtc);

    private static string ResolvePath(string root, string configuredPath, string fileName)
    {
        var directory = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(root, configuredPath);
        return Path.Combine(directory, fileName);
    }

    private sealed record PersistedAgent(
        Guid AgentId,
        string MachineId,
        string DisplayName,
        AgentRole Role,
        string ClassroomId,
        bool IsShared,
        string TokenHash,
        bool IsOnline,
        DateTimeOffset LastSeenUtc);
}
