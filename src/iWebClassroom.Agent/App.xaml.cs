using System.Windows;

namespace IWebClassroom.Agent;

public partial class App : System.Windows.Application
{
    private AgentTrayIcon? _trayIcon;

    internal bool IsShuttingDown { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
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
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
