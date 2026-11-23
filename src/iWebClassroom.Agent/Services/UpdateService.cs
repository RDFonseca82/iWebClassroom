using System.Diagnostics;
using System.Net.Http.Json;
using System.Reflection;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using IWebClassroom.Agent.Configuration;
using IWebClassroom.Agent.Localization;
using IWebClassroom.Contracts;

namespace IWebClassroom.Agent.Services;

public sealed class UpdateService
{
    public async Task<bool> CheckAndStartAsync(AgentSettings settings, CancellationToken cancellationToken)
    {
        var policy = LoadPolicy();
        if (!policy.Enabled || string.IsNullOrWhiteSpace(policy.SignerThumbprint))
        {
            return false;
        }

        using var manifestClient = new HttpClient { BaseAddress = new Uri(settings.ServerUrl), Timeout = TimeSpan.FromMinutes(1) };
        manifestClient.DefaultRequestHeaders.Add(HubProtocol.AgentIdHeader, settings.AgentId.ToString());
        manifestClient.DefaultRequestHeaders.Add(HubProtocol.AgentTokenHeader, settings.AgentToken);
        manifestClient.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(CultureInfo.CurrentUICulture.Name));

        using var response = await manifestClient.GetAsync($"/api/v1/updates/agent/{Uri.EscapeDataString(policy.Channel)}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        var manifest = await response.Content.ReadFromJsonAsync<UpdateManifest>(cancellationToken);
        if (manifest is null || !IsNewer(manifest.Version))
        {
            return false;
        }

        if (!string.Equals(manifest.DownloadUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(AgentStrings.UpdateHttpsRequired);
        }

        var updateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "iWebClassroom", "updates");
        Directory.CreateDirectory(updateDirectory);
        var packagePath = Path.Combine(updateDirectory, $"agent-{manifest.Version}.msi");
        using var downloadClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        await using (var source = await downloadClient.GetStreamAsync(manifest.DownloadUrl, cancellationToken))
        await using (var destination = File.Create(packagePath))
        {
            await source.CopyToAsync(destination, cancellationToken);
        }

        string hash;
        await using (var packageStream = File.OpenRead(packagePath))
        {
            hash = Convert.ToHexString(await SHA256.HashDataAsync(packageStream, cancellationToken));
        }
        if (!string.Equals(hash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(packagePath);
            throw new InvalidOperationException(AgentStrings.UpdateHashInvalid);
        }

        if (!string.Equals(Normalize(policy.SignerThumbprint), Normalize(manifest.SignerThumbprint), StringComparison.OrdinalIgnoreCase) ||
            !AuthenticodeVerifier.IsTrustedAndSignedBy(packagePath, policy.SignerThumbprint))
        {
            File.Delete(packagePath);
            throw new InvalidOperationException(AgentStrings.UpdateSignatureInvalid);
        }

        var updaterPath = Path.Combine(AppContext.BaseDirectory, "iWebClassroom.Updater.exe");
        if (!File.Exists(updaterPath))
        {
            throw new InvalidOperationException(AgentStrings.UpdaterMissing);
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
                "--wait-pid", Environment.ProcessId.ToString()
            }
        });
        return true;
    }

    private static bool IsNewer(string candidate)
    {
        var currentValue = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
        return Version.TryParse(candidate, out var candidateValue) && candidateValue > currentValue;
    }

    private static UpdatePolicy LoadPolicy()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "update.json");
        if (!File.Exists(path))
        {
            return new UpdatePolicy(false, "stable", string.Empty);
        }

        return JsonSerializer.Deserialize<UpdatePolicy>(File.ReadAllText(path), JsonSerializerOptions.Web)
               ?? new UpdatePolicy(false, "stable", string.Empty);
    }

    private static string Normalize(string value) => value.Replace(" ", string.Empty, StringComparison.Ordinal);

    private sealed record UpdatePolicy(bool Enabled, string Channel, string SignerThumbprint);
}
