using System.Text.Json;
using IWebClassroom.Contracts;
using IWebClassroom.Server.Configuration;
using Microsoft.Extensions.Options;

namespace IWebClassroom.Server.Services;

public sealed class CommandAuditStore
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly string _filePath;

    public CommandAuditStore(IOptions<ClassroomOptions> options, IWebHostEnvironment environment)
    {
        var configuredPath = options.Value.DataDirectory;
        var directory = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath);
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "audit.jsonl");
    }

    public async Task AppendAsync(ClassroomCommand command, IReadOnlyCollection<Guid> targets, CancellationToken cancellationToken)
    {
        var entry = new
        {
            command.CommandId,
            command.Kind,
            Targets = targets,
            command.IssuedAtUtc
        };
        var line = JsonSerializer.Serialize(entry, JsonSerializerOptions.Web) + Environment.NewLine;

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(_filePath, line, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
