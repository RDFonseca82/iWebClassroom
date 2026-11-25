using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IWebClassroom.Agent.Configuration;
using IWebClassroom.Agent.Localization;
using IWebClassroom.Contracts;

namespace IWebClassroom.Agent.Services;

public sealed class RoomJoinClient
{
    public async Task<ResolveRoomResponse> ResolveAsync(
        AgentSettings settings,
        string roomCode,
        string studentName,
        CancellationToken cancellationToken)
    {
        using var client = CreateClient(settings);
        using var response = await client.PostAsJsonAsync(
            "/api/v1/rooms/resolve",
            new ResolveRoomRequest(roomCode, studentName),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ErrorAsync(response, cancellationToken));
        }

        return await response.Content.ReadFromJsonAsync<ResolveRoomResponse>(cancellationToken)
               ?? throw new InvalidOperationException(AgentStrings.InvalidRoomPin);
    }

    public async Task<RoamingJoinResponse> RoamAsync(
        Uri destination,
        string handoffToken,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient
        {
            BaseAddress = new Uri(destination.ToString().TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(20)
        };
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(CultureInfo.CurrentUICulture.Name));
        using var response = await client.PostAsJsonAsync(
            "/api/v1/rooms/roam",
            new RoamingJoinRequest(handoffToken),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ErrorAsync(response, cancellationToken));
        }

        return await response.Content.ReadFromJsonAsync<RoamingJoinResponse>(cancellationToken)
               ?? throw new InvalidOperationException(AgentStrings.RoomLookupUnavailable);
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

    private static async Task<string> ErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            if (body.TryGetProperty("error", out var error) && !string.IsNullOrWhiteSpace(error.GetString()))
            {
                return error.GetString()!;
            }
            if (body.TryGetProperty("detail", out var detail) && !string.IsNullOrWhiteSpace(detail.GetString()))
            {
                return detail.GetString()!;
            }
        }
        catch (JsonException)
        {
            // Fall back to the localized generic message.
        }

        return response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable
            ? AgentStrings.RoomLookupUnavailable
            : AgentStrings.InvalidRoomPin;
    }
}
