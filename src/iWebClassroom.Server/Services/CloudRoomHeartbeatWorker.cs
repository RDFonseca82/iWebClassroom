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
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
            do
            {
                await rooms.HeartbeatAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
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
