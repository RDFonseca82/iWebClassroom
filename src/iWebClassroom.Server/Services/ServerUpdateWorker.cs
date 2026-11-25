using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using IWebClassroom.Agent.Services;
using IWebClassroom.Contracts;
using IWebClassroom.Server.Configuration;
using Microsoft.Extensions.Options;

namespace IWebClassroom.Server.Services;

public sealed class ServerUpdateWorker(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<ServerUpdateOptions> options,
    IOptionsMonitor<CloudSyncOptions> cloudOptions,
    CloudSyncStateStore cloudState,
    CloudApprovedUpdateStore approvedUpdates,
    AgentRegistry agents,
    LessonRegistry lessons,
    ServerMaintenanceState maintenance,
    IWebHostEnvironment environment,
    ILogger<ServerUpdateWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var policy = options.CurrentValue;
                var approval = approvedUpdates.Snapshot().Approval;
                if (approval is not null &&
                    IsNewerThanCurrent(approval.Version) &&
                    !agents.List().Any(agent => agent.IsOnline) &&
                    lessons.List().Count == 0 &&
                    !string.IsNullOrWhiteSpace(policy.SignerThumbprint) &&
                    Uri.TryCreate(approval.ManifestUrl, UriKind.Absolute, out var manifestUri) &&
                    manifestUri.Scheme == Uri.UriSchemeHttps &&
                    await TryStartUpdateAsync(manifestUri, approval, policy, stoppingToken))
                {
                    Environment.Exit(0);
                    return;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "The approved server update check failed.");
            }

            var pending = approvedUpdates.Snapshot().Approval is { } pendingApproval && IsNewerThanCurrent(pendingApproval.Version);
            await Task.Delay(pending ? TimeSpan.FromSeconds(30) : TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    private async Task<bool> TryStartUpdateAsync(
        Uri manifestUri,
        CloudApprovedServerUpdate approval,
        ServerUpdateOptions policy,
        CancellationToken cancellationToken)
    {
        var synchronization = cloudState.Snapshot();
        var accessToken = cloudState.GetAccessToken();
        if (!synchronization.ServerId.HasValue || string.IsNullOrWhiteSpace(accessToken) ||
            !IsCloudApiUri(manifestUri, cloudOptions.CurrentValue.BaseUrl))
        {
            return false;
        }

        var client = httpClientFactory.CreateClient(nameof(ServerUpdateWorker));
        using var manifestRequest = CreateAuthorizedRequest(
            manifestUri,
            synchronization.ServerId.Value,
            accessToken);
        using var manifestResponse = await client.SendAsync(manifestRequest, cancellationToken);
        manifestResponse.EnsureSuccessStatusCode();
        var manifest = await manifestResponse.Content.ReadFromJsonAsync<UpdateManifest>(cancellationToken);
        if (manifest is null ||
            !string.Equals(manifest.Component, "server", StringComparison.OrdinalIgnoreCase) ||
            !Version.TryParse(manifest.Version, out var candidateVersion) ||
            Normalize(candidateVersion) != Normalize(ParseVersion(approval.Version)) ||
            Normalize(candidateVersion) <= Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0)))
        {
            return false;
        }

        if (!IsCloudApiUri(manifest.DownloadUrl, cloudOptions.CurrentValue.BaseUrl) ||
            !string.Equals(Normalize(policy.SignerThumbprint), Normalize(manifest.SignerThumbprint), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The server update manifest violates the local signing policy.");
        }

        var updateDirectory = Path.Combine(environment.ContentRootPath, "updates");
        Directory.CreateDirectory(updateDirectory);
        var packagePath = Path.Combine(updateDirectory, $"server-{candidateVersion}.msi");
        using var packageRequest = CreateAuthorizedRequest(
            manifest.DownloadUrl,
            synchronization.ServerId.Value,
            accessToken);
        using var packageResponse = await client.SendAsync(
            packageRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        packageResponse.EnsureSuccessStatusCode();
        await using (var source = await packageResponse.Content.ReadAsStreamAsync(cancellationToken))
        await using (var destination = File.Create(packagePath))
        {
            await source.CopyToAsync(destination, cancellationToken);
        }

        await using (var packageStream = File.OpenRead(packagePath))
        {
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(packageStream, cancellationToken));
            if (!string.Equals(actualHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(packagePath);
                throw new InvalidOperationException("The server update SHA-256 hash is invalid.");
            }
        }

        if (!AuthenticodeVerifier.IsTrustedAndSignedBy(packagePath, policy.SignerThumbprint))
        {
            File.Delete(packagePath);
            throw new InvalidOperationException("The server update Authenticode signature is invalid.");
        }

        var updaterPath = Path.Combine(AppContext.BaseDirectory, "iWebClassroom.Updater.exe");
        if (!File.Exists(updaterPath))
        {
            throw new FileNotFoundException("The update helper is not installed.", updaterPath);
        }

        maintenance.BeginUpdateDrain();
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        if (agents.List().Any(agent => agent.IsOnline) || lessons.List().Count > 0)
        {
            maintenance.CancelUpdateDrain();
            logger.LogInformation("The approved update is ready but was deferred because an agent or lesson became active.");
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = updaterPath,
                UseShellExecute = false,
                ArgumentList =
                {
                    "--package", packagePath,
                    "--sha256", manifest.Sha256,
                    "--signer", policy.SignerThumbprint,
                    "--wait-pid", Environment.ProcessId.ToString(),
                    "--service-name", policy.ServiceName
                }
            });
        }
        catch
        {
            maintenance.CancelUpdateDrain();
            throw;
        }
        logger.LogInformation(
            "The school-approved signed server update to version {Version} is ready and will now be installed while the server is idle.",
            candidateVersion);
        return true;
    }

    private static string Normalize(string value) => value.Replace(" ", string.Empty, StringComparison.Ordinal);

    private static Version Normalize(Version version) => new(
        version.Major,
        version.Minor,
        Math.Max(0, version.Build),
        Math.Max(0, version.Revision));

    private static Version ParseVersion(string value) =>
        Version.TryParse(value, out var version) ? version : new Version(0, 0);

    private static bool IsNewerThanCurrent(string value) =>
        Normalize(ParseVersion(value)) > Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0));

    private static HttpRequestMessage CreateAuthorizedRequest(Uri uri, Guid serverId, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("X-iWebClassroom-Server", serverId.ToString());
        return request;
    }

    private static bool IsCloudApiUri(Uri uri, string configuredBaseUrl)
    {
        if (!Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var cloudBase) ||
            cloudBase.Scheme != Uri.UriSchemeHttps ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        return string.Equals(uri.Host, cloudBase.Host, StringComparison.OrdinalIgnoreCase) &&
               uri.Port == cloudBase.Port;
    }
}
