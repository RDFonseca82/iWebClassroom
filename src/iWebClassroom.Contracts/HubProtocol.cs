namespace IWebClassroom.Contracts;

public static class HubProtocol
{
    public const string HubPath = "/hubs/classroom";
    public const string ReceiveCommand = "ReceiveCommand";
    public const string ScreenFrame = "ScreenFrame";
    public const string ScreenStopped = "ScreenStopped";
    public const string PublishScreenFrame = "PublishScreenFrame";
    public const string PublishScreenStopped = "PublishScreenStopped";
    public const string PublishStudentScreenFrame = "PublishStudentScreenFrame";

    public const string AgentIdHeader = "X-Agent-Id";
    public const string AgentTokenHeader = "X-Agent-Token";
    public const string AdminTokenHeader = "X-Admin-Token";
    public const string TeacherSessionHeader = "X-Teacher-Session";
}
