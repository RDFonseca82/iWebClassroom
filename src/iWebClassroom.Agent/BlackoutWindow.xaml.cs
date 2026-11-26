using System.Windows;

namespace IWebClassroom.Agent;

public partial class BlackoutWindow : Window
{
    private bool _allowClose;

    public BlackoutWindow(System.Drawing.Rectangle bounds)
    {
        InitializeComponent();
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
    }

    public void CloseFromAgent()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
        }
        base.OnClosing(e);
    }
}
