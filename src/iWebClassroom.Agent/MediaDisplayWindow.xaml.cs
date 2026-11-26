using System.Windows;
using System.Windows.Media.Imaging;

namespace IWebClassroom.Agent;

public partial class MediaDisplayWindow : Window
{
    private bool _allowClose;

    public MediaDisplayWindow(string path, string contentType)
    {
        InitializeComponent();
        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            MediaImage.Source = image;
            MediaImage.Visibility = Visibility.Visible;
        }
        else
        {
            MediaVideo.Source = new Uri(path, UriKind.Absolute);
            MediaVideo.Visibility = Visibility.Visible;
            Loaded += (_, _) => MediaVideo.Play();
        }
    }

    private void MediaVideo_Ended(object sender, RoutedEventArgs e)
    {
        MediaVideo.Position = TimeSpan.Zero;
        MediaVideo.Play();
    }

    public void CloseFromAgent()
    {
        _allowClose = true;
        MediaVideo.Stop();
        MediaVideo.Source = null;
        MediaImage.Source = null;
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
