using IWebClassroom.Server.Security;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace IWebClassroom.Server.Services;

public sealed class HttpsCertificateRenewalWorker(
    ManagedHttpsCertificateProvider certificates,
    ILogger<HttpsCertificateRenewalWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                if (certificates.RefreshIfRequired())
                {
                    logger.LogInformation("The managed HTTPS certificate was renewed after its validity or network addresses changed.");
                    if (WindowsServiceHelpers.IsWindowsService())
                    {
                        // The MSI configures Windows Service recovery. A non-zero exit makes SCM restart the
                        // service and Kestrel immediately starts using the renewed certificate.
                        Environment.Exit(75);
                    }
                }
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "The managed HTTPS certificate could not be renewed.");
            }
        }
    }
}
