using System.Net.Http.Json;
using IWebClassroom.Agent.Configuration;
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
        var request = new EnrollmentRequest(MachineIdentity.Get(), displayName, role, classroomId, enrollmentCode);
        using var response = await client.PostAsJsonAsync("/api/v1/enrollment", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => "O código de inscrição não é válido.",
                System.Net.HttpStatusCode.ServiceUnavailable => "O servidor ainda não tem a inscrição configurada.",
                _ => $"A inscrição falhou ({(int)response.StatusCode})."
            });
        }

        var enrollment = await response.Content.ReadFromJsonAsync<EnrollmentResponse>(cancellationToken)
                         ?? throw new InvalidOperationException("O servidor devolveu uma resposta de inscrição vazia.");
        return new AgentSettings(
            serverUri.ToString().TrimEnd('/'),
            enrollment.AgentId,
            enrollment.AgentToken,
            displayName,
            role,
            classroomId);
    }
}
