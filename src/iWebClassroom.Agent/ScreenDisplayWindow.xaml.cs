using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace IWebClassroom.Agent;

public partial class ScreenDisplayWindow : Window
{
    private bool _allowClose;

    public ScreenDisplayWindow()
    {
        InitializeComponent();
    }

    public void SetFrame(byte[] jpeg)
    {
        using var stream = new MemoryStream(jpeg);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        ScreenImage.Source = image;
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
