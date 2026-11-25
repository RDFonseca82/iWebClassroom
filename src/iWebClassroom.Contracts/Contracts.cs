using System.Text.Json;

namespace IWebClassroom.Contracts;

public sealed record EnrollmentRequest(
    string MachineId,
    string DisplayName,
    AgentRole Role,
    string ClassroomId,
    string EnrollmentCode,
    bool IsShared = false);

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
    bool IsShared,
    bool IsOnline,
    DateTimeOffset LastSeenUtc,
    bool IsRoaming = false);

public sealed record TeacherAccessRequest(string Email);

public sealed record TeacherAccessVerifyRequest(string Email, string Code);

public sealed record TeacherAccessChallengeResponse(DateTimeOffset ExpiresAtUtc);

public sealed record TeacherSessionResponse(
    string SessionToken,
    string TeacherId,
    string TeacherName,
    DateTimeOffset ExpiresAtUtc);

public sealed record TeacherIdentity(string TeacherId, string DisplayName);

public sealed record ClassroomDefinition(
    string ClassroomId,
    string Code,
    string Name);

public sealed record AvailableRoomSummary(
    Guid LessonId,
    string RoomId,
    string RoomName,
    string TeacherDisplayName,
    DateTimeOffset ExpiresAtUtc);

public sealed record JoinRoomRequest(Guid LessonId, string Pin, string StudentName);

public sealed record ResolveRoomRequest(string Code, string StudentName);

public sealed record ResolveRoomResponse(
    bool IsLocal,
    string RoomName,
    IReadOnlyCollection<string> ServerUrls,
    string? HandoffToken,
    LessonSessionSummary? Lesson);

public sealed record RoamingJoinRequest(string HandoffToken);

public sealed record RoamingJoinResponse(
    string ServerUrl,
    EnrollmentResponse Enrollment,
    LessonSessionSummary Lesson);

public sealed record LessonParticipantSummary(Guid AgentId, string StudentName);

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
    int DurationMinutes = 180,
    bool IsTemporary = false,
    string? RoomName = null);

public sealed record LessonSessionSummary(
    Guid LessonId,
    string RoomId,
    Guid TeacherAgentId,
    string TeacherId,
    string TeacherDisplayName,
    string RoomName,
    bool IsTemporary,
    string? JoinCode,
    bool IsCloudRouted,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    IReadOnlyCollection<Guid> MemberAgentIds,
    IReadOnlyCollection<LessonParticipantSummary> Participants);

public sealed record TeacherOverview(
    IReadOnlyCollection<AgentSummary> Agents,
    IReadOnlyCollection<LessonSessionSummary> Lessons,
    IReadOnlyCollection<ClassroomDefinition> FixedRooms,
    TeacherIdentity Teacher,
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
