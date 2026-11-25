using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using IWebClassroom.Agent.Localization;
using IWebClassroom.Contracts;

namespace IWebClassroom.Agent.Services;

public sealed class ServerTrustBootstrapper
{
    public async Task<Uri> EnsureTrustedAsync(Uri configuredServer, string enrollmentCode, CancellationToken cancellationToken)
        => await EnsureTrustedAsync(configuredServer, request => request.Headers.Add("X-Enrollment-Code", enrollmentCode), cancellationToken);

    public async Task<Uri> EnsureTrustedAsync(
        Uri configuredServer,
        Guid agentId,
        string agentToken,
        CancellationToken cancellationToken) =>
        await EnsureTrustedAsync(configuredServer, request =>
        {
            request.Headers.Add(HubProtocol.AgentIdHeader, agentId.ToString());
            request.Headers.Add(HubProtocol.AgentTokenHeader, agentToken);
        }, cancellationToken);

    private static async Task<Uri> EnsureTrustedAsync(
        Uri configuredServer,
        Action<HttpRequestMessage> authorize,
        CancellationToken cancellationToken)
    {
        var secureServer = SecureServerUri(configuredServer);
        var bootstrapServer = new UriBuilder(secureServer)
        {
            Scheme = Uri.UriSchemeHttp,
            Port = 7080
        }.Uri;

        using var client = new HttpClient { BaseAddress = bootstrapServer, Timeout = TimeSpan.FromSeconds(15) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/certificate/root");
        authorize(request);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/pkix-cert"));
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => AgentStrings.EnrollmentInvalid,
                _ => AgentStrings.CertificateBootstrapFailed
            });
        }

        var certificateBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        using var certificate = X509CertificateLoader.LoadCertificate(certificateBytes);
        var constraints = certificate.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
        if (constraints is null || !constraints.CertificateAuthority ||
            !certificate.Subject.Contains("iWebClassroom Local School CA", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(AgentStrings.InvalidServerCertificate);
        }

        using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        var existing = store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, false);
        if (existing.Count == 0)
        {
            store.Add(certificate);
        }

        return secureServer;
    }

    private static Uri SecureServerUri(Uri configuredServer)
    {
        var builder = new UriBuilder(configuredServer)
        {
            Scheme = Uri.UriSchemeHttps,
            Port = configuredServer.IsDefaultPort || configuredServer.Port == 7080 ? 7443 : configuredServer.Port
        };
        return builder.Uri;
    }
}
