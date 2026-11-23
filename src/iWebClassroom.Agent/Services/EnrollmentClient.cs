using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Globalization;
using IWebClassroom.Agent.Configuration;
using IWebClassroom.Agent.Localization;
using IWebClassroom.Contracts;

namespace IWebClassroom.Agent.Services;

public sealed class EnrollmentClient
{
    public async Task<AgentSettings> EnrollAsync(
        Uri serverUri,
        string enrollmentCode,
        string displayName,
        AgentRole role,
        string classroomId,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient { BaseAddress = serverUri, Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(CultureInfo.CurrentUICulture.Name));
        var request = new EnrollmentRequest(MachineIdentity.Get(), displayName, role, classroomId, enrollmentCode);
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
            classroomId);
    }
}
