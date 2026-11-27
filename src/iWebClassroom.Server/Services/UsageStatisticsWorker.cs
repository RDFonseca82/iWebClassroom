namespace IWebClassroom.Server.Services;

public sealed class UsageStatisticsWorker(
    UsageStatisticsStore statistics,
    AgentRegistry agents,
    CloudSyncStateStore cloudState,
    CloudSyncClient cloud,
    ILogger<UsageStatisticsWorker> logger) : BackgroundService
{
    private DateOnly? _uploadedOn;
    private DateTimeOffset _nextUploadAttemptUtc = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await statistics.SampleOnlineAgentsAsync(agents.List(), stoppingToken);
                await UploadDuringQuietHoursAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Usage statistics processing failed and will be retried.");
            }
        }
    }

    private async Task UploadDuringQuietHoursAsync(CancellationToken cancellationToken)
    {
        var localNow = DateTimeOffset.Now;
        var today = DateOnly.FromDateTime(localNow.Date);
        if (localNow.Hour is < 2 or >= 6 || _uploadedOn == today || DateTimeOffset.UtcNow < _nextUploadAttemptUtc)
        {
            return;
        }

        var state = cloudState.Snapshot();
        var token = cloudState.GetAccessToken();
        if (!state.ServerId.HasValue || string.IsNullOrWhiteSpace(token))
        {
            _nextUploadAttemptUtc = DateTimeOffset.UtcNow.AddMinutes(15);
            return;
        }

        var entries = await statistics.CompletedDaysAsync(cancellationToken);
        if (entries.Count > 0)
        {
            await cloud.UploadStatisticsAsync(
                state.ServerId.Value,
                token,
                new CloudUsageStatisticsUploadRequest(entries),
                cancellationToken);
            await cloudState.MarkCloudContactAsync(cancellationToken);
        }
        _uploadedOn = today;
        logger.LogInformation("Uploaded {Count} completed usage-statistics days during the quiet-hours window.", entries.Count);
    }
}
