using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
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
    }

    private void ApplySettingsToForm()
    {
        ServerUrlText.Text = _settings.ServerUrl;
        DisplayNameText.Text = _settings.DisplayName;
        ClassroomText.Text = _settings.ClassroomId;
        RoleCombo.SelectedIndex = _settings.Role == AgentRole.Teacher ? 1 : 0;
        SharedComputerCheck.IsChecked = _settings.IsShared;
        UpdateAssignmentMode();
        IdentityText.Text = _settings.AgentId == Guid.Empty ? AgentStrings.NotEnrolled : _settings.AgentId.ToString();
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e) => await ConnectAsync(reenroll: SettingsChanged());

    private bool SettingsChanged()
    {
        return !string.Equals(_settings.ServerUrl.TrimEnd('/'), ServerUrlText.Text.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase) ||
               !string.Equals(_settings.DisplayName, DisplayNameText.Text.Trim(), StringComparison.Ordinal) ||
               !string.Equals(_settings.ClassroomId, ClassroomText.Text.Trim(), StringComparison.Ordinal) ||
               _settings.Role != SelectedRole() ||
               _settings.IsShared != (SharedComputerCheck.IsChecked == true);
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

            if (reenroll || _settings.AgentId == Guid.Empty || string.IsNullOrWhiteSpace(_settings.AgentToken))
            {
                if (string.IsNullOrWhiteSpace(EnrollmentCodeText.Password))
                {
                    throw new InvalidOperationException(AgentStrings.EnterEnrollmentCode);
                }

                _settings = await _enrollmentClient.EnrollAsync(
                    serverUri,
                    EnrollmentCodeText.Password,
                    Required(DisplayNameText.Text, AgentStrings.ComputerNameField),
                    SelectedRole(),
                    SharedComputerCheck.IsChecked == true ? "PARTILHADO" : Required(ClassroomText.Text, AgentStrings.ClassroomField),
                    SharedComputerCheck.IsChecked == true,
                    CancellationToken.None);
                await _settingsStore.SaveAsync(_settings);
                EnrollmentCodeText.Clear();
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
            StatusText.Text = AgentStrings.Connected;
            ShareButton.IsEnabled = _settings.Role == AgentRole.Teacher;
            SharedRoomsPanel.Visibility = _settings.IsShared ? Visibility.Visible : Visibility.Collapsed;
            if (_settings.IsShared)
            {
                await RefreshRoomsAsync();
            }
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
                case ClassroomCommandKind.StartScreenShare when _settings.Role == AgentRole.Teacher:
                    await Dispatcher.InvokeAsync(StartSharing);
                    break;
                case ClassroomCommandKind.StopScreenShare when _settings.Role == AgentRole.Teacher:
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

    private async void RefreshRoomsButton_Click(object sender, RoutedEventArgs e) => await RefreshRoomsAsync();

    private async Task RefreshRoomsAsync()
    {
        if (!_settings.IsShared || _settings.AgentId == Guid.Empty)
        {
            return;
        }

        try
        {
            var rooms = await _roomJoinClient.ListAsync(_settings, CancellationToken.None);
            AvailableRoomsCombo.ItemsSource = rooms;
            AvailableRoomsCombo.SelectedIndex = rooms.Count > 0 ? 0 : -1;
            ActivityText.Text = rooms.Count == 0 ? AgentStrings.NoRoomsAvailable : string.Empty;
        }
        catch (Exception exception)
        {
            ActivityText.Text = exception.Message;
        }
    }

    private async void JoinRoomButton_Click(object sender, RoutedEventArgs e)
    {
        if (AvailableRoomsCombo.SelectedItem is not AvailableRoomSummary room)
        {
            MessageBox.Show(this, AgentStrings.SelectRoom, AgentStrings.AppTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var studentName = Required(StudentNameText.Text, AgentStrings.StudentName);
            var joined = await _roomJoinClient.JoinAsync(_settings, room.LessonId, RoomPinText.Password.Trim(), studentName, CancellationToken.None);
            RoomPinText.Clear();
            ActivityText.Text = AgentStrings.Format(nameof(AgentStrings.RoomJoined), joined.RoomName);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, AgentStrings.AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
        await _screenCapture.DisposeAsync();
        await _connection.DisposeAsync();
        _screenWindow?.CloseFromAgent();
        base.OnClosed(e);
    }
}
