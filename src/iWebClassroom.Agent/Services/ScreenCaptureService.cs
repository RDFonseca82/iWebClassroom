using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IWebClassroom.Agent.Services;

public sealed class ScreenCaptureService : IAsyncDisposable
{
    private CancellationTokenSource? _captureCancellation;
    private Task? _captureTask;

    public bool IsRunning => _captureTask is { IsCompleted: false };

    public void Start(Func<byte[], CancellationToken, Task> publishFrame)
    {
        if (IsRunning)
        {
            return;
        }

        _captureCancellation = new CancellationTokenSource();
        _captureTask = CaptureLoopAsync(publishFrame, _captureCancellation.Token);
    }

    public async Task StopAsync()
    {
        if (_captureCancellation is null || _captureTask is null)
        {
            return;
        }

        await _captureCancellation.CancelAsync();
        try
        {
            await _captureTask;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _captureCancellation.Dispose();
            _captureCancellation = null;
            _captureTask = null;
        }
    }

    private static async Task CaptureLoopAsync(Func<byte[], CancellationToken, Task> publishFrame, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var frame = CaptureJpeg();
            await publishFrame(frame, cancellationToken);
        }
    }

    private static byte[] CaptureJpeg()
    {
        var left = GetSystemMetrics(76);
        var top = GetSystemMetrics(77);
        var width = GetSystemMetrics(78);
        var height = GetSystemMetrics(79);
        var screenDc = GetDC(IntPtr.Zero);
        var memoryDc = CreateCompatibleDC(screenDc);
        var bitmap = CreateCompatibleBitmap(screenDc, width, height);
        var previous = SelectObject(memoryDc, bitmap);

        try
        {
            if (!BitBlt(memoryDc, 0, 0, width, height, screenDc, left, top, 0x00CC0020 | 0x40000000))
            {
                throw new InvalidOperationException("Não foi possível capturar o ecrã.");
            }

            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            BitmapSource output = source;
            if (source.PixelWidth > 1280)
            {
                var scale = 1280d / source.PixelWidth;
                var transformed = new TransformedBitmap(source, new ScaleTransform(scale, scale));
                transformed.Freeze();
                output = transformed;
            }

            var encoder = new JpegBitmapEncoder { QualityLevel = 65 };
            encoder.Frames.Add(BitmapFrame.Create(output));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        finally
        {
            SelectObject(memoryDc, previous);
            DeleteObject(bitmap);
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr deviceContext, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr graphicsObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr graphicsObject);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, int operation);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
