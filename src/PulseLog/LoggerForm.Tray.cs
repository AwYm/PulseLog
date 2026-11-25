namespace HeartRateLogger;

internal sealed partial class LoggerForm
{
    private NotifyIcon? _trayIcon;
    private ContextMenuStrip? _trayMenu;
    private ToolStripMenuItem? _trayRecording;
    private FormWindowState _trayRestoreWindowState = FormWindowState.Normal;
    private bool _restoringFromTray;
    private bool _exitRequested;

    private void InitializeTray()
    {
        if (_trayIcon is not null)
        {
            return;
        }

        var open = new ToolStripMenuItem("Open Pulse Log", null, (_, _) => RestoreFromTray());
        _trayRecording = new ToolStripMenuItem("Pause recording", null, (_, _) => ToggleRecording());
        var exit = new ToolStripMenuItem("Exit Pulse Log", null, (_, _) => ExitFromTray());
        _trayMenu = new ContextMenuStrip { ShowImageMargin = false };
        _trayMenu.Items.AddRange([open, _trayRecording, new ToolStripSeparator(), exit]);
        _trayMenu.Opening += (_, _) => RefreshTray();

        _trayIcon = new NotifyIcon
        {
            Icon = Icon ?? SystemIcons.Application,
            ContextMenuStrip = _trayMenu
        };
        _trayIcon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                RestoreFromTray();
            }
        };

        Disposed += (_, _) => DisposeTray();
        ApplyTrayTheme();
        RefreshTray();
        _trayIcon.Visible = true;
    }

    private void ApplyTrayTheme()
    {
        if (_trayMenu is not { } menu)
        {
            return;
        }

        var palette = Palette;
        menu.BackColor = palette.Surface;
        menu.ForeColor = palette.Ink;
        menu.Renderer = new TrayMenuRenderer(palette);
        menu.Invalidate();
    }

    private void RefreshTray()
    {
        if (_trayIcon is not { } icon || _trayRecording is not { } recording)
        {
            return;
        }

        recording.Enabled = _activeLog is not null && !_closing;
        recording.Text = _recordingPaused ? "Resume recording" : "Pause recording";
        icon.Text = recording.Enabled
            ? _recordingPaused ? "Pulse Log · Recording paused" : "Pulse Log · Recording"
            : "Pulse Log";
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_trayIcon is null || _closing || _restoringFromTray)
        {
            return;
        }

        if (WindowState == FormWindowState.Minimized)
        {
            Hide();
            return;
        }

        _trayRestoreWindowState = WindowState;
    }

    private void RestoreFromTray()
    {
        if (_closing || IsDisposed || Disposing)
        {
            return;
        }

        if (!Visible || WindowState == FormWindowState.Minimized)
        {
            _restoringFromTray = true;
            try
            {
                WindowState = _trayRestoreWindowState;
                Show();
                if (WindowState != _trayRestoreWindowState)
                {
                    WindowState = _trayRestoreWindowState;
                }
            }
            finally
            {
                _restoringFromTray = false;
            }
        }

        Activate();
    }

    private void ExitFromTray()
    {
        _exitRequested = true;
        try
        {
            Close();
        }
        finally
        {
            _exitRequested = false;
        }
    }

    private void DisposeTray()
    {
        var icon = _trayIcon;
        var menu = _trayMenu;
        _trayIcon = null;
        _trayMenu = null;
        _trayRecording = null;
        if (icon is not null)
        {
            icon.Visible = false;
            icon.ContextMenuStrip = null;
            icon.Dispose();
        }
        menu?.Dispose();
    }

    private sealed class TrayMenuRenderer(ThemePalette palette) : ToolStripProfessionalRenderer(new TrayMenuColors(palette))
    {
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            Color ink = e.Item.Enabled ? e.Item.Selected ? palette.Accent : palette.Ink : palette.Muted;
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle, ink, e.TextFormat);
        }
    }

    private sealed class TrayMenuColors(ThemePalette palette) : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => palette.Surface;
        public override Color MenuBorder => palette.Border;
        public override Color MenuItemBorder => palette.Border;
        public override Color MenuItemSelected => palette.Tint;
        public override Color SeparatorDark => palette.Border;
        public override Color SeparatorLight => palette.Surface;
    }
}
