using System.Collections.Concurrent;
using System.Security.Cryptography;
using IWebClassroom.Server.Configuration;
using Microsoft.Extensions.Options;

namespace IWebClassroom.Server.Services;

public sealed class LessonMediaStore
{
    private static readonly IReadOnlyDictionary<string, string> AllowedTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/gif"] = ".gif",
        ["image/bmp"] = ".bmp",
        ["video/mp4"] = ".mp4",
        ["video/x-ms-wmv"] = ".wmv"
    };

    private readonly ConcurrentDictionary<Guid, LessonMediaEntry> _entries = new();
    private readonly string _directory;

    public LessonMediaStore(IOptions<ClassroomOptions> options, IWebHostEnvironment environment)
    {
        var dataDirectory = Path.IsPathRooted(options.Value.DataDirectory)
            ? options.Value.DataDirectory
            : Path.Combine(environment.ContentRootPath, options.Value.DataDirectory);
        _directory = Path.Combine(dataDirectory, "lesson-media");
        Directory.CreateDirectory(_directory);
        foreach (var file in Directory.GetFiles(_directory))
        {
            try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    public async Task<LessonMediaEntry> CreateAsync(
        Guid lessonId,
        string fileName,
        string contentType,
        Stream content,
        IReadOnlyCollection<Guid> targetAgentIds,
        CancellationToken cancellationToken)
    {
        if (!AllowedTypes.TryGetValue(contentType, out var extension))
        {
            throw new InvalidOperationException("Unsupported media type.");
        }

        var id = Guid.NewGuid();
        var path = Path.Combine(_directory, id.ToString("N") + extension);
        await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 81920, true))
        {
            await content.CopyToAsync(output, cancellationToken);
        }

        var length = new FileInfo(path).Length;
        if (length is < 1 or > 25_000_000)
        {
            File.Delete(path);
            throw new InvalidOperationException("Media files must contain between 1 byte and 25 MB.");
        }

        await using var hashStream = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(hashStream, cancellationToken));
        var entry = new LessonMediaEntry(
            id, lessonId, Path.GetFileName(fileName), contentType, path, hash,
            targetAgentIds.Distinct().ToArray(), DateTimeOffset.UtcNow);
        _entries[id] = entry;
        return entry;
    }

    public bool TryOpen(Guid mediaId, Guid lessonId, Guid agentId, out LessonMediaEntry? entry)
    {
        entry = null;
        if (!_entries.TryGetValue(mediaId, out var candidate) || candidate.LessonId != lessonId ||
            !candidate.TargetAgentIds.Contains(agentId) || !File.Exists(candidate.Path))
        {
            return false;
        }
        entry = candidate;
        return true;
    }

    public void RemoveLesson(Guid lessonId)
    {
        foreach (var item in _entries.Where(item => item.Value.LessonId == lessonId).ToArray())
        {
            if (_entries.TryRemove(item.Key, out var removed))
            {
                try { File.Delete(removed.Path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}

public sealed record LessonMediaEntry(
    Guid MediaId,
    Guid LessonId,
    string FileName,
    string ContentType,
    string Path,
    string Sha256,
    IReadOnlyCollection<Guid> TargetAgentIds,
    DateTimeOffset CreatedAtUtc);
