using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.ComponentModel;
using MessageBox = System.Windows.MessageBox;
using IWebClassroom.Agent.Configuration;
using IWebClassroom.Agent.Localization;
using IWebClassroom.Agent.Services;
using IWebClassroom.Contracts;
using Microsoft.Web.WebView2.Core;

namespace IWebClassroom.Agent;

public partial class MainWindow : Window
{
    private readonly AgentSettingsStore _settingsStore = new();
    private readonly EnrollmentClient _enrollmentClient = new();
    private readonly ServerTrustBootstrapper _trustBootstrapper = new();
    private readonly RoomJoinClient _roomJoinClient = new();
    private readonly ClassroomConnection _connection = new();
    private readonly ApplicationCatalog _applications = new();
    private readonly ScreenCaptureService _screenCapture = new();
    private readonly ScreenCaptureService _studentCapture = new();
    private readonly UpdateService _updates = new();
    private readonly AgentLogStore _logs = AgentLogStore.Current;
    private readonly AgentUsageStore _usage = new();
    private readonly DispatcherTimer _profileTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer _logUploadTimer = new() { Interval = TimeSpan.FromMinutes(5) };
    private readonly DispatcherTimer _usageTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly SemaphoreSlim _teacherConsoleInitialization = new(1, 1);
    private AgentSettings _settings = AgentSettings.Empty;
    private ScreenDisplayWindow? _screenWindow;
    private readonly List<BlackoutWindow> _blackoutWindows = [];
    private MediaDisplayWindow? _mediaWindow;
    private string? _mediaPath;
    private IReadOnlyCollection<Guid> _shareViewers = [];
    private int _screenFramePending;
    private bool _closing;
    private bool _disposed;
    private bool _busy;
    private bool _profileIsAssigned;
    private bool _teacherConsoleHandlersAttached;
    private CoreWebView2Environment? _teacherConsoleEnvironment;
    private string? _teacherConsoleServer;
    private DateTimeOffset _lastProfileWarningUtc = DateTimeOffset.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = $"v{Assembly.GetExecutingAssembly().GetName().Version}";
        _connection.CommandReceived += HandleCommandAsync;
        _connection.ScreenFrameReceived += HandleScreenFrame;
        _connection.ScreenStopped += HandleScreenStopped;
        _connection.StateChanged += state => Dispatcher.Invoke(() =>
        {
            StatusText.Text = state;
            UpdateConnectionControls();
            UpdateRolePanels();
        });
        _profileTimer.Tick += async (_, _) => await RefreshProfileAsync();
        _logUploadTimer.Tick += async (_, _) => await UploadBackgroundDataAsync();
        _usageTimer.Tick += async (_, _) => await RecordUsageMinuteAsync();
    }

    internal async Task StartAsync()
    {
        _settings = await _settingsStore.LoadAsync();
        try
        {
            await _usage.RecordStartAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logs.Warning("Usage", "The agent start statistic could not be persisted.", exception, "usage_start_failed");
        }
        _logs.Information("Configuration", "Agent settings loaded.", "settings_loaded");
        ApplySettingsToForm();
        if (_settings.AgentId != Guid.Empty && !string.IsNullOrWhiteSpace(_settings.AgentToken))
        {
            await ConnectAsync(reenroll: false);
        }
        else
        {
            await ConnectAsync(reenroll: true);
        }
        _profileTimer.Start();
        _logUploadTimer.Start();
        _usageTimer.Start();
    }

    private void ApplySettingsToForm()
    {
        ServerUrlText.Text = _settings.ServerUrl;
        DisplayNameText.Text = string.IsNullOrWhiteSpace(_settings.DisplayName)
            ? Environment.MachineName
            : _settings.DisplayName;
        ClassroomText.Text = _settings.ClassroomId;
        RoleCombo.SelectedIndex = _settings.Role == AgentRole.Teacher ? 1 : 0;
        SharedComputerCheck.IsChecked = _settings.IsShared;
        UpdateAssignmentMode();
        AssignmentText.Text = string.Equals(_settings.ClassroomId, "PENDENTE", StringComparison.OrdinalIgnoreCase)
            ? AgentStrings.PendingAssignment
            : _settings.IsShared
                ? AgentStrings.SharedComputer
                : $"{(_settings.Role == AgentRole.Teacher ? AgentStrings.Teacher : AgentStrings.Student)} · {_settings.ClassroomId}";
        IdentityText.Text = _settings.AgentId == Guid.Empty ? AgentStrings.NotEnrolled : _settings.AgentId.ToString();
        UpdateConnectionControls();
        UpdateRolePanels();
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e) => await ConnectAsync(reenroll: false);

    private bool SettingsChanged()
    {
        return !string.Equals(_settings.ServerUrl.TrimEnd('/'), ServerUrlText.Text.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }

    private async Task ConnectAsync(bool reenroll)
    {
        SetBusy(true, AgentStrings.Connecting);
        try
        {
            if (!Uri.TryCreate(ServerUrlText.Text.Trim(), UriKind.Absolute, out var serverUri) ||
                (serverUri.Scheme != Uri.UriSchemeHttps && serverUri.Scheme != Uri.UriSchemeHttp))
            {
                throw new InvalidOperationException(AgentStrings.InvalidServer);
            }

            if (_settings.AgentId == Guid.Empty || string.IsNullOrWhiteSpace(_settings.AgentToken))
            {
                _settings = await _enrollmentClient.EnrollAsync(
                    serverUri,
                    string.Empty,
                    Environment.MachineName,
                    AgentRole.Student,
                    "PENDENTE",
                    false,
                    CancellationToken.None);
                await _settingsStore.SaveAsync(_settings);
                ApplySettingsToForm();
            }
            else
            {
                var secureServer = await _trustBootstrapper.EnsureTrustedAsync(
                    serverUri,
                    _settings.AgentId,
                    _settings.AgentToken,
                    CancellationToken.None);
                if (!string.Equals(_settings.ServerUrl.TrimEnd('/'), secureServer.ToString().TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                {
                    _settings = _settings with { ServerUrl = secureServer.ToString().TrimEnd('/') };
                    await _settingsStore.SaveAsync(_settings);
                    ApplySettingsToForm();
                }
            }

            await _connection.ConnectAsync(_settings, CancellationToken.None);
            try
            {
                await _enrollmentClient.UpdateCapabilitiesAsync(_settings, CancellationToken.None);
            }
            catch (Exception capabilityError)
            {
                _logs.Warning("Capabilities", "Display information could not be sent to the server.", capabilityError, "capabilities_failed");
            }
            await RefreshProfileAsync();
            StatusText.Text = AgentStrings.Connected;
            ShareButton.IsEnabled = _settings.Role == AgentRole.Teacher;
            SharedRoomsPanel.Visibility = _settings.IsShared ? Visibility.Visible : Visibility.Collapsed;
            _logs.Information("Connection", $"Connected to {_settings.ServerUrl}.", "server_connected");
            _ = UploadBackgroundDataAsync();
            _ = CheckForUpdatesAsync();
        }
        catch (Exception exception)
        {
            _logs.Error("Connection", "The agent could not connect to the local server.", exception, "server_connection_failed");
            StatusText.Text = AgentStrings.ConnectionError;
            if (IsVisible)
            {
                MessageBox.Show(this, exception.Message, AgentStrings.AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            SetBusy(false, string.Empty);
        }
    }

    private async Task RefreshProfileAsync()
    {
        if (_settings.AgentId == Guid.Empty || string.IsNullOrWhiteSpace(_settings.AgentToken))
        {
            return;
        }

        try
        {
            var profile = await _enrollmentClient.GetProfileAsync(_settings, CancellationToken.None);
            _profileIsAssigned = profile.IsAssigned;
            var updated = _settings with
            {
                DisplayName = profile.DisplayName,
                Role = profile.Role,
                ClassroomId = profile.ClassroomId,
                IsShared = profile.IsShared
            };
            if (updated != _settings)
            {
                _settings = updated;
                await _settingsStore.SaveAsync(_settings);
                ApplySettingsToForm();
            }
            UpdateRolePanels();
        }
        catch (Exception exception)
        {
            // The current connection remains usable; retry on the next interval.
            if (DateTimeOffset.UtcNow - _lastProfileWarningUtc >= TimeSpan.FromMinutes(5))
            {
                _lastProfileWarningUtc = DateTimeOffset.UtcNow;
                _logs.Warning("Profile", "The agent profile could not be refreshed.", exception, "profile_refresh_failed");
            }
        }
    }

    private async Task HandleCommandAsync(ClassroomCommand command)
    {
        _logs.Information("Command", $"Received command {command.Kind}.", "command_received");
        try
        {
            switch (command.Kind)
            {
                case ClassroomCommandKind.ShowMessage:
                    var message = command.Payload.GetProperty("message").GetString() ?? string.Empty;
                    await Dispatcher.InvokeAsync(() => MessageBox.Show(message, AgentStrings.TeacherMessageTitle, MessageBoxButton.OK, MessageBoxImage.Information));
                    break;
                case ClassroomCommandKind.LaunchApplication:
                    _applications.Launch(
                        command.Payload.GetProperty("applicationId").GetString() ?? string.Empty,
                        command.Payload.TryGetProperty("fileName", out var fileName) ? fileName.GetString() : null,
                        command.Payload.TryGetProperty("arguments", out var arguments) ? arguments.GetString() : null);
                    break;
                case ClassroomCommandKind.StartScreenShare:
                    var monitorIndex = command.Payload.TryGetProperty("monitorIndex", out var monitor) ? monitor.GetInt32() : 0;
                    var viewers = command.Payload.TryGetProperty("viewerAgentIds", out var viewerIds)
                        ? viewerIds.EnumerateArray().Select(value => value.GetGuid()).ToArray()
                        : [];
                    await StartSharingAsync(monitorIndex, viewers);
                    break;
                case ClassroomCommandKind.StopScreenShare:
                    await StopSharingAsync();
                    break;
                case ClassroomCommandKind.CheckForUpdate:
                    await CheckForUpdatesAsync();
                    break;
                case ClassroomCommandKind.StartBlackout:
                    await Dispatcher.InvokeAsync(StartBlackout);
                    break;
                case ClassroomCommandKind.StopBlackout:
                    await Dispatcher.InvokeAsync(StopBlackout);
                    break;
                case ClassroomCommandKind.StartStudentView:
                    var studentMonitor = command.Payload.TryGetProperty("monitorIndex", out var studentDisplay)
                        ? studentDisplay.GetInt32() : 0;
                    var studentViewers = command.Payload.TryGetProperty("viewerAgentIds", out var studentViewerIds) &&
                                         studentViewerIds.ValueKind == JsonValueKind.Array
                        ? studentViewerIds.EnumerateArray().Select(value => value.GetGuid()).ToArray()
                        : [];
                    await StartStudentViewAsync(studentMonitor, studentViewers);
                    break;
                case ClassroomCommandKind.StopStudentView:
                    await _studentCapture.StopAsync();
                    break;
                case ClassroomCommandKind.ShowMedia:
                    await ShowMediaAsync(command.Payload);
                    break;
                case ClassroomCommandKind.StopMedia:
                    await Dispatcher.InvokeAsync(StopMedia);
                    break;
            }
            _logs.Information("Command", $"Completed command {command.Kind}.", "command_completed");
        }
        catch (Exception exception)
        {
            _logs.Error("Command", $"Command {command.Kind} failed.", exception, "command_failed");
            await Dispatcher.InvokeAsync(() => ActivityText.Text = $"{AgentStrings.FailurePrefix}: {exception.Message}");
        }
    }

    private void HandleScreenFrame(byte[] frame)
    {
        if (_settings.Role != AgentRole.Student || _closing)
        {
            return;
        }

        if (Interlocked.Exchange(ref _screenFramePending, 1) == 1)
        {
            return;
        }
        _ = Dispatcher.BeginInvoke(() =>
        {
            try
            {
                _screenWindow ??= new ScreenDisplayWindow();
                _screenWindow.SetFrame(frame);
                if (!_screenWindow.IsVisible)
                {
                    _screenWindow.Show();
                }
            }
            finally
            {
                Interlocked.Exchange(ref _screenFramePending, 0);
            }
        });
    }

    private void HandleScreenStopped() => Dispatcher.Invoke(() =>
    {
        _screenWindow?.CloseFromAgent();
        _screenWindow = null;
    });

    private async void ShareButton_Click(object sender, RoutedEventArgs e) => await StartSharingAsync(0, []);

    private async Task StartSharingAsync(int monitorIndex, IReadOnlyCollection<Guid> viewers)
    {
        if (!_connection.IsConnected)
        {
            return;
        }

        await _screenCapture.StopAsync();
        _shareViewers = viewers.Distinct().ToArray();
        _screenCapture.Start(monitorIndex, (frame, cancellationToken) =>
            _connection.PublishFrameAsync(frame, _shareViewers, cancellationToken));
        await Dispatcher.InvokeAsync(() =>
        {
            ShareButton.IsEnabled = false;
            StopShareButton.IsEnabled = true;
            ActivityText.Text = AgentStrings.ScreenSharing;
        });
    }

    private async void StopShareButton_Click(object sender, RoutedEventArgs e) => await StopSharingAsync();

    private async Task StopSharingAsync()
    {
        await _screenCapture.StopAsync();
        if (_connection.IsConnected)
        {
            try
            {
                await _connection.PublishStoppedAsync(_shareViewers, CancellationToken.None);
            }
            catch (Exception exception)
            {
                _logs.Warning("ScreenShare", "The local capture stopped, but the server no longer accepted the stop notification.", exception, "screen_stop_notification_failed");
            }
        }
        _shareViewers = [];

        await Dispatcher.InvokeAsync(() =>
        {
            ShareButton.IsEnabled = _settings.Role == AgentRole.Teacher && _connection.IsConnected;
            StopShareButton.IsEnabled = false;
            ActivityText.Text = string.Empty;
        });
    }

    private async Task StartStudentViewAsync(int monitorIndex, IReadOnlyCollection<Guid> viewers)
    {
        await _studentCapture.StopAsync();
        var recipients = viewers.Distinct().ToArray();
        _studentCapture.Start(monitorIndex, (frame, cancellationToken) =>
            _connection.PublishStudentFrameAsync(frame, recipients, cancellationToken));
    }

    private void StartBlackout()
    {
        StopBlackout();
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var window = new BlackoutWindow(screen.Bounds);
            _blackoutWindows.Add(window);
            window.Show();
        }
    }

    private void StopBlackout()
    {
        foreach (var window in _blackoutWindows.ToArray())
        {
            window.CloseFromAgent();
        }
        _blackoutWindows.Clear();
    }

    private async Task ShowMediaAsync(JsonElement payload)
    {
        var downloadUrl = payload.GetProperty("downloadUrl").GetString()
                          ?? throw new InvalidOperationException("Missing media URL.");
        var expectedHash = payload.GetProperty("sha256").GetString() ?? string.Empty;
        var fileName = Path.GetFileName(payload.GetProperty("fileName").GetString() ?? "media.bin");
        var contentType = payload.GetProperty("contentType").GetString() ?? "application/octet-stream";
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "iWebClassroom", "media");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{Guid.NewGuid():N}-{fileName}");
        using var client = new HttpClient { BaseAddress = new Uri(_settings.ServerUrl), Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.Add(HubProtocol.AgentIdHeader, _settings.AgentId.ToString());
        client.DefaultRequestHeaders.Add(HubProtocol.AgentTokenHeader, _settings.AgentToken);
        await using (var input = await client.GetStreamAsync(downloadUrl))
        await using (var output = File.Create(path))
        {
            await input.CopyToAsync(output);
        }
        await using (var stream = File.OpenRead(path))
        {
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream));
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
                throw new InvalidOperationException(AgentStrings.UpdateHashInvalid);
            }
        }
        await Dispatcher.InvokeAsync(() =>
        {
            StopMedia();
            _mediaPath = path;
            _mediaWindow = new MediaDisplayWindow(path, contentType);
            _mediaWindow.Show();
        });
    }

    private void StopMedia()
    {
        _mediaWindow?.CloseFromAgent();
        _mediaWindow = null;
        if (!string.IsNullOrWhiteSpace(_mediaPath))
        {
            try { File.Delete(_mediaPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            _mediaPath = null;
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            if (await _updates.CheckAndStartAsync(_settings, CancellationToken.None))
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    ActivityText.Text = AgentStrings.InstallingUpdate;
                });
                await DisposeAgentAsync();
                await Dispatcher.InvokeAsync(() => ((App)System.Windows.Application.Current).ShutdownAgent());
            }
        }
        catch (Exception exception)
        {
            _logs.Error("Update", "The update check failed.", exception, "update_check_failed");
            await Dispatcher.InvokeAsync(() => ActivityText.Text = $"{AgentStrings.UpdatePrefix}: {exception.Message}");
        }
    }

    private AgentRole SelectedRole() => ((ComboBoxItem)RoleCombo.SelectedItem).Tag?.ToString() == "Teacher"
        ? AgentRole.Teacher
        : AgentRole.Student;

    private void SharedComputerCheck_Changed(object sender, RoutedEventArgs e) => UpdateAssignmentMode();

    private void UpdateAssignmentMode()
    {
        if (ClassroomText is null || RoleCombo is null || SharedRoomsPanel is null)
        {
            return;
        }

        var shared = SharedComputerCheck.IsChecked == true;
        ClassroomText.IsEnabled = !shared;
        RoleCombo.IsEnabled = !shared;
        if (shared)
        {
            ClassroomText.Text = "PARTILHADO";
            RoleCombo.SelectedIndex = 0;
        }

        SharedRoomsPanel.Visibility = shared && _connection.IsConnected ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void JoinRoomButton_Click(object sender, RoutedEventArgs e)
    {
        var roomCode = RoomCodeText.Text.Trim();
        if (roomCode.Length != 6 || roomCode.Any(character => !char.IsAsciiDigit(character)))
        {
            MessageBox.Show(this, AgentStrings.InvalidRoomCode, AgentStrings.AppTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SetBusy(true, AgentStrings.Connecting);
        try
        {
            var studentName = Required(StudentNameText.Text, AgentStrings.StudentName);
            var resolution = await _roomJoinClient.ResolveAsync(_settings, roomCode, studentName, CancellationToken.None);
            LessonSessionSummary joined;
            if (resolution.IsLocal && resolution.Lesson is not null)
            {
                await _connection.ConnectAsync(_settings, CancellationToken.None);
                joined = resolution.Lesson;
            }
            else
            {
                joined = await JoinRemoteRoomAsync(resolution);
            }

            RoomCodeText.Clear();
            ActivityText.Text = AgentStrings.Format(nameof(AgentStrings.RoomJoined), joined.RoomName);
            _logs.Information("Room", $"Joined room {joined.RoomName} ({roomCode}).", "room_joined");
        }
        catch (Exception exception)
        {
            _logs.Error("Room", $"Could not join room {roomCode}.", exception, "room_join_failed");
            MessageBox.Show(this, exception.Message, AgentStrings.AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SetBusy(false, ActivityText.Text);
        }
    }

    private async Task<LessonSessionSummary> JoinRemoteRoomAsync(ResolveRoomResponse resolution)
    {
        if (string.IsNullOrWhiteSpace(resolution.HandoffToken) || resolution.ServerUrls.Count == 0)
        {
            throw new InvalidOperationException(AgentStrings.RoomLookupUnavailable);
        }

        Exception? lastError = null;
        foreach (var serverUrl in resolution.ServerUrls)
        {
            if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var destination))
            {
                continue;
            }

            try
            {
                var secureDestination = await _trustBootstrapper.EnsureTrustedForRoomAsync(
                    destination, resolution.HandoffToken, CancellationToken.None);
                var roaming = await _roomJoinClient.RoamAsync(
                    secureDestination, resolution.HandoffToken, CancellationToken.None);
                var roamingSettings = _settings with
                {
                    ServerUrl = secureDestination.ToString().TrimEnd('/'),
                    AgentId = roaming.Enrollment.AgentId,
                    AgentToken = roaming.Enrollment.AgentToken,
                    Role = AgentRole.Student,
                    ClassroomId = "PARTILHADO",
                    IsShared = true
                };
                await _connection.ConnectAsync(roamingSettings, CancellationToken.None);
                return roaming.Lesson;
            }
            catch (Exception exception)
            {
                lastError = exception;
            }
        }

        throw new InvalidOperationException(lastError?.Message ?? AgentStrings.RoomLookupUnavailable);
    }

    private static string Required(string value, string field)
    {
        var result = value.Trim();
        return string.IsNullOrWhiteSpace(result) ? throw new InvalidOperationException(AgentStrings.Format(nameof(AgentStrings.RequiredField), field)) : result;
    }

    private void SetBusy(bool busy, string activity)
    {
        _busy = busy;
        UpdateConnectionControls();
        ActivityText.Text = activity;
    }

    private async Task RecordUsageMinuteAsync()
    {
        try
        {
            await _usage.RecordConnectedMinuteAsync(_connection.IsConnected);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logs.Warning("Usage", "Agent usage statistics could not be persisted.", exception, "usage_persist_failed");
        }
    }

    private async Task UploadBackgroundDataAsync()
    {
        await _logs.UploadAsync(_settings, CancellationToken.None);
        try
        {
            await _usage.UploadAsync(_settings, CancellationToken.None);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
        {
            _logs.Warning("Usage", "Agent usage statistics could not be sent to the local server.", exception, "usage_upload_failed");
        }
    }

    private void UpdateConnectionControls()
    {
        ConnectButton.Visibility = _connection.IsConnected ? Visibility.Collapsed : Visibility.Visible;
        ConnectButton.IsEnabled = !_busy;
    }

    private void UpdateRolePanels()
    {
        var fixedTeacher = _profileIsAssigned && _settings.Role == AgentRole.Teacher && !_settings.IsShared &&
                           !string.Equals(_settings.ClassroomId, "PENDENTE", StringComparison.OrdinalIgnoreCase);
        TeacherConsolePanel.Visibility = fixedTeacher ? Visibility.Visible : Visibility.Collapsed;
        SharedRoomsPanel.Visibility = _settings.IsShared ? Visibility.Visible : Visibility.Collapsed;
        if (fixedTeacher && _connection.IsConnected && IsVisible)
        {
            _ = EnsureTeacherConsoleAsync();
        }
    }

    private async Task EnsureTeacherConsoleAsync()
    {
        await _teacherConsoleInitialization.WaitAsync();
        try
        {
            if (_closing)
            {
                return;
            }

            if (!Uri.TryCreate(_settings.ServerUrl, UriKind.Absolute, out var serverUri))
            {
                throw new InvalidOperationException(AgentStrings.InvalidServer);
            }

            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "iWebClassroom", "WebView2", "TeacherConsole");
            _teacherConsoleEnvironment ??= await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            if (TeacherConsole.CoreWebView2 is null)
            {
                await TeacherConsole.EnsureCoreWebView2Async(_teacherConsoleEnvironment);
            }
            var webView = TeacherConsole.CoreWebView2
                          ?? throw new InvalidOperationException("Microsoft Edge WebView2 could not be initialized.");
            if (!_teacherConsoleHandlersAttached)
            {
                webView.Settings.AreDevToolsEnabled = false;
                webView.Settings.AreDefaultContextMenusEnabled = false;
                webView.NavigationStarting += (_, args) =>
                {
                    if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var destination) &&
                        Uri.TryCreate(_settings.ServerUrl, UriKind.Absolute, out var allowed) &&
                        !string.Equals(destination.Authority, allowed.Authority, StringComparison.OrdinalIgnoreCase))
                    {
                        args.Cancel = true;
                    }
                };
                webView.NewWindowRequested += (_, args) =>
                {
                    args.Handled = true;
                    if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var destination) &&
                        Uri.TryCreate(_settings.ServerUrl, UriKind.Absolute, out var allowed) &&
                        string.Equals(destination.Authority, allowed.Authority, StringComparison.OrdinalIgnoreCase))
                    {
                        webView.Navigate(destination.AbsoluteUri);
                    }
                };
                webView.NavigationCompleted += (_, args) =>
                {
                    if (args.IsSuccess)
                    {
                        TeacherConsoleErrorPanel.Visibility = Visibility.Collapsed;
                        TeacherConsole.Visibility = Visibility.Visible;
                        return;
                    }

                    _logs.Warning(
                        "TeacherConsole",
                        $"The embedded teacher console navigation failed: {args.WebErrorStatus}.",
                        eventCode: "teacher_console_navigation_failed");
                    TeacherConsole.Visibility = Visibility.Collapsed;
                    TeacherConsoleErrorPanel.Visibility = Visibility.Visible;
                };
                webView.ProcessFailed += (_, args) =>
                {
                    _logs.Warning(
                        "TeacherConsole",
                        $"The embedded teacher console process failed: {args.ProcessFailedKind}.",
                        eventCode: "teacher_console_process_failed");
                    _teacherConsoleServer = null;
                    TeacherConsole.Visibility = Visibility.Collapsed;
                    TeacherConsoleErrorPanel.Visibility = Visibility.Visible;
                };
                _teacherConsoleHandlersAttached = true;
            }

            var target = new Uri(serverUri, "/").AbsoluteUri;
            if (!string.Equals(_teacherConsoleServer, target, StringComparison.OrdinalIgnoreCase))
            {
                webView.Navigate(target);
                _teacherConsoleServer = target;
            }
            TeacherConsoleErrorPanel.Visibility = Visibility.Collapsed;
            TeacherConsole.Visibility = Visibility.Visible;
        }
        catch (Exception exception)
        {
            _logs.Warning("TeacherConsole", "The embedded teacher console could not be initialized.", exception, "teacher_console_failed");
            TeacherConsole.Visibility = Visibility.Collapsed;
            TeacherConsoleErrorPanel.Visibility = Visibility.Visible;
        }
        finally
        {
            _teacherConsoleInitialization.Release();
        }
    }

    private async void RetryTeacherConsole_Click(object sender, RoutedEventArgs e)
    {
        _teacherConsoleServer = null;
        await EnsureTeacherConsoleAsync();
    }

    internal string TrayStatus => StatusText.Text;
    internal string TrayAssignment => AssignmentText.Text;
    internal string TrayServer => _settings.ServerUrl;
    internal string TrayComputer => string.IsNullOrWhiteSpace(_settings.DisplayName) ? Environment.MachineName : _settings.DisplayName;

    internal void ShowFromTray()
    {
        if (!IsVisible)
        {
            Show();
        }
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
        UpdateRolePanels();
    }

    internal async Task RefreshFromTrayAsync()
    {
        await RefreshProfileAsync();
        if (!_connection.IsConnected)
        {
            await ConnectAsync(reenroll: false);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!((App)System.Windows.Application.Current).IsShuttingDown)
        {
            e.Cancel = true;
            Hide();
            ((App)System.Windows.Application.Current).NotifyStillRunning();
            return;
        }

        base.OnClosing(e);
    }

    private async Task DisposeAgentAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _closing = true;
        _profileTimer.Stop();
        _logUploadTimer.Stop();
        _usageTimer.Stop();
        await UploadBackgroundDataAsync();
        await _studentCapture.DisposeAsync();
        await _screenCapture.DisposeAsync();
        await _connection.DisposeAsync();
        _screenWindow?.CloseFromAgent();
        StopBlackout();
        StopMedia();
        TeacherConsole.Dispose();
    }

    protected override async void OnClosed(EventArgs e)
    {
        await DisposeAgentAsync();
        base.OnClosed(e);
    }
}
