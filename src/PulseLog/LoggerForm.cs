using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace HeartRateLogger;

internal sealed partial class LoggerForm : Form
{
    private const int ImmersiveDarkModeAttribute = 20;
    private const int SettingToggleWidth = 112;

    private static readonly ThemePalette DarkPalette = new(
        Canvas: Color.FromArgb(17, 25, 23),
        Surface: Color.FromArgb(27, 39, 35),
        Ink: Color.FromArgb(228, 241, 234),
        Muted: Color.FromArgb(171, 192, 182),
        Accent: Color.FromArgb(140, 221, 173),
        Tint: Color.FromArgb(38, 59, 50),
        Border: Color.FromArgb(67, 91, 80),
        Error: Color.FromArgb(255, 170, 157),
        PrimaryInk: Color.FromArgb(17, 33, 26),
        PrimaryHover: Color.FromArgb(164, 231, 191),
        PrimaryPressed: Color.FromArgb(111, 190, 146));

    private static readonly ThemePalette LightPalette = new(
        Canvas: Color.FromArgb(245, 246, 242),
        Surface: Color.White,
        Ink: Color.FromArgb(16, 45, 40),
        Muted: Color.FromArgb(76, 102, 93),
        Accent: Color.FromArgb(20, 99, 78),
        Tint: Color.FromArgb(224, 240, 230),
        Border: Color.FromArgb(210, 223, 215),
        Error: Color.FromArgb(153, 48, 42),
        PrimaryInk: Color.White,
        PrimaryHover: Color.FromArgb(23, 115, 89),
        PrimaryPressed: Color.FromArgb(16, 45, 40));

    private readonly CancellationTokenSource _lifetime = new();
    private readonly ToolTip _hints = new();
    private readonly ThemeComboBox _devices = new();
    private readonly ThemeComboBox _chartWindow = new();
    private readonly List<Label> _labels = new();
    private readonly List<TableLayoutPanel> _cards = new();
    private readonly List<TableLayoutPanel> _metricTiles = new();
    private readonly List<RadioButton> _chartModes = new();
    private readonly Label _deviceStatus;
    private readonly Label _fileStatus;
    private readonly Label _settingsStatus;
    private readonly RadioButton _dashboardTab = MakeTab("Dashboard");
    private readonly RadioButton _settingsTab = MakeTab("Settings");
    private readonly CheckBox _closeToTray = MakeSettingToggle("Close to tray");
    private readonly CheckBox _themeToggle = MakeSettingToggle("Dark mode");
    private readonly Button _scan = MakeButton("Scan");
    private readonly Button _connect = MakeButton("Connect");
    private readonly Button _recording = MakeButton("Pause recording");
    private readonly Button _openLogs = MakeButton("Open folder");
    private readonly Button _export = MakeButton("Export CSV");
    private readonly HeartRateHero _hero = new();
    private readonly HeartRateChart _chart = new();
    private readonly Label _minimum;
    private readonly Label _maximum;
    private readonly Label _average;

    private HeartRateMonitorClient? _client;
    private SessionLog? _activeLog;
    private SessionLog? _lastLog;
    private List<HeartRateReading>? _pendingReadings;
    private bool _scanning;
    private bool _connecting;
    private bool _droppedDuringConnect;
    private bool _exporting;
    private bool _recordingPaused;
    private bool _hasLiveReading;
    private volatile bool _closing;
    private string? _connectedName;
    private long _sampleCount;
    private int _minBpm;
    private int _maxBpm;
    private double _meanBpm;
    private bool DarkMode => _themeToggle.Checked;
    private ThemePalette Palette => DarkMode ? DarkPalette : LightPalette;

    internal LoggerForm()
    {
        _deviceStatus = MakeLabel(9.5f, FontStyle.Regular, tone: LabelTone.Muted);
        _fileStatus = MakeLabel(9.5f, FontStyle.Regular, tone: LabelTone.Muted);
        _settingsStatus = MakeLabel(9.5f, FontStyle.Regular, tone: LabelTone.Muted);
        _minimum = MakeLabel(17, FontStyle.Bold);
        _maximum = MakeLabel(17, FontStyle.Bold);
        _average = MakeLabel(17, FontStyle.Bold);
        _themeToggle.Checked = true;
        Text = "Pulse Log";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1000, 743);
        MinimumSize = new Size(740, 680);
        BackColor = Palette.Canvas;
        ForeColor = Palette.Ink;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(28, 18, 28, 14)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(BuildHeader(), 0, 0);
        layout.Controls.Add(BuildPages(), 0, 1);
        Controls.Add(layout);
        InitializeTray();
        _closeToTray.Checked = TrayPreference.Load();
        _closeToTray.CheckedChanged += (_, _) =>
        {
            StyleSettingToggle(_closeToTray);
            try
            {
                TrayPreference.Save(_closeToTray.Checked);
                SetStatus(_settingsStatus, string.Empty, error: false);
            }
            catch (Exception error) when (IsFileError(error))
            {
                SetStatus(_settingsStatus, $"Could not save Close to tray. {DescribeStorageError(error)}", error: true);
            }
        };

        _themeToggle.CheckedChanged += (_, _) => ApplyTheme();
        _scan.Click += async (_, _) => await ScanAsync();
        _connect.Click += async (_, _) =>
        {
            if (_activeLog is not null)
            {
                EndSession("Disconnected. Scan or connect again to start a new session.");
            }
            else if (_devices.SelectedItem is HeartRateMonitorInfo monitor)
            {
                await ConnectAsync(monitor);
            }
        };
        _recording.Click += (_, _) => ToggleRecording();
        _openLogs.Click += (_, _) => OpenLogs();
        _export.Click += async (_, _) => await ExportAsync();

        SetDeviceStatus("Wake your monitor, turn on Bluetooth, then select Scan.");
        SetFileStatus("Sessions and CSV exports save beside PulseLog.exe.");
        _hero.SetReading(null);
        UpdateStatsValues();
        UpdateButtons();
        ApplyTheme();
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 7),
            ColumnCount = 2,
            RowCount = 2
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 206));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        header.Controls.Add(MakeLabel(30, FontStyle.Bold, "Pulse Log"), 0, 0);
        header.Controls.Add(MakeLabel(10.5f, FontStyle.Regular, "Bluetooth heart rate logger", LabelTone.Muted), 0, 1);

        var tabs = new TableLayoutPanel
        {
            AccessibleName = "Pulse Log pages",
            AccessibleRole = AccessibleRole.PageTabList,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 7, 0, 4),
            ColumnCount = 3,
            RowCount = 1
        };
        tabs.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        tabs.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 6));
        tabs.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        _dashboardTab.CheckedChanged += (_, _) => StyleChoiceButton(_dashboardTab);
        _settingsTab.CheckedChanged += (_, _) => StyleChoiceButton(_settingsTab);
        tabs.Controls.Add(_dashboardTab, 0, 0);
        tabs.Controls.Add(_settingsTab, 2, 0);
        _dashboardTab.Checked = true;
        header.Controls.Add(tabs, 1, 0);
        return header;
    }

    private Control BuildPages()
    {
        var pages = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var dashboard = BuildDashboard();
        var settings = BuildSettings();
        settings.Visible = false;
        pages.Controls.Add(settings);
        pages.Controls.Add(dashboard);
        _settingsTab.CheckedChanged += (_, _) =>
        {
            dashboard.Visible = !_settingsTab.Checked;
            settings.Visible = _settingsTab.Checked;
        };
        return pages;
    }

    private Control BuildDashboard()
    {
        var dashboard = new TableLayoutPanel
        {
            AccessibleName = "Dashboard",
            AccessibleRole = AccessibleRole.PropertyPage,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 1,
            RowCount = 4
        };
        dashboard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        dashboard.RowStyles.Add(new RowStyle(SizeType.Absolute, 99));
        dashboard.RowStyles.Add(new RowStyle(SizeType.Absolute, 144));
        dashboard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        dashboard.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        dashboard.Controls.Add(BuildDevices(), 0, 0);
        dashboard.Controls.Add(BuildSummary(), 0, 1);
        dashboard.Controls.Add(BuildChart(), 0, 2);
        dashboard.Controls.Add(BuildFooter(), 0, 3);
        return dashboard;
    }

    private Control BuildSettings()
    {
        var settings = new TableLayoutPanel
        {
            AccessibleName = "Settings",
            AccessibleRole = AccessibleRole.PropertyPage,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 1,
            RowCount = 5
        };
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 83));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 182));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        settings.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var appearance = MakeCard(2, 1);
        appearance.Padding = new Padding(16, 12, 16, 12);
        appearance.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        appearance.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, SettingToggleWidth));
        appearance.Controls.Add(MakeLabel(11, FontStyle.Bold, "Dark mode"), 0, 0);
        appearance.Controls.Add(_themeToggle, 1, 0);
        settings.Controls.Add(appearance, 0, 0);

        var window = MakeCard(2, 2);
        window.Padding = new Padding(16, 10, 16, 10);
        window.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        window.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, SettingToggleWidth));
        window.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        window.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        window.Controls.Add(MakeLabel(11, FontStyle.Bold, "Close to tray"), 0, 0);
        window.Controls.Add(MakeLabel(9.5f, FontStyle.Regular, "Hide Pulse Log in the system tray when you click X.", LabelTone.Muted), 0, 1);
        window.Controls.Add(_closeToTray, 1, 0);
        window.SetRowSpan(_closeToTray, 2);
        settings.Controls.Add(window, 0, 1);
        settings.Controls.Add(BuildMonitorDetails(), 0, 2);
        settings.Controls.Add(_settingsStatus, 0, 3);
        return settings;
    }

    private Control BuildDevices()
    {
        var card = MakeCard(4, 3);
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 87));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 149));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 21));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var heading = MakeLabel(11, FontStyle.Bold, "Heart rate monitor");
        card.Controls.Add(heading, 0, 0);
        card.SetColumnSpan(heading, 4);

        _devices.AccessibleName = "Bluetooth heart rate monitor";
        _devices.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        ConfigureComboBox(_devices);
        _devices.Margin = new Padding(0, 0, 8, 0);
        _devices.DisplayMember = nameof(HeartRateMonitorInfo.DisplayName);
        _devices.SelectedIndexChanged += (_, _) => UpdateButtons();
        _scan.Margin = new Padding(0, 0, 8, 0);
        _connect.Margin = new Padding(0, 0, 8, 0);
        card.Controls.Add(_devices, 0, 1);
        card.Controls.Add(_scan, 1, 1);
        card.Controls.Add(_connect, 2, 1);
        card.Controls.Add(_recording, 3, 1);
        card.Controls.Add(_deviceStatus, 0, 2);
        card.SetColumnSpan(_deviceStatus, 4);
        return card;
    }

    private Control BuildSummary()
    {
        var summary = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 12),
            ColumnCount = 2,
            RowCount = 1
        };
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 53));
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47));
        summary.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _hero.Dock = DockStyle.Fill;
        _hero.Margin = new Padding(0, 0, 12, 0);
        summary.Controls.Add(_hero, 0, 0);

        var stats = MakeCard(3, 2);
        stats.Margin = Padding.Empty;
        stats.Padding = new Padding(14, 9, 14, 10);
        stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        stats.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        stats.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var statsHeading = MakeLabel(11, FontStyle.Bold, "Session stats · BPM");
        stats.Controls.Add(statsHeading, 0, 0);
        stats.SetColumnSpan(statsHeading, 3);
        stats.Controls.Add(BuildStat("Minimum", _minimum), 0, 1);
        stats.Controls.Add(BuildStat("Maximum", _maximum), 1, 1);
        stats.Controls.Add(BuildStat("Average", _average), 2, 1);
        summary.Controls.Add(stats, 1, 0);
        return summary;
    }

    private Control BuildChart()
    {
        _chart.Dock = DockStyle.Fill;
        _chart.Margin = Padding.Empty;

        var toolbar = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            WrapContents = false
        };
        var options = new[]
        {
            (Label: "Max", Mode: HeartRateChartMode.Maximum, Width: 48, Description: "Maximum per chart interval"),
            (Label: "Min", Mode: HeartRateChartMode.Minimum, Width: 48, Description: "Minimum per chart interval"),
            (Label: "Min + Max", Mode: HeartRateChartMode.Range, Width: 84, Description: "Minimum and maximum range per chart interval"),
            (Label: "Avg", Mode: HeartRateChartMode.Average, Width: 48, Description: "Average per chart interval")
        };

        foreach (var option in options)
        {
            var button = new RadioButton
            {
                Text = option.Label,
                AccessibleName = option.Description,
                Tag = option.Mode,
                Appearance = Appearance.Button,
                TextAlign = ContentAlignment.MiddleCenter,
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false,
                Font = new Font("Segoe UI", 9.5f),
                Size = new Size(option.Width, 28),
                Margin = new Padding(0, 0, 4, 0)
            };
            button.CheckedChanged += (_, _) =>
            {
                StyleChoiceButton(button);
                if (button.Checked)
                {
                    _chart.SetMode(option.Mode);
                }
            };
            _chartModes.Add(button);
            toolbar.Controls.Add(button);
            if (option.Mode == HeartRateChartMode.Range)
            {
                button.Checked = true;
            }
        }

        _chartWindow.AccessibleName = "Chart time range";
        ConfigureComboBox(_chartWindow);
        _chartWindow.Font = new Font("Segoe UI", 9.5f);
        _chartWindow.Width = 146;
        _chartWindow.Margin = new Padding(8, 2, 0, 0);

        var windows = new[]
        {
            (Label: "Last 1 minute", Minutes: 1),
            (Label: "Last 2 minutes", Minutes: 2),
            (Label: "Last 5 minutes", Minutes: 5),
            (Label: "Last 15 minutes", Minutes: 15),
            (Label: "Last 30 minutes", Minutes: 30),
            (Label: "Last hour", Minutes: 60),
            (Label: "Last 2 hours", Minutes: 120),
            (Label: "Last 6 hours", Minutes: 360),
            (Label: "Last 12 hours", Minutes: 720),
            (Label: "Last 24 hours", Minutes: 1440)
        };
        _chartWindow.MaxDropDownItems = windows.Length;
        _chartWindow.Items.AddRange(windows.Select(window => window.Label).ToArray());
        _chartWindow.SelectedIndexChanged += (_, _) =>
        {
            if (_chartWindow.SelectedIndex >= 0)
            {
                _chart.SetWindow(TimeSpan.FromMinutes(windows[_chartWindow.SelectedIndex].Minutes));
            }
        };
        _chartWindow.SelectedIndex = 1;
        toolbar.Controls.Add(_chartWindow);
        toolbar.Size = toolbar.PreferredSize;
        toolbar.Location = new Point(_chart.ClientSize.Width - toolbar.Width - 22, 13);
        _chart.Controls.Add(toolbar);
        return _chart;
    }

    private Control BuildFooter()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 10, 0, 0),
            Margin = Padding.Empty,
            RowCount = 1,
            ColumnCount = 3
        };
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 121));

        _openLogs.Margin = new Padding(0, 4, 8, 4);
        _export.Margin = new Padding(0, 4, 0, 4);
        footer.Controls.Add(_fileStatus, 0, 0);
        footer.Controls.Add(_openLogs, 1, 0);
        footer.Controls.Add(_export, 2, 0);
        return footer;
    }

    private async Task ScanAsync()
    {
        _scanning = true;
        UpdateButtons();
        SetDeviceStatus("Scanning for nearby Bluetooth LE devices…");
        try
        {
            var previous = _devices.SelectedItem as HeartRateMonitorInfo;
            var found = await HeartRateMonitorClient.ScanAsync(_lifetime.Token);
            if (_closing)
            {
                return;
            }

            _devices.BeginUpdate();
            try
            {
                _devices.Items.Clear();
                foreach (var monitor in found)
                {
                    _devices.Items.Add(monitor);
                }

                if (found.Count > 0)
                {
                    var selected = previous is null ? -1 : Array.FindIndex(found.ToArray(), monitor =>
                        (previous.DeviceId is not null && previous.DeviceId == monitor.DeviceId) ||
                        (previous.Address is not null && previous.Address == monitor.Address));
                    _devices.SelectedIndex = selected >= 0 ? selected : 0;
                }
            }
            finally
            {
                _devices.EndUpdate();
            }

            SetDeviceStatus(found.Count == 0
                ? "No devices found. Wake your monitor, keep it nearby, then scan again."
                : found.Any(monitor => monitor.AdvertisesHeartRateService)
                    ? "Select your monitor and choose Connect to start recording."
                    : "Select your monitor to check for the Bluetooth Heart Rate Service.");
        }
        catch (OperationCanceledException) when (_closing)
        {
        }
        catch (Exception error) when (IsDeviceError(error))
        {
            if (!_closing)
            {
                SetDeviceStatus(DescribeError(error), error: true);
            }
        }
        finally
        {
            _scanning = false;
            if (!_closing)
            {
                UpdateButtons();
            }
        }
    }

    private async Task ConnectAsync(HeartRateMonitorInfo monitor)
    {
        _connecting = true;
        _droppedDuringConnect = false;
        _pendingReadings = new List<HeartRateReading>();
        var client = new HeartRateMonitorClient();
        _client = client;
        client.ReadingReceived += reading => PostToForm(() => ReceiveReading(client, reading));
        client.Disconnected += () => PostToForm(() => HandleDisconnect(client));
        UpdateButtons();
        SetDeviceStatus($"Connecting to {monitor.Name}…");

        try
        {
            await client.ConnectAsync(monitor, _lifetime.Token);
            if (_closing)
            {
                return;
            }

            if (_droppedDuringConnect || !client.IsConnected)
            {
                throw new InvalidOperationException("The monitor connection was lost during setup. Try connecting again.");
            }

            try
            {
                _activeLog = SessionLog.Start();
            }
            catch (Exception error) when (IsFileError(error))
            {
                EndSession($"Could not start the session. {DescribeStorageError(error)}", error: true);
                return;
            }

            _lastLog = _activeLog;
            _connectedName = monitor.Name;
            ShowMonitorConnected(monitor.Name);
            _recordingPaused = false;
            _hasLiveReading = false;
            _sampleCount = 0;
            _meanBpm = 0;
            _hero.SetReading(null);
            _chart.ClearReadings();
            UpdateStatsValues();
            SetDeviceStatus($"Connected to {_connectedName}. Waiting for readings…");
            SetFileStatus("Recording beside PulseLog.exe.");

            var pending = _pendingReadings;
            _pendingReadings = null;
            if (pending is not null)
            {
                foreach (var reading in pending)
                {
                    ReceiveReading(client, reading);
                }
            }
        }
        catch (OperationCanceledException) when (_closing)
        {
        }
        catch (Exception error) when (IsDeviceError(error))
        {
            if (!_closing)
            {
                EndSession(DescribeError(error), error: true);
            }
        }
        finally
        {
            _connecting = false;
            if (!_closing)
            {
                UpdateButtons();
            }
        }
    }

    private void ReceiveReading(HeartRateMonitorClient client, HeartRateReading reading)
    {
        if (!ReferenceEquals(_client, client))
        {
            return;
        }

        if (_activeLog is null)
        {
            _pendingReadings?.Add(reading);
            return;
        }

        var bpm = reading.BeatsPerMinute;
        _hero.SetReading(bpm);
        UpdateMonitorDetails(reading);
        if (!_hasLiveReading)
        {
            _hasLiveReading = true;
            SetDeviceStatus($"Connected to {_connectedName}. Receiving readings.");
        }

        if (_recordingPaused)
        {
            return;
        }

        try
        {
            _activeLog.Append(reading);
        }
        catch (Exception error) when (IsFileError(error))
        {
            EndSession($"Recording stopped. {DescribeStorageError(error)}", error: true);
            return;
        }

        _sampleCount++;
        _minBpm = _sampleCount == 1 ? bpm : Math.Min(_minBpm, bpm);
        _maxBpm = _sampleCount == 1 ? bpm : Math.Max(_maxBpm, bpm);
        _meanBpm += (bpm - _meanBpm) / _sampleCount;
        _chart.AddReading(reading.Timestamp, bpm);
        UpdateStatsValues();

        if (_sampleCount == 1)
        {
            UpdateButtons();
        }
    }

    private void ToggleRecording()
    {
        if (_activeLog is null)
        {
            return;
        }

        _recordingPaused = !_recordingPaused;
        if (_recordingPaused)
        {
            _chart.BreakTrace();
        }

        SetFileStatus(_recordingPaused ? "Recording paused. Live BPM still updates." : "Recording beside PulseLog.exe.");
        UpdateButtons();
    }

    private void HandleDisconnect(HeartRateMonitorClient client)
    {
        if (!ReferenceEquals(_client, client))
        {
            return;
        }

        if (_activeLog is null)
        {
            _droppedDuringConnect = true;
            return;
        }

        EndSession("Monitor connection lost. Reconnect to start a new session.", error: true);
    }

    private void EndSession(string message, bool error = false)
    {
        var client = _client;
        _client = null;
        _pendingReadings = null;
        var log = _activeLog;
        _activeLog = null;
        _recordingPaused = false;
        _connectedName = null;
        ClearMonitorDetails();
        client?.Dispose();

        try
        {
            log?.Dispose();
            if (log is not null)
            {
                _hero.SetSessionEnded();
                SetFileStatus("Session saved beside PulseLog.exe.");
            }
        }
        catch (Exception fileError) when (IsFileError(fileError))
        {
            SetFileStatus($"Could not finish saving the session. {fileError.Message}", error: true);
        }

        SetDeviceStatus(message, error);
        UpdateButtons();
    }

    private async Task ExportAsync()
    {
        if (_lastLog is not { } log || _exporting)
        {
            return;
        }

        _exporting = true;
        UpdateButtons();
        try
        {
            var destination = await Task.Run(log.Export);
            if (!_closing)
            {
                SetFileStatus("CSV exported beside PulseLog.exe.");
                _hints.SetToolTip(_fileStatus, destination);
            }
        }
        catch (Exception error) when (IsFileError(error))
        {
            if (!_closing)
            {
                SetFileStatus($"Could not export CSV. {DescribeStorageError(error)}", error: true);
            }
        }
        finally
        {
            _exporting = false;
            if (!_closing)
            {
                UpdateButtons();
            }
        }
    }

    private void OpenLogs()
    {
        try
        {
            using var folder = Process.Start(new ProcessStartInfo(SessionLog.DirectoryPath) { UseShellExecute = true });
        }
        catch (Exception error) when (IsFileError(error) || error is System.ComponentModel.Win32Exception)
        {
            SetFileStatus($"Could not open the program folder. {error.Message}", error: true);
        }
    }

    private TableLayoutPanel BuildStat(string name, Label value)
    {
        var metric = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(3, 0, 3, 0),
            Padding = new Padding(3, 7, 3, 7),
            ColumnCount = 1,
            RowCount = 2
        };
        metric.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        metric.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
        metric.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var caption = MakeLabel(9.5f, FontStyle.Regular, name, LabelTone.Muted);
        caption.TextAlign = ContentAlignment.MiddleCenter;
        value.AccessibleName = $"Session {name.ToLowerInvariant()} heart rate in BPM";
        value.TextAlign = ContentAlignment.MiddleCenter;
        metric.Controls.Add(caption, 0, 0);
        metric.Controls.Add(value, 0, 1);
        _metricTiles.Add(metric);
        return metric;
    }

    private void UpdateStatsValues()
    {
        _minimum.Text = _sampleCount == 0 ? "—" : _minBpm.ToString(CultureInfo.CurrentCulture);
        _maximum.Text = _sampleCount == 0 ? "—" : _maxBpm.ToString(CultureInfo.CurrentCulture);
        _average.Text = _sampleCount == 0 ? "—" : _meanBpm.ToString("0.0", CultureInfo.CurrentCulture);
    }

    private void UpdateButtons()
    {
        var active = _activeLog is not null;
        _scan.Enabled = !active && !_scanning && !_connecting;
        _scan.Text = _scanning ? "Scanning…" : "Scan";
        _devices.Enabled = !active && !_scanning && !_connecting && _devices.Items.Count > 0;
        _connect.Enabled = !_scanning && !_connecting && (active || _devices.SelectedItem is not null);
        _connect.Text = active ? "Disconnect" : _connecting ? "Connecting…" : "Connect";
        _recording.Enabled = active;
        _recording.Text = _recordingPaused ? "Resume recording" : "Pause recording";
        _export.Enabled = _lastLog is not null && _sampleCount > 0 && !_exporting;
        ApplyButtonTheme();
        RefreshTray();
    }

    private void ApplyTheme()
    {
        var palette = Palette;
        BackColor = palette.Canvas;
        ForeColor = palette.Ink;
        foreach (var card in _cards)
        {
            card.BackColor = palette.Surface;
        }
        foreach (var tile in _metricTiles)
        {
            tile.BackColor = palette.Canvas;
        }
        foreach (var label in _labels)
        {
            ApplyLabelTheme(label);
        }
        foreach (var button in _chartModes)
        {
            StyleChoiceButton(button);
        }
        StyleChoiceButton(_dashboardTab);
        StyleChoiceButton(_settingsTab);
        foreach (var combo in new[] { _devices, _chartWindow })
        {
            combo.ApplyTheme(palette, DarkMode);
        }
        StyleSettingToggle(_themeToggle);
        StyleSettingToggle(_closeToTray);
        ApplyButtonTheme();
        _hero.SetDarkMode(DarkMode);
        _chart.SetDarkMode(DarkMode);
        _hints.BackColor = palette.Surface;
        _hints.ForeColor = palette.Ink;
        ApplyTrayTheme();
        ApplyTitleBarTheme();
    }

    private void ApplyButtonTheme()
    {
        StyleButton(_scan);
        StyleButton(_connect, primary: true);
        StyleButton(_recording);
        StyleButton(_openLogs);
        StyleButton(_export, primary: true);
    }

    private void StyleButton(Button button, bool primary = false)
    {
        var palette = Palette;
        var accent = primary && button.Enabled;
        button.BackColor = accent ? palette.Accent : primary ? palette.Tint : palette.Surface;
        button.ForeColor = accent ? palette.PrimaryInk : button.Enabled ? palette.Ink : palette.Muted;
        button.FlatAppearance.BorderColor = accent ? palette.Accent : palette.Border;
        button.FlatAppearance.MouseOverBackColor = accent ? palette.PrimaryHover : palette.Canvas;
        button.FlatAppearance.MouseDownBackColor = accent ? palette.PrimaryPressed : palette.Tint;
    }

    private void StyleSettingToggle(CheckBox toggle)
    {
        toggle.Text = toggle.Checked ? "On" : "Off";
        StyleChoiceButton(toggle, toggle.Checked);
    }

    private void StyleChoiceButton(RadioButton button) => StyleChoiceButton(button, button.Checked);

    private void StyleChoiceButton(ButtonBase button, bool selected)
    {
        var palette = Palette;
        button.BackColor = selected ? palette.Tint : palette.Surface;
        button.ForeColor = selected ? palette.Accent : palette.Ink;
        button.FlatAppearance.BorderColor = selected ? palette.Accent : palette.Border;
        button.FlatAppearance.CheckedBackColor = palette.Tint;
        button.FlatAppearance.MouseOverBackColor = palette.Canvas;
        button.FlatAppearance.MouseDownBackColor = palette.Tint;
    }

    private void ApplyLabelTheme(Label label)
    {
        label.ForeColor = label.Tag switch
        {
            LabelTone.Muted => Palette.Muted,
            LabelTone.Error => Palette.Error,
            _ => Palette.Ink
        };
    }

    private void ConfigureComboBox(ComboBox combo)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.FlatStyle = FlatStyle.Flat;
        combo.DrawMode = DrawMode.OwnerDrawFixed;
        combo.DrawItem += (_, e) =>
        {
            var selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            var palette = Palette;
            using var background = new SolidBrush(selected ? palette.Tint : palette.Canvas);
            e.Graphics.FillRectangle(background, e.Bounds);
            if (e.Index >= 0 && e.Index < combo.Items.Count)
            {
                var ink = selected ? palette.Accent : combo.Enabled ? palette.Ink : palette.Muted;
                var bounds = Rectangle.Inflate(e.Bounds, -3, 0);
                TextRenderer.DrawText(e.Graphics, combo.GetItemText(combo.Items[e.Index]), combo.Font, bounds, ink,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            if (selected)
            {
                e.DrawFocusRectangle();
            }
        };
    }

    private void SetDeviceStatus(string message, bool error = false) => SetStatus(_deviceStatus, message, error);
    private void SetFileStatus(string message, bool error = false) => SetStatus(_fileStatus, message, error);

    private void SetStatus(Label label, string message, bool error)
    {
        label.Tag = error ? LabelTone.Error : LabelTone.Muted;
        ApplyLabelTheme(label);
        label.Text = message;
        _hints.SetToolTip(label, message);
    }

    private void PostToForm(Action action)
    {
        if (_closing)
        {
            return;
        }

        try
        {
            BeginInvoke(() =>
            {
                if (!_closing)
                {
                    action();
                }
            });
        }
        catch (InvalidOperationException) when (_closing || IsDisposed)
        {
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var area = Screen.FromControl(this).WorkingArea;
        if (Width > area.Width || Height > area.Height)
        {
            Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
            CenterToScreen();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyTitleBarTheme();
    }

    private void ApplyTitleBarTheme()
    {
        if (IsHandleCreated)
        {
            var enabled = DarkMode ? 1 : 0;
            DwmSetWindowAttribute(Handle, ImmersiveDarkModeAttribute, ref enabled, sizeof(int));
        }
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel || _closing)
        {
            return;
        }

        if (e.CloseReason == CloseReason.UserClosing && _closeToTray.Checked && !_exitRequested &&
            _trayIcon is { Visible: true })
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _closing = true;
        DisposeTray();
        _lifetime.Cancel();
        _client?.Dispose();
        try
        {
            _activeLog?.Dispose();
        }
        catch (Exception error) when (IsFileError(error))
        {
            MessageBox.Show(this, error.Message, "Could not finish saving the session", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        _lifetime.Dispose();
        _hints.Dispose();
    }

    private static bool IsDeviceError(Exception error) => error is InvalidOperationException
        or UnauthorizedAccessException or TimeoutException or COMException or NotSupportedException or NotImplementedException;

    private static string DescribeError(Exception error) => error is NotImplementedException or PlatformNotSupportedException
        ? "Bluetooth Low Energy is unavailable. Check your Bluetooth adapter and Windows settings."
        : error.Message;

    private static bool IsFileError(Exception error) => error is IOException
        or UnauthorizedAccessException or NotSupportedException or ArgumentException or System.Security.SecurityException;

    private static string DescribeStorageError(Exception error) => error is UnauthorizedAccessException or System.Security.SecurityException
        ? "Move PulseLog.exe to a folder you can write to, then try again."
        : error.Message;

    private TableLayoutPanel MakeCard(int columns, int rows)
    {
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(15, 8, 15, 7),
            Margin = new Padding(0, 0, 0, 12),
            ColumnCount = columns,
            RowCount = rows
        };
        _cards.Add(card);
        return card;
    }

    private Label MakeLabel(float points, FontStyle style, string text = "", LabelTone tone = LabelTone.Normal)
    {
        var label = new Label
        {
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty,
            Font = new Font("Segoe UI", points, style),
            Text = text,
            UseMnemonic = false,
            Tag = tone
        };
        _labels.Add(label);
        return label;
    }

    private static Button MakeButton(string text) => new ThemeButton
    {
        Text = text,
        Dock = DockStyle.Fill,
        Margin = Padding.Empty,
        FlatStyle = FlatStyle.Flat,
        UseVisualStyleBackColor = false,
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
    };

    private static RadioButton MakeTab(string text) => new()
    {
        Text = text,
        AccessibleRole = AccessibleRole.PageTab,
        Appearance = Appearance.Button,
        Dock = DockStyle.Fill,
        Margin = Padding.Empty,
        FlatStyle = FlatStyle.Flat,
        TextAlign = ContentAlignment.MiddleCenter,
        UseVisualStyleBackColor = false,
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
    };

    private static CheckBox MakeSettingToggle(string name) => new()
    {
        AccessibleName = name,
        Appearance = Appearance.Button,
        Anchor = AnchorStyles.Right,
        Size = new Size(SettingToggleWidth, 36),
        Margin = Padding.Empty,
        FlatStyle = FlatStyle.Flat,
        TextAlign = ContentAlignment.MiddleCenter,
        UseVisualStyleBackColor = false,
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
    };

    private sealed class ThemeButton : Button
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Enabled)
            {
                return;
            }

            // Keep the disabled caption legible on the app's own background.
            var bounds = Rectangle.Inflate(ClientRectangle, -2, -2);
            using var background = new SolidBrush(BackColor);
            e.Graphics.FillRectangle(background, bounds);
            TextRenderer.DrawText(e.Graphics, Text, Font, bounds, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    private sealed class ThemeComboBox : ComboBox
    {
        private const int PaintMessage = 0x000F;
        private ThemePalette? _darkPalette;

        internal void ApplyTheme(ThemePalette palette, bool darkMode)
        {
            _darkPalette = darkMode ? palette : null;
            BackColor = palette.Canvas;
            ForeColor = palette.Ink;
            Invalidate();
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg != PaintMessage || _darkPalette is not { } palette || !IsHandleCreated)
            {
                return;
            }

            var bounds = ClientRectangle;
            int arrowWidth = LogicalToDeviceUnits(20);
            int inset = LogicalToDeviceUnits(6);
            if (bounds.Width <= arrowWidth + inset || bounds.Height < 2)
            {
                return;
            }

            // Style the closed field while the native control handles input and the popup.
            using var graphics = Graphics.FromHwnd(Handle);
            using var background = new SolidBrush(palette.Canvas);
            graphics.FillRectangle(background, bounds);
            using var border = new Pen(Focused ? palette.Accent : palette.Border);
            graphics.DrawRectangle(border, bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);

            int centerX = bounds.Right - arrowWidth / 2;
            int centerY = bounds.Top + bounds.Height / 2;
            int arrowSize = LogicalToDeviceUnits(3);
            var ink = Enabled ? palette.Ink : palette.Muted;
            using var arrow = new Pen(ink, Math.Max(1, LogicalToDeviceUnits(1)));
            graphics.DrawLines(arrow, new Point[]
            {
                new Point(centerX - arrowSize, centerY - 1),
                new Point(centerX, centerY + arrowSize - 1),
                new Point(centerX + arrowSize, centerY - 1)
            });

            var textBounds = new Rectangle(bounds.Left + inset, bounds.Top, bounds.Width - arrowWidth - inset, bounds.Height);
            TextRenderer.DrawText(graphics, GetItemText(SelectedItem), Font, textBounds, ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    private sealed record ThemePalette(
        Color Canvas, Color Surface, Color Ink, Color Muted, Color Accent, Color Tint, Color Border,
        Color Error, Color PrimaryInk, Color PrimaryHover, Color PrimaryPressed);

    private enum LabelTone
    {
        Normal,
        Muted,
        Error
    }
}
