using IWebClassroom.Contracts;
using IWebClassroom.Server.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace IWebClassroom.Server.Services;

public sealed class LessonExpirationWorker(
    LessonRegistry lessons,
    TeacherSessionService teacherSessions,
    AgentRegistry agents,
    CloudRoomService cloudRooms,
    StudentScreenStore studentScreens,
    LessonMediaStore media,
    IHubContext<ClassroomHub> hub,
    ILogger<LessonExpirationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var expired = await lessons.EndExpiredAsync(stoppingToken);
            var disconnected = await lessons.EndTemporaryForOfflineTeachersAsync(teacherSessions, stoppingToken);
            foreach (var lesson in expired.Concat(disconnected))
            {
                studentScreens.RemoveLesson(lesson.LessonId);
                media.RemoveLesson(lesson.LessonId);
                if (lesson.IsTemporary)
                {
                    await cloudRooms.EndAsync(lesson.LessonId, stoppingToken);
                }
                await hub.Clients.Group(ClassroomHub.LessonGroup(lesson.LessonId))
                    .SendAsync(HubProtocol.ScreenStopped, stoppingToken);
                foreach (var kind in new[] { ClassroomCommandKind.StopBlackout, ClassroomCommandKind.StopStudentView, ClassroomCommandKind.StopMedia })
                {
                    var cleanup = new ClassroomCommand(Guid.NewGuid(), kind, System.Text.Json.JsonSerializer.SerializeToElement(new { }), DateTimeOffset.UtcNow);
                    await hub.Clients.Group(ClassroomHub.LessonGroup(lesson.LessonId))
                        .SendAsync(HubProtocol.ReceiveCommand, cleanup, stoppingToken);
                }
                var stopShare = new ClassroomCommand(Guid.NewGuid(), ClassroomCommandKind.StopScreenShare, System.Text.Json.JsonSerializer.SerializeToElement(new { }), DateTimeOffset.UtcNow);
                await hub.Clients.Group(ClassroomHub.AgentGroup(lesson.TeacherAgentId))
                    .SendAsync(HubProtocol.ReceiveCommand, stopShare, stoppingToken);
                foreach (var agentId in lesson.MemberAgentIds)
                {
                    foreach (var connectionId in agents.GetConnectionIds(agentId))
                    {
                        await hub.Groups.RemoveFromGroupAsync(connectionId, ClassroomHub.LessonGroup(lesson.LessonId), stoppingToken);
                    }
                }
                await agents.RemoveRoamingAsync(lesson.MemberAgentIds, stoppingToken);

                logger.LogInformation("Lesson {LessonId} in room {RoomId} expired.", lesson.LessonId, lesson.RoomId);
            }
        }
    }
}
