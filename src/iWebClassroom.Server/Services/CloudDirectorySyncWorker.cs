using IWebClassroom.Server.Configuration;
using Microsoft.Extensions.Options;

namespace IWebClassroom.Server.Services;

public sealed class CloudDirectorySyncWorker(
    CloudSyncService synchronization,
    IOptionsMonitor<CloudSyncOptions> options,
    ILogger<CloudDirectorySyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (options.CurrentValue.Enabled && synchronization.Status().Connected)
                {
                    await synchronization.SynchronizeDirectoryAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "The lightweight school directory synchronization attempt failed.");
            }

            var seconds = Math.Clamp(options.CurrentValue.DirectoryIntervalSeconds, 30, 300);
            await Task.Delay(TimeSpan.FromSeconds(seconds), stoppingToken);
        }
    }
}
