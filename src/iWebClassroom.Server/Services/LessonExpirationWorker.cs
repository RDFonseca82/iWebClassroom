using IWebClassroom.Contracts;
using IWebClassroom.Server.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace IWebClassroom.Server.Services;

public sealed class LessonExpirationWorker(
    LessonRegistry lessons,
    AgentRegistry agents,
    IHubContext<ClassroomHub> hub,
    ILogger<LessonExpirationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var expired = await lessons.EndExpiredAsync(stoppingToken);
            foreach (var lesson in expired)
            {
                await hub.Clients.Group(ClassroomHub.LessonGroup(lesson.LessonId))
                    .SendAsync(HubProtocol.ScreenStopped, stoppingToken);
                foreach (var agentId in lesson.MemberAgentIds)
                {
                    foreach (var connectionId in agents.GetConnectionIds(agentId))
                    {
                        await hub.Groups.RemoveFromGroupAsync(connectionId, ClassroomHub.LessonGroup(lesson.LessonId), stoppingToken);
                    }
                }

                logger.LogInformation("Lesson {LessonId} in room {RoomId} expired.", lesson.LessonId, lesson.RoomId);
            }
        }
    }
}
