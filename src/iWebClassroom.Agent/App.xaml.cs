using System.Windows;
using System.Windows.Threading;
using IWebClassroom.Agent.Services;

namespace IWebClassroom.Agent;

public partial class App : System.Windows.Application
{
    private const string InstanceMutexName = "Local\\iWebClassroom.Agent.Instance";
    private const string ShowWindowEventName = "Local\\iWebClassroom.Agent.ShowWindow";
    private AgentTrayIcon? _trayIcon;
    private readonly AgentLogStore _logs = AgentLogStore.Current;
    private Mutex? _instanceMutex;
    private EventWaitHandle? _showWindowEvent;
    private RegisteredWaitHandle? _showWindowRegistration;

    internal bool IsShuttingDown { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);
        _instanceMutex = new Mutex(true, InstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            _showWindowEvent.Set();
            _showWindowEvent.Dispose();
            _showWindowEvent = null;
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown(0);
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        _logs.Information("Lifecycle", $"Agent started on {Environment.MachineName}.", "agent_started");
        var window = new MainWindow();
        MainWindow = window;
        _trayIcon = new AgentTrayIcon(window);
        _showWindowRegistration = ThreadPool.RegisterWaitForSingleObject(
            _showWindowEvent,
            (_, _) => Dispatcher.BeginInvoke(() =>
            {
                if (!IsShuttingDown)
                {
                    window.ShowFromTray();
                }
            }),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
        _ = StartAgentAsync(window);
    }

    private async Task StartAgentAsync(MainWindow window)
    {
        try
        {
            await window.StartAsync();
        }
        catch (Exception exception)
        {
            _logs.Error("Lifecycle", "The background agent initialization failed.", exception, "agent_initialization_failed");
        }
    }

    internal void NotifyStillRunning() => _trayIcon?.NotifyStillRunning();

    internal void ShutdownAgent()
    {
        IsShuttingDown = true;
        Shutdown();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        IsShuttingDown = true;
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        IsShuttingDown = true;
        _logs.Information("Lifecycle", "Agent stopped.", "agent_stopped");
        _trayIcon?.Dispose();
        _showWindowRegistration?.Unregister(null);
        _showWindowRegistration = null;
        _showWindowEvent?.Dispose();
        _showWindowEvent = null;
        if (_instanceMutex is not null)
        {
            try
            {
                _instanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The mutex was not acquired by this process.
            }
            _instanceMutex.Dispose();
            _instanceMutex = null;
        }
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) =>
        _logs.Critical("Unhandled", "An unhandled user interface error occurred.", e.Exception, "ui_unhandled_exception");

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e) =>
        _logs.Critical(
            "Unhandled",
            "An unhandled agent error occurred.",
            e.ExceptionObject as Exception,
            "agent_unhandled_exception");
}
