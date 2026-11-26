using System.Windows;
using System.Windows.Threading;
using IWebClassroom.Agent.Services;

namespace IWebClassroom.Agent;

public partial class App : System.Windows.Application
{
    private AgentTrayIcon? _trayIcon;
    private readonly AgentLogStore _logs = AgentLogStore.Current;

    internal bool IsShuttingDown { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        _logs.Information("Lifecycle", $"Agent started on {Environment.MachineName}.", "agent_started");
        var window = new MainWindow();
        MainWindow = window;
        _trayIcon = new AgentTrayIcon(window);
        window.Show();
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
