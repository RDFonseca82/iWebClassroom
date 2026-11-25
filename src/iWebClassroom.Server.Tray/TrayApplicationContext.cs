using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;

namespace IWebClassroom.Server.Tray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string SecureBaseUrl = "https://localhost:7443";
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _versionItem;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly HttpClient _client;
    private readonly Icon _icon;
    private readonly IntPtr _iconHandle;

    public TrayApplicationContext()
    {
        (_icon, _iconHandle) = CreateLogoIcon();
        var menu = new ContextMenuStrip();
        menu.Items.Add(Item(Text("Abrir administração local", "Open local administration", "Abrir administración local"), (_, _) => Open("/admin.html"), true));
        menu.Items.Add(Item(Text("Abrir consola do professor", "Open teacher console", "Abrir consola del profesor"), (_, _) => Open("/")));
        menu.Items.Add(new ToolStripSeparator());
        _statusItem = Item(Text("Estado: a verificar…", "Status: checking…", "Estado: comprobando…"), null);
        _statusItem.Enabled = false;
        _versionItem = Item($"{Text("Versão", "Version", "Versión")}: {Assembly.GetExecutingAssembly().GetName().Version}", null);
        _versionItem.Enabled = false;
        menu.Items.Add(_statusItem);
        menu.Items.Add(_versionItem);
        menu.Items.Add(Item(Text("Copiar endereço seguro", "Copy secure address", "Copiar dirección segura"), (_, _) => CopyAddress()));
        menu.Items.Add(Item(Text("Atualizar estado", "Refresh status", "Actualizar estado"), async (_, _) => await RefreshStatusAsync()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item(Text("Sair do ícone", "Exit tray icon", "Cerrar icono"), (_, _) => ExitThread()));

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "iWebClassroom Server",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => Open("/admin.html");

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
                errors == System.Net.Security.SslPolicyErrors.None ||
                certificate?.Subject.Contains(Environment.MachineName, StringComparison.OrdinalIgnoreCase) == true
        };
        _client = new HttpClient(handler) { BaseAddress = new Uri(SecureBaseUrl), Timeout = TimeSpan.FromSeconds(5) };
        _timer = new System.Windows.Forms.Timer { Interval = 30_000 };
        _timer.Tick += async (_, _) => await RefreshStatusAsync();
        _timer.Start();
        _ = RefreshStatusAsync();
    }

    private async Task RefreshStatusAsync()
    {
        try
        {
            var health = await _client.GetFromJsonAsync<HealthResponse>("/health");
            _statusItem.Text = Text("Estado: online", "Status: online", "Estado: online");
            _versionItem.Text = $"{Text("Versão", "Version", "Versión")}: {health?.Version ?? "—"}";
            _notifyIcon.Text = $"iWebClassroom Server · {Text("online", "online", "online")}";
        }
        catch
        {
            _statusItem.Text = Text("Estado: indisponível", "Status: unavailable", "Estado: no disponible");
            _notifyIcon.Text = $"iWebClassroom Server · {Text("indisponível", "unavailable", "no disponible")}";
        }
    }

    private static ToolStripMenuItem Item(string text, EventHandler? handler, bool bold = false)
    {
        var item = new ToolStripMenuItem(text);
        if (handler is not null) item.Click += handler;
        if (bold) item.Font = new Font(item.Font, FontStyle.Bold);
        return item;
    }

    private static void Open(string path) => Process.Start(new ProcessStartInfo(SecureBaseUrl + path) { UseShellExecute = true });

    private static void CopyAddress()
    {
        try { Clipboard.SetText(SecureBaseUrl); } catch (ExternalException) { }
    }

    private static string Text(string portuguese, string english, string spanish) =>
        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
        {
            "en" => english,
            "es" => spanish,
            _ => portuguese
        };

    private static (Icon Icon, IntPtr Handle) CreateLogoIcon()
    {
        var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var outline = new Pen(Color.FromArgb(101, 220, 191), 2.2f))
        using (var check = new Pen(Color.White, 2.6f))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.FromArgb(7, 22, 45));
            var hexagon = new[]
            {
                new PointF(16, 3), new PointF(27, 8), new PointF(27, 23),
                new PointF(16, 29), new PointF(5, 23), new PointF(5, 8)
            };
            graphics.DrawPolygon(outline, hexagon);
            graphics.DrawLines(check, [new PointF(10, 16), new PointF(14.5f, 21), new PointF(23, 11.5f)]);
        }

        var handle = bitmap.GetHicon();
        bitmap.Dispose();
        return (Icon.FromHandle(handle), handle);
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _timer.Dispose();
        _client.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon.Dispose();
        DestroyIcon(_iconHandle);
        base.ExitThreadCore();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    private sealed record HealthResponse(string Status, string Version, DateTimeOffset Utc);
}
