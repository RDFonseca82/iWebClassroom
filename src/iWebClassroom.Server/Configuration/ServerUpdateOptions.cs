namespace IWebClassroom.Server.Configuration;

public sealed class ServerUpdateOptions
{
    public bool Enabled { get; init; }

    public string ManifestUrl { get; init; } = string.Empty;

    public string SignerThumbprint { get; init; } = string.Empty;

    public int CheckIntervalHours { get; init; } = 6;

    public string ScheduledTaskName { get; init; } = "iWebClassroom Server";
}
