namespace IWebClassroom.Server.Configuration;

public sealed class CloudSyncOptions
{
    public bool Enabled { get; init; } = true;

    public string BaseUrl { get; init; } = "https://cloud.iwebclassroom.app";

    public int IntervalSeconds { get; init; } = 300;

    public int DirectoryIntervalSeconds { get; init; } = 45;

    public int RequestTimeoutSeconds { get; init; } = 30;
}
