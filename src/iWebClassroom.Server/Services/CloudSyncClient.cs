using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IWebClassroom.Server.Configuration;
using Microsoft.Extensions.Options;

namespace IWebClassroom.Server.Services;

public sealed class CloudSyncClient(IHttpClientFactory httpClientFactory, IOptionsMonitor<CloudSyncOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CloudAuthResponse> AuthenticateAsync(CloudAuthRequest request, CancellationToken cancellationToken)
    {
        var client = CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/server/auth", request, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CloudAuthResponse>(JsonOptions, cancellationToken)
               ?? throw new InvalidOperationException("The cloud returned an empty authentication response.");
    }

    public async Task<CloudSyncResponse> SynchronizeAsync(
        Guid serverId,
        string accessToken,
        CloudSyncRequest request,
        CancellationToken cancellationToken)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Add("X-iWebClassroom-Server", serverId.ToString());
        using var response = await client.PostAsJsonAsync("/api/v1/server/sync", request, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CloudSyncResponse>(JsonOptions, cancellationToken)
               ?? throw new InvalidOperationException("The cloud returned an empty synchronization response.");
    }

    private HttpClient CreateClient()
    {
        var policy = options.CurrentValue;
        if (!Uri.TryCreate(policy.BaseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("CloudSync:BaseUrl must be an absolute HTTPS address.");
        }

        var client = httpClientFactory.CreateClient(nameof(CloudSyncClient));
        client.BaseAddress = baseUri;
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(policy.RequestTimeoutSeconds, 5, 120));
        return client;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(body);
            body = document.RootElement.TryGetProperty("message", out var message) ? message.GetString() ?? body : body;
        }
        catch (JsonException)
        {
            // Keep the plain response body.
        }

        throw new HttpRequestException($"Cloud API returned {(int)response.StatusCode}: {body}", null, response.StatusCode);
    }
}
