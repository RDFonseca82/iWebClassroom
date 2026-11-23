using IWebClassroom.Contracts;

namespace IWebClassroom.Agent.Configuration;

public sealed record AgentSettings(
    string ServerUrl,
    Guid AgentId,
    string AgentToken,
    string DisplayName,
    AgentRole Role,
    string ClassroomId)
{
    public static AgentSettings Empty { get; } = new(
        "https://localhost:7443",
        Guid.Empty,
        string.Empty,
        Environment.MachineName,
        AgentRole.Student,
        "SALA-1");
}
