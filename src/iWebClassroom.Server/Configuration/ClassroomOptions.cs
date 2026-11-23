namespace IWebClassroom.Server.Configuration;

public sealed class ClassroomOptions
{
    public string DataDirectory { get; init; } = "data";

    public string ReleaseDirectory { get; init; } = "releases";

    public int MaximumFrameBytes { get; init; } = 1_500_000;

    public int MinimumFrameIntervalMilliseconds { get; init; } = 150;

    public IReadOnlyCollection<string> AllowedApplicationIds { get; init; } = ["notepad", "calculator"];
}
