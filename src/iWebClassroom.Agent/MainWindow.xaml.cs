using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using IWebClassroom.Agent.Configuration;
using IWebClassroom.Agent.Localization;
using IWebClassroom.Agent.Services;
using IWebClassroom.Contracts;

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
    private readonly UpdateService _updates = new();
    private readonly DispatcherTimer _profileTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private AgentSettings _settings = AgentSettings.Empty;
    private ScreenDisplayWindow? _screenWindow;
    private bool _closing;

    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = $"v{Assembly.GetExecutingAssembly().GetName().Version}";
        _connection.CommandReceived += HandleCommandAsync;
        _connection.ScreenFrameReceived += HandleScreenFrame;
        _connection.ScreenStopped += HandleScreenStopped;
        _connection.StateChanged += state => Dispatcher.Invoke(() => StatusText.Text = state);
        _profileTimer.Tick += async (_, _) => await RefreshProfileAsync();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = await _settingsStore.LoadAsync();
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
    }

    private void ApplySettingsToForm()
    {
        ServerUrlText.Text = _settings.ServerUrl;
        DisplayNameText.Text = _settings.DisplayName;
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
            await RefreshProfileAsync();
            StatusText.Text = AgentStrings.Connected;
            ShareButton.IsEnabled = _settings.Role == AgentRole.Teacher;
            SharedRoomsPanel.Visibility = _settings.IsShared ? Visibility.Visible : Visibility.Collapsed;
            _ = CheckForUpdatesAsync();
        }
        catch (Exception exception)
        {
            StatusText.Text = AgentStrings.ConnectionError;
            MessageBox.Show(this, exception.Message, AgentStrings.AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
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
        }
        catch
        {
            // The current connection remains usable; retry on the next interval.
        }
    }

    private async Task HandleCommandAsync(ClassroomCommand command)
    {
        try
        {
            switch (command.Kind)
            {
                case ClassroomCommandKind.ShowMessage:
                    var message = command.Payload.GetProperty("message").GetString() ?? string.Empty;
                    await Dispatcher.InvokeAsync(() => MessageBox.Show(message, AgentStrings.TeacherMessageTitle, MessageBoxButton.OK, MessageBoxImage.Information));
                    break;
                case ClassroomCommandKind.LaunchApplication:
                    _applications.Launch(command.Payload.GetProperty("applicationId").GetString() ?? string.Empty);
                    break;
                case ClassroomCommandKind.StartScreenShare:
                    await Dispatcher.InvokeAsync(StartSharing);
                    break;
                case ClassroomCommandKind.StopScreenShare:
                    await StopSharingAsync();
                    break;
                case ClassroomCommandKind.CheckForUpdate:
                    await CheckForUpdatesAsync();
                    break;
            }
        }
        catch (Exception exception)
        {
            await Dispatcher.InvokeAsync(() => ActivityText.Text = $"{AgentStrings.FailurePrefix}: {exception.Message}");
        }
    }

    private void HandleScreenFrame(byte[] frame)
    {
        if (_settings.Role != AgentRole.Student || _closing)
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            _screenWindow ??= new ScreenDisplayWindow();
            _screenWindow.SetFrame(frame);
            if (!_screenWindow.IsVisible)
            {
                _screenWindow.Show();
            }
        });
    }

    private void HandleScreenStopped() => Dispatcher.Invoke(() =>
    {
        _screenWindow?.CloseFromAgent();
        _screenWindow = null;
    });

    private void ShareButton_Click(object sender, RoutedEventArgs e) => StartSharing();

    private void StartSharing()
    {
        if (_settings.Role != AgentRole.Teacher || !_connection.IsConnected)
        {
            return;
        }

        _screenCapture.Start(_connection.PublishFrameAsync);
        ShareButton.IsEnabled = false;
        StopShareButton.IsEnabled = true;
        ActivityText.Text = AgentStrings.ScreenSharing;
    }

    private async void StopShareButton_Click(object sender, RoutedEventArgs e) => await StopSharingAsync();

    private async Task StopSharingAsync()
    {
        await _screenCapture.StopAsync();
        if (_connection.IsConnected)
        {
            await _connection.PublishStoppedAsync(CancellationToken.None);
        }

        await Dispatcher.InvokeAsync(() =>
        {
            ShareButton.IsEnabled = _settings.Role == AgentRole.Teacher && _connection.IsConnected;
            StopShareButton.IsEnabled = false;
            ActivityText.Text = string.Empty;
        });
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
                    Close();
                });
            }
        }
        catch (Exception exception)
        {
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
        }
        catch (Exception exception)
        {
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
        ConnectButton.IsEnabled = !busy;
        ActivityText.Text = activity;
    }

    protected override async void OnClosed(EventArgs e)
    {
        _closing = true;
        _profileTimer.Stop();
        await _screenCapture.DisposeAsync();
        await _connection.DisposeAsync();
        _screenWindow?.CloseFromAgent();
        base.OnClosed(e);
    }
}
