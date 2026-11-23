using System.Diagnostics;
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
    IOptions<ServerUpdateOptions> options,
    IWebHostEnvironment environment,
    ILogger<ServerUpdateWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var policy = options.Value;
        if (!policy.Enabled || string.IsNullOrWhiteSpace(policy.SignerThumbprint) ||
            !Uri.TryCreate(policy.ManifestUrl, UriKind.Absolute, out var manifestUri) ||
            manifestUri.Scheme != Uri.UriSchemeHttps)
        {
            logger.LogInformation("Automatic server updates are disabled or do not have a pinned signing certificate.");
            return;
        }

        var interval = TimeSpan.FromHours(Math.Clamp(policy.CheckIntervalHours, 1, 168));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await TryStartUpdateAsync(manifestUri, policy, stoppingToken))
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
                logger.LogError(exception, "The automatic server update check failed.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task<bool> TryStartUpdateAsync(Uri manifestUri, ServerUpdateOptions policy, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(nameof(ServerUpdateWorker));
        var manifest = await client.GetFromJsonAsync<UpdateManifest>(manifestUri, cancellationToken);
        if (manifest is null ||
            !string.Equals(manifest.Component, "server", StringComparison.OrdinalIgnoreCase) ||
            !Version.TryParse(manifest.Version, out var candidateVersion) ||
            candidateVersion <= (Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0)))
        {
            return false;
        }

        if (manifest.DownloadUrl.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(Normalize(policy.SignerThumbprint), Normalize(manifest.SignerThumbprint), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The server update manifest violates the local signing policy.");
        }

        var updateDirectory = Path.Combine(environment.ContentRootPath, "updates");
        Directory.CreateDirectory(updateDirectory);
        var packagePath = Path.Combine(updateDirectory, $"server-{candidateVersion}.msi");
        await using (var source = await client.GetStreamAsync(manifest.DownloadUrl, cancellationToken))
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
                "--scheduled-task", policy.ScheduledTaskName
            }
        });
        logger.LogInformation("A signed server update to version {Version} is ready and will now be installed.", candidateVersion);
        return true;
    }

    private static string Normalize(string value) => value.Replace(" ", string.Empty, StringComparison.Ordinal);
}
