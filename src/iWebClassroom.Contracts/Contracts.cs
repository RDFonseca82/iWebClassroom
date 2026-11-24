using System.Text.Json;

namespace IWebClassroom.Contracts;

public sealed record EnrollmentRequest(
    string MachineId,
    string DisplayName,
    AgentRole Role,
    string ClassroomId,
    string EnrollmentCode);

public sealed record EnrollmentResponse(
    Guid AgentId,
    string AgentToken,
    string ServerVersion);

public sealed record AgentSummary(
    Guid AgentId,
    string MachineId,
    string DisplayName,
    AgentRole Role,
    string ClassroomId,
    bool IsOnline,
    DateTimeOffset LastSeenUtc);

public sealed record ClassroomCommand(
    Guid CommandId,
    ClassroomCommandKind Kind,
    JsonElement Payload,
    DateTimeOffset IssuedAtUtc);

public sealed record CommandRequest(
    IReadOnlyCollection<Guid> TargetAgentIds,
    ClassroomCommandKind Kind,
    JsonElement Payload);

public sealed record StartLessonRequest(
    string RoomId,
    Guid TeacherAgentId,
    int DurationMinutes = 180);

public sealed record LessonSessionSummary(
    Guid LessonId,
    string RoomId,
    Guid TeacherAgentId,
    string TeacherDisplayName,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    IReadOnlyCollection<Guid> MemberAgentIds);

public sealed record TeacherOverview(
    IReadOnlyCollection<AgentSummary> Agents,
    IReadOnlyCollection<LessonSessionSummary> Lessons,
    int DefaultLessonDurationMinutes);

public sealed record UpdateManifest(
    string Component,
    string Channel,
    string Version,
    Uri DownloadUrl,
    string Sha256,
    string SignerThumbprint,
    bool Mandatory,
    DateTimeOffset PublishedAtUtc);
