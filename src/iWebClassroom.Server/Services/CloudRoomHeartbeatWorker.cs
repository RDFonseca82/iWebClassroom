namespace IWebClassroom.Server.Services;

public sealed class CloudRoomHeartbeatWorker(
    CloudRoomService rooms,
    ILogger<CloudRoomHeartbeatWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await rooms.HeartbeatAsync(stoppingToken);
                var interval = rooms.HasActiveLessons
                    ? TimeSpan.FromSeconds(20)
                    : TimeSpan.FromMinutes(1);
                await Task.Delay(interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal service shutdown.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The cloud room heartbeat worker stopped unexpectedly.");
        }
    }
}
