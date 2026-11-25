using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Globalization;
using IWebClassroom.Agent.Configuration;
using IWebClassroom.Agent.Localization;
using IWebClassroom.Contracts;

namespace IWebClassroom.Agent.Services;

public sealed class EnrollmentClient
{
    private readonly ServerTrustBootstrapper _trustBootstrapper = new();

    public async Task<AgentSettings> EnrollAsync(
        Uri serverUri,
        string enrollmentCode,
        string displayName,
        AgentRole role,
        string classroomId,
        bool isShared,
        CancellationToken cancellationToken)
    {
        serverUri = await _trustBootstrapper.EnsureTrustedAsync(serverUri, enrollmentCode, cancellationToken);
        using var client = new HttpClient { BaseAddress = serverUri, Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(CultureInfo.CurrentUICulture.Name));
        var request = new EnrollmentRequest(MachineIdentity.Get(), displayName, role, classroomId, enrollmentCode, isShared);
        using var response = await client.PostAsJsonAsync("/api/v1/enrollment", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => AgentStrings.EnrollmentInvalid,
                System.Net.HttpStatusCode.ServiceUnavailable => AgentStrings.EnrollmentUnavailable,
                _ => AgentStrings.Format(nameof(AgentStrings.EnrollmentFailed), (int)response.StatusCode)
            });
        }

        var enrollment = await response.Content.ReadFromJsonAsync<EnrollmentResponse>(cancellationToken)
                         ?? throw new InvalidOperationException(AgentStrings.EmptyEnrollmentResponse);
        return new AgentSettings(
            serverUri.ToString().TrimEnd('/'),
            enrollment.AgentId,
            enrollment.AgentToken,
            displayName,
            role,
            classroomId,
            isShared);
    }

    public async Task<AgentSummary> GetProfileAsync(AgentSettings settings, CancellationToken cancellationToken)
    {
        using var client = new HttpClient
        {
            BaseAddress = new Uri(settings.ServerUrl),
            Timeout = TimeSpan.FromSeconds(20)
        };
        client.DefaultRequestHeaders.Add(HubProtocol.AgentIdHeader, settings.AgentId.ToString());
        client.DefaultRequestHeaders.Add(HubProtocol.AgentTokenHeader, settings.AgentToken);
        using var response = await client.GetAsync("/api/v1/agent/profile", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(AgentStrings.Format(nameof(AgentStrings.EnrollmentFailed), (int)response.StatusCode));
        }

        return await response.Content.ReadFromJsonAsync<AgentSummary>(cancellationToken)
               ?? throw new InvalidOperationException(AgentStrings.EmptyEnrollmentResponse);
    }
}
