using IWebClassroom.Agent.Configuration;
using IWebClassroom.Agent.Localization;
using IWebClassroom.Contracts;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using System.Globalization;

namespace IWebClassroom.Agent.Services;

public sealed class ClassroomConnection : IAsyncDisposable
{
    private HubConnection? _connection;

    public event Func<ClassroomCommand, Task>? CommandReceived;

    public event Action<byte[]>? ScreenFrameReceived;

    public event Action? ScreenStopped;

    public event Action<string>? StateChanged;

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public async Task ConnectAsync(AgentSettings settings, CancellationToken cancellationToken)
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        var hubUri = new Uri(new Uri(settings.ServerUrl.TrimEnd('/') + "/"), HubProtocol.HubPath.TrimStart('/'));
        _connection = new HubConnectionBuilder()
            .WithUrl(hubUri, options =>
            {
                options.Headers.Add(HubProtocol.AgentIdHeader, settings.AgentId.ToString());
                options.Headers.Add(HubProtocol.AgentTokenHeader, settings.AgentToken);
                options.Headers.Add("Accept-Language", CultureInfo.CurrentUICulture.Name);
                options.Transports = HttpTransportType.WebSockets | HttpTransportType.LongPolling;
            })
            .WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)])
            .Build();

        _connection.On<ClassroomCommand>(HubProtocol.ReceiveCommand, async command =>
        {
            if (CommandReceived is not null)
            {
                await CommandReceived(command);
            }
        });
        _connection.On<byte[]>(HubProtocol.ScreenFrame, frame => ScreenFrameReceived?.Invoke(frame));
        _connection.On(HubProtocol.ScreenStopped, () => ScreenStopped?.Invoke());
        _connection.Reconnecting += _ => { StateChanged?.Invoke(AgentStrings.Reconnecting); return Task.CompletedTask; };
        _connection.Reconnected += _ => { StateChanged?.Invoke(AgentStrings.Connected); return Task.CompletedTask; };
        _connection.Closed += _ => { StateChanged?.Invoke(AgentStrings.Disconnected); return Task.CompletedTask; };

        await _connection.StartAsync(cancellationToken);
        StateChanged?.Invoke(AgentStrings.Connected);
    }

    public Task PublishFrameAsync(byte[] frame, CancellationToken cancellationToken) =>
        _connection?.InvokeAsync(HubProtocol.PublishScreenFrame, frame, cancellationToken)
        ?? Task.CompletedTask;

    public Task PublishStoppedAsync(CancellationToken cancellationToken) =>
        _connection?.InvokeAsync(HubProtocol.PublishScreenStopped, cancellationToken)
        ?? Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
