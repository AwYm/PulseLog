using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace HeartRateLogger;

internal sealed class LoggerForm : Form
{
    private static readonly Color Canvas = Color.FromArgb(245, 246, 242);
    private static readonly Color Paper = Color.White;
    private static readonly Color Ink = Color.FromArgb(16, 45, 40);
    private static readonly Color Subtle = Color.FromArgb(76, 102, 93);
    private static readonly Color Green = Color.FromArgb(20, 99, 78);
    private static readonly Color Tint = Color.FromArgb(224, 240, 230);
    private static readonly Color Border = Color.FromArgb(210, 223, 215);
    private static readonly Color ErrorInk = Color.FromArgb(153, 48, 42);

    private readonly CancellationTokenSource _lifetime = new();
    private readonly ToolTip _hints = new();
    private readonly ComboBox _devices = new();
    private readonly ComboBox _chartWindow = new();
    private readonly Label _deviceStatus = MakeLabel(9.5f, FontStyle.Regular, Subtle);
    private readonly Label _fileStatus = MakeLabel(9.5f, FontStyle.Regular, Subtle);
    private readonly Button _scan = MakeButton("Scan", false);
    private readonly Button _connect = MakeButton("Connect", true);
    private readonly Button _recording = MakeButton("Pause recording", false);
    private readonly Button _openLogs = MakeButton("Open folder", false);
    private readonly Button _export = MakeButton("Export CSV", true);
    private readonly HeartRateHero _hero = new();
    private readonly HeartRateChart _chart = new();
    private readonly Label _minimum = MakeLabel(17, FontStyle.Bold, Ink);
    private readonly Label _maximum = MakeLabel(17, FontStyle.Bold, Ink);
    private readonly Label _average = MakeLabel(17, FontStyle.Bold, Ink);

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

    internal LoggerForm()
    {
        Text = "Pulse Log";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1000, 743);
        MinimumSize = new Size(740, 680);
        BackColor = Canvas;
        ForeColor = Ink;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(28, 18, 28, 14)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 99));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 144));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        layout.Controls.Add(BuildHeader(), 0, 0);
        layout.Controls.Add(BuildDevices(), 0, 1);
        layout.Controls.Add(BuildSummary(), 0, 2);
        layout.Controls.Add(BuildChart(), 0, 3);
        layout.Controls.Add(BuildFooter(), 0, 4);
        Controls.Add(layout);

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
    }

    private static Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 7),
            ColumnCount = 1,
            RowCount = 2
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        header.Controls.Add(MakeLabel(30, FontStyle.Bold, Ink, "Pulse Log"), 0, 0);
        header.Controls.Add(MakeLabel(10.5f, FontStyle.Regular, Subtle, "Bluetooth heart rate logger"), 0, 1);
        return header;
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

        var heading = MakeLabel(11, FontStyle.Bold, Ink, "Heart rate monitor");
        card.Controls.Add(heading, 0, 0);
        card.SetColumnSpan(heading, 4);

        _devices.AccessibleName = "Bluetooth heart rate monitor";
        _devices.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _devices.DropDownStyle = ComboBoxStyle.DropDownList;
        _devices.FlatStyle = FlatStyle.Flat;
        _devices.BackColor = Canvas;
        _devices.ForeColor = Ink;
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
        var statsHeading = MakeLabel(11, FontStyle.Bold, Ink, "Session stats · BPM");
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
                Font = new Font("Segoe UI", 9.5f),
                Size = new Size(option.Width, 28),
                Margin = new Padding(0, 0, 4, 0)
            };
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.CheckedBackColor = Tint;
            button.FlatAppearance.MouseOverBackColor = Canvas;
            button.FlatAppearance.MouseDownBackColor = Tint;
            button.CheckedChanged += (_, _) =>
            {
                button.ForeColor = button.Checked ? Green : Ink;
                button.FlatAppearance.BorderColor = button.Checked ? Green : Border;
                if (button.Checked)
                {
                    _chart.SetMode(option.Mode);
                }
            };
            toolbar.Controls.Add(button);
            if (option.Mode == HeartRateChartMode.Range)
            {
                button.Checked = true;
            }
        }

        _chartWindow.AccessibleName = "Chart time range";
        _chartWindow.DropDownStyle = ComboBoxStyle.DropDownList;
        _chartWindow.FlatStyle = FlatStyle.Flat;
        _chartWindow.BackColor = Canvas;
        _chartWindow.ForeColor = Ink;
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
            (Label: "Last hour", Minutes: 60)
        };
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

    private static TableLayoutPanel BuildStat(string name, Label value)
    {
        var metric = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(3, 0, 3, 0),
            Padding = new Padding(3, 7, 3, 7),
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Canvas
        };
        metric.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        metric.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
        metric.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var caption = MakeLabel(9.5f, FontStyle.Regular, Subtle, name);
        caption.TextAlign = ContentAlignment.MiddleCenter;
        value.AccessibleName = $"Session {name.ToLowerInvariant()} heart rate in BPM";
        value.TextAlign = ContentAlignment.MiddleCenter;
        metric.Controls.Add(caption, 0, 0);
        metric.Controls.Add(value, 0, 1);
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
        SetPrimaryButtonEnabled(_connect, !_scanning && !_connecting && (active || _devices.SelectedItem is not null));
        _connect.Text = active ? "Disconnect" : _connecting ? "Connecting…" : "Connect";
        _recording.Enabled = active;
        _recording.Text = _recordingPaused ? "Resume recording" : "Pause recording";
        SetPrimaryButtonEnabled(_export, _lastLog is not null && _sampleCount > 0 && !_exporting);
    }

    private static void SetPrimaryButtonEnabled(Button button, bool enabled)
    {
        button.Enabled = enabled;
        button.BackColor = enabled ? Green : Tint;
        button.ForeColor = enabled ? Paper : Subtle;
        button.FlatAppearance.BorderColor = enabled ? Green : Border;
    }

    private void SetDeviceStatus(string message, bool error = false) => SetStatus(_deviceStatus, message, error);
    private void SetFileStatus(string message, bool error = false) => SetStatus(_fileStatus, message, error);

    private void SetStatus(Label label, string message, bool error)
    {
        label.ForeColor = error ? ErrorInk : Subtle;
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

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel || _closing)
        {
            return;
        }

        _closing = true;
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

    private static TableLayoutPanel MakeCard(int columns, int rows) => new()
    {
        BackColor = Paper,
        Dock = DockStyle.Fill,
        Padding = new Padding(15, 8, 15, 7),
        Margin = new Padding(0, 0, 0, 12),
        ColumnCount = columns,
        RowCount = rows
    };

    private static Label MakeLabel(float points, FontStyle style, Color color, string text = "") => new()
    {
        AutoEllipsis = true,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = Padding.Empty,
        ForeColor = color,
        Font = new Font("Segoe UI", points, style),
        Text = text,
        UseMnemonic = false
    };

    private static Button MakeButton(string text, bool primary)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            BackColor = primary ? Green : Paper,
            ForeColor = primary ? Paper : Ink,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };
        button.FlatAppearance.BorderColor = primary ? Green : Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(23, 115, 89) : Canvas;
        button.FlatAppearance.MouseDownBackColor = primary ? Ink : Tint;
        return button;
    }

}
