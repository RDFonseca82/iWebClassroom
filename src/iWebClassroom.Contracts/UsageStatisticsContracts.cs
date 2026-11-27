namespace IWebClassroom.Contracts;

public sealed record AgentUsageSnapshot(
    string Date,
    int ProcessStarts,
    long ConnectedSeconds);
