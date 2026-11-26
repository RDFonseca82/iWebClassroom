using System.Collections.Concurrent;
using IWebClassroom.Contracts;
using IWebClassroom.Server.Configuration;
using IWebClassroom.Server.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace IWebClassroom.Server.Hubs;

public sealed class ClassroomHub(
    AgentRegistry registry,
    LessonRegistry lessons,
    StudentScreenStore studentScreens,
    ServerMaintenanceState maintenance,
    IOptionsMonitor<ClassroomOptions> options,
    ILogger<ClassroomHub> logger) : Hub
{
    private static readonly ConcurrentDictionary<string, long> LastFrameTicks = new(StringComparer.Ordinal);

    public override async Task OnConnectedAsync()
    {
        if (maintenance.IsUpdateDrain)
        {
            Context.Abort();
            return;
        }

        var httpContext = Context.GetHttpContext();
        var idValue = httpContext?.Request.Headers[HubProtocol.AgentIdHeader].ToString();
        var token = httpContext?.Request.Headers[HubProtocol.AgentTokenHeader].ToString();

        if (!Guid.TryParse(idValue, out var agentId) || !registry.TryAuthenticate(agentId, token, out var agent) || agent is null)
        {
            Context.Abort();
            return;
        }

        if (maintenance.IsUpdateDrain)
        {
            Context.Abort();
            return;
        }

        await registry.AttachConnectionAsync(agentId, Context.ConnectionId, Context.ConnectionAborted);
        if (maintenance.IsUpdateDrain)
        {
            await registry.DetachConnectionAsync(Context.ConnectionId, CancellationToken.None);
            Context.Abort();
            return;
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, AgentGroup(agentId), Context.ConnectionAborted);
        if (lessons.TryGetForAgent(agent, out var lesson))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, LessonGroup(lesson!.LessonId), Context.ConnectionAborted);
        }

        logger.LogInformation("Agent {AgentId} connected as {Role} with default room {ClassroomId}.", agentId, agent.Role, agent.ClassroomId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        LastFrameTicks.TryRemove(Context.ConnectionId, out _);
        if (registry.TryGetByConnection(Context.ConnectionId, out var disconnected) && disconnected is not null)
        {
            studentScreens.Remove(disconnected.AgentId);
        }
        await registry.DetachConnectionAsync(Context.ConnectionId, CancellationToken.None);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task PublishScreenFrame(byte[] jpegFrame, IReadOnlyCollection<Guid> viewerAgentIds)
    {
        if (!registry.TryGetByConnection(Context.ConnectionId, out var agent) ||
            agent is null ||
            !lessons.TryGetForAgent(agent, out var lesson) ||
            lesson!.TeacherAgentId != agent.AgentId)
        {
            throw new HubException("Only the teacher of an active lesson can publish a screen.");
        }

        if (jpegFrame.Length == 0 || jpegFrame.Length > options.CurrentValue.MaximumFrameBytes)
        {
            throw new HubException("The screen frame has an invalid size.");
        }

        var now = Environment.TickCount64;
        var previous = LastFrameTicks.GetOrAdd(Context.ConnectionId, 0);
        if (now - previous < options.CurrentValue.MinimumFrameIntervalMilliseconds)
        {
            return;
        }

        LastFrameTicks[Context.ConnectionId] = now;
        var viewers = viewerAgentIds.Distinct()
            .Where(agentId => agentId != agent.AgentId && lesson.MemberAgentIds.Contains(agentId))
            .Take(200)
            .Select(AgentGroup)
            .ToArray();
        if (viewers.Length == 0)
        {
            return;
        }
        await Clients.Groups(viewers)
            .SendAsync(HubProtocol.ScreenFrame, jpegFrame, Context.ConnectionAborted);
    }

    public async Task PublishScreenStopped(IReadOnlyCollection<Guid> viewerAgentIds)
    {
        if (!registry.TryGetByConnection(Context.ConnectionId, out var agent) ||
            agent is null ||
            !lessons.TryGetForAgent(agent, out var lesson) ||
            lesson!.TeacherAgentId != agent.AgentId)
        {
            throw new HubException("Only the teacher of an active lesson can stop a screen share.");
        }

        var viewers = viewerAgentIds.Distinct()
            .Where(agentId => agentId != agent.AgentId && lesson.MemberAgentIds.Contains(agentId))
            .Take(200)
            .Select(AgentGroup)
            .ToArray();
        await Clients.Groups(viewers)
            .SendAsync(HubProtocol.ScreenStopped, Context.ConnectionAborted);
    }

    public Task PublishStudentScreenFrame(byte[] jpegFrame)
    {
        if (!registry.TryGetByConnection(Context.ConnectionId, out var agent) || agent is null ||
            agent.Role != AgentRole.Student || !lessons.TryGetForAgent(agent, out var lesson) ||
            !studentScreens.CanPublish(lesson!.LessonId, agent.AgentId) ||
            jpegFrame.Length == 0 || jpegFrame.Length > options.CurrentValue.MaximumFrameBytes)
        {
            throw new HubException("Only a student in an active lesson can publish a monitor preview.");
        }

        studentScreens.Update(lesson.LessonId, agent.AgentId, jpegFrame);
        return Task.CompletedTask;
    }

    public static string AgentGroup(Guid agentId) => $"agent:{agentId:N}";

    public static string LessonGroup(Guid lessonId) => $"lesson:{lessonId:N}";
}
