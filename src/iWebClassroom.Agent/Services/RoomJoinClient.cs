using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using IWebClassroom.Agent.Configuration;
using IWebClassroom.Agent.Localization;
using IWebClassroom.Contracts;

namespace IWebClassroom.Agent.Services;

public sealed class RoomJoinClient
{
    public async Task<IReadOnlyCollection<AvailableRoomSummary>> ListAsync(
        AgentSettings settings,
        CancellationToken cancellationToken)
    {
        using var client = CreateClient(settings);
        return await client.GetFromJsonAsync<AvailableRoomSummary[]>("/api/v1/rooms/available", cancellationToken) ?? [];
    }

    public async Task<LessonSessionSummary> JoinAsync(
        AgentSettings settings,
        Guid lessonId,
        string pin,
        CancellationToken cancellationToken)
    {
        using var client = CreateClient(settings);
        using var response = await client.PostAsJsonAsync("/api/v1/rooms/join", new JoinRoomRequest(lessonId, pin), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(AgentStrings.InvalidRoomPin);
        }

        return await response.Content.ReadFromJsonAsync<LessonSessionSummary>(cancellationToken)
               ?? throw new InvalidOperationException(AgentStrings.InvalidRoomPin);
    }

    private static HttpClient CreateClient(AgentSettings settings)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(settings.ServerUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(15)
        };
        client.DefaultRequestHeaders.Add(HubProtocol.AgentIdHeader, settings.AgentId.ToString());
        client.DefaultRequestHeaders.Add(HubProtocol.AgentTokenHeader, settings.AgentToken);
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(CultureInfo.CurrentUICulture.Name));
        return client;
    }
}
