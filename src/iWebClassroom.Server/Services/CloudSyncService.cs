using System.Reflection;
using IWebClassroom.Server.Configuration;
using Microsoft.Extensions.Options;

namespace IWebClassroom.Server.Services;

public sealed class CloudSyncService(
    CloudSyncClient client,
    CloudSyncStateStore stateStore,
    CloudLicenseStore licenseStore,
    CloudApprovedUpdateStore approvedUpdateStore,
    CloudDirectoryStore directoryStore,
    CloudConfigurationStore configurationStore,
    AgentRegistry agents,
    IOptionsMonitor<CloudSyncOptions> options,
    ILogger<CloudSyncService> logger)
{
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly SemaphoreSlim _directorySyncLock = new(1, 1);
    private string? _directoryEtag;

    public CloudSyncStatus Status()
    {
        var state = stateStore.Snapshot();
        return new CloudSyncStatus(
            options.CurrentValue.Enabled,
            state.ServerId.HasValue && !string.IsNullOrWhiteSpace(stateStore.GetAccessToken()),
            state.InstanceUid,
            state.ServerId,
            state.SchoolId,
            state.SchoolName,
            state.ConfigurationRevision,
            state.Status,
            state.LastSyncUtc,
            state.LastError,
            state.Conflict,
            options.CurrentValue.BaseUrl);
    }

    public async Task<CloudSyncStatus> ConnectAsync(CloudConnectRequest request, CancellationToken cancellationToken)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (!options.CurrentValue.Enabled)
            {
                throw new InvalidOperationException("Cloud synchronization is disabled on this server.");
            }

            var state = stateStore.Snapshot();
            var version = CurrentVersion();
            var response = await client.AuthenticateAsync(new CloudAuthRequest(
                request.Username.Trim(),
                request.Password,
                state.InstanceUid,
                Environment.MachineName,
                version), cancellationToken);

            var configurationHash = string.Empty;
            if (response.Configuration.Revision > 0)
            {
                await configurationStore.ApplyAsync(response.Configuration.Settings, cancellationToken);
                configurationHash = response.Configuration.Settings.Hash();
            }

            await stateStore.SaveAuthenticationAsync(response, configurationHash, cancellationToken);
            logger.LogInformation(
                "Cloud synchronization connected to school {SchoolName} with server id {ServerId}.",
                response.SchoolName,
                response.ServerId);
            await SynchronizeAsync(cancellationToken);
            return Status();
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task<CloudSyncStatus> SynchronizeAsync(CancellationToken cancellationToken)
    {
        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            var state = stateStore.Snapshot();
            var token = stateStore.GetAccessToken();
            if (!options.CurrentValue.Enabled || !state.ServerId.HasValue || string.IsNullOrWhiteSpace(token))
            {
                return Status();
            }

            var current = configurationStore.Current;
            var currentHash = current.Hash();
            var agentList = agents.List();
            var request = new CloudSyncRequest(
                state.InstanceUid,
                CurrentVersion(),
                CurrentVersion(),
                agentList.Count(item => !item.IsRoaming),
                agentList
                    .Where(item => !item.IsRoaming && item.IsAssigned && !item.IsShared)
                    .Select(item => item.ClassroomId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(),
                new CloudConfigurationRequest(
                    state.ConfigurationRevision,
                    !string.Equals(currentHash, state.ConfigurationHash, StringComparison.OrdinalIgnoreCase),
                    current));

            var response = await client.SynchronizeAsync(state.ServerId.Value, token, request, cancellationToken);
            await licenseStore.ApplyAsync(response.License, cancellationToken);
            await approvedUpdateStore.ApplyAsync(response.ServerUpdate, cancellationToken);
            await directoryStore.ApplyAsync(response.Directory, cancellationToken);
            await agents.ApplyAssignmentsAsync(response.Directory?.Devices, cancellationToken);
            var remoteHash = response.Configuration.Settings.Hash();
            if (!string.Equals(remoteHash, currentHash, StringComparison.OrdinalIgnoreCase))
            {
                await configurationStore.ApplyAsync(response.Configuration.Settings, cancellationToken);
            }

            await stateStore.UpdateSynchronizationAsync(
                response.Configuration.Revision,
                remoteHash,
                response.Configuration.Conflict,
                cancellationToken);
            logger.LogInformation(
                "Cloud synchronization completed at revision {Revision}{Conflict}.",
                response.Configuration.Revision,
                response.Configuration.Conflict ? " with a resolved conflict" : string.Empty);
            return Status();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await stateStore.SetErrorAsync(exception.Message, CancellationToken.None);
            logger.LogError(exception, "Cloud synchronization failed.");
            throw;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task<CloudSyncStatus> UpdateLocalConfigurationAsync(CloudServerSettings settings, CancellationToken cancellationToken)
    {
        await configurationStore.ApplyAsync(settings, cancellationToken);
        if (Status().Connected)
        {
            await SynchronizeAsync(cancellationToken);
        }
        return Status();
    }

    public async Task SynchronizeDirectoryAsync(CancellationToken cancellationToken)
    {
        await _directorySyncLock.WaitAsync(cancellationToken);
        try
        {
            var state = stateStore.Snapshot();
            var token = stateStore.GetAccessToken();
            if (!options.CurrentValue.Enabled || !state.ServerId.HasValue || string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            var result = await client.SynchronizeDirectoryAsync(
                state.ServerId.Value,
                token,
                new CloudDirectorySyncRequest(agents.List()
                    .Where(item => !item.IsRoaming)
                    .Select(item => new CloudDeviceReport(
                        item.AgentId,
                        item.MachineId,
                        item.DisplayName,
                        item.IsOnline,
                        item.LastSeenUtc))
                    .ToArray()),
                _directoryEtag,
                cancellationToken);
            _directoryEtag = result.ETag;
            if (!result.NotModified)
            {
                await directoryStore.ApplyAsync(result.Directory, cancellationToken);
                await agents.ApplyAssignmentsAsync(result.Directory?.Devices, cancellationToken);
                logger.LogInformation("The lightweight school directory synchronization completed.");
            }
        }
        finally
        {
            _directorySyncLock.Release();
        }
    }

    public async Task<CloudSyncStatus> DisconnectAsync(CancellationToken cancellationToken)
    {
        await stateStore.DisconnectAsync(cancellationToken);
        logger.LogInformation("Cloud synchronization credentials were removed from this server.");
        return Status();
    }

    public async Task<CloudAdministratorResponse> AuthenticateAdministratorAsync(
        CloudAdministratorLoginRequest request,
        CancellationToken cancellationToken)
    {
        var state = stateStore.Snapshot();
        var token = stateStore.GetAccessToken();
        if (!state.ServerId.HasValue || string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("This local server is not connected to central management.");
        }

        return await client.AuthenticateAdministratorAsync(state.ServerId.Value, token, request, cancellationToken);
    }

    public async Task<CloudTeacherAccessChallenge> RequestTeacherAccessAsync(
        CloudTeacherAccessRequest request,
        CancellationToken cancellationToken)
    {
        var (serverId, token) = ConnectedIdentity();
        return await client.RequestTeacherAccessAsync(serverId, token, request, cancellationToken);
    }

    public async Task<CloudTeacherAccessIdentity> VerifyTeacherAccessAsync(
        CloudTeacherAccessVerifyRequest request,
        CancellationToken cancellationToken)
    {
        var (serverId, token) = ConnectedIdentity();
        return await client.VerifyTeacherAccessAsync(serverId, token, request, cancellationToken);
    }

    public CloudServerSettings Configuration() => configurationStore.Current;

    public CloudLicenseState License() => licenseStore.Snapshot();

    private (Guid ServerId, string AccessToken) ConnectedIdentity()
    {
        var state = stateStore.Snapshot();
        var token = stateStore.GetAccessToken();
        if (!state.ServerId.HasValue || string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("This local server is not connected to central management.");
        }

        return (state.ServerId.Value, token);
    }

    private static string CurrentVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
}
