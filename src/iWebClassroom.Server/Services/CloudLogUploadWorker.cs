namespace IWebClassroom.Server.Services;

public sealed class CloudLogUploadWorker(
    CloudSyncStateStore stateStore,
    CloudSyncClient client,
    LocalServerLogStore logs,
    ILogger<CloudLogUploadWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await UploadAvailableBatchesAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal service shutdown.
        }
    }

    private async Task UploadAvailableBatchesAsync(CancellationToken cancellationToken)
    {
        var state = stateStore.Snapshot();
        var token = stateStore.GetAccessToken();
        if (!state.ServerId.HasValue || string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        for (var batchNumber = 0; batchNumber < 4; batchNumber++)
        {
            var batch = await logs.ReadBatchAsync(250, cancellationToken);
            if (batch.Entries.Count == 0)
            {
                return;
            }

            try
            {
                await client.UploadLogsAsync(state.ServerId.Value, token, new CloudServerLogUploadRequest(batch.Entries), cancellationToken);
                await logs.AcknowledgeAsync(batch.Cursor, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
            {
                logger.LogWarning(exception, "The local server log batch could not be synchronized with the cloud.");
                return;
            }
        }
    }
}
