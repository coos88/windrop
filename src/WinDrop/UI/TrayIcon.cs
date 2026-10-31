using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using WinDrop.AirDrop;

namespace WinDrop.UI;

/// <summary>The notification-area icon and its menu.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon = new();
    private readonly ToolStripMenuItem _status = new() { Enabled = false };
    private readonly ToolStripMenuItem _visible = new("Zichtbaar voor AirDrop") { CheckOnClick = true };
    private readonly ToolStripMenuItem _contextMenu = new("‘Delen via AirDrop’ in Verkenner") { CheckOnClick = true };
    private readonly ToolStripMenuItem _autostart = new("Starten met Windows") { CheckOnClick = true };
    private Icon? _current;
    private string? _lastReceived;

    public event Action<bool>? VisibleToggled;
    public event Action? SendFilesClicked;
    public event Action? OpenReceivedClicked;
    public event Action? SetupClicked;
    public event Action? LogClicked;
    public event Action? ExitClicked;
    public event Action<bool>? ContextMenuToggled;
    public event Action<bool>? AutostartToggled;

    public TrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(_status);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_visible);
        menu.Items.Add("Bestanden versturen…", null, (_, _) => SendFilesClicked?.Invoke());
        menu.Items.Add("Ontvangen bestanden openen", null, (_, _) => OpenReceivedClicked?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_contextMenu);
        menu.Items.Add(_autostart);
        menu.Items.Add("Installatie en wifistick…", null, (_, _) => SetupClicked?.Invoke());
        menu.Items.Add("Logboek openen", null, (_, _) => LogClicked?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Afsluiten", null, (_, _) => ExitClicked?.Invoke());
        _visible.CheckedChanged += (_, _) => { if (!_updating) VisibleToggled?.Invoke(_visible.Checked); };
        _contextMenu.CheckedChanged += (_, _) => ContextMenuToggled?.Invoke(_contextMenu.Checked);
        _autostart.CheckedChanged += (_, _) => AutostartToggled?.Invoke(_autostart.Checked);

        _icon.ContextMenuStrip = menu;
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) SendFilesClicked?.Invoke(); };
        _icon.BalloonTipClicked += (_, _) =>
        {
            if (_lastReceived is not null && File.Exists(_lastReceived))
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{_lastReceived}\"");
        };
        SystemEvents.UserPreferenceChanged += OnThemeChanged;
        Update(AirDropState.Off, null);
        _icon.Visible = true;
    }

    public void SetToggles(bool contextMenu, bool autostart)
    {
        _contextMenu.Checked = contextMenu;
        _autostart.Checked = autostart;
    }

    private AirDropState _state;
    private string? _error;

    public void Update(AirDropState state, string? error)
    {
        _state = state;
        _error = error;
        string text = state switch
        {
            AirDropState.Visible => "AirDrop: zichtbaar voor iedereen",
            AirDropState.Starting => "AirDrop: bezig met starten…",
            AirDropState.Error => "AirDrop: " + (error ?? "fout"),
            _ => "AirDrop: uit",
        };
        _status.Text = text;
        _icon.Text = text.Length > 127 ? text[..127] : text;
        _updating = true;
        _visible.Checked = state is AirDropState.Visible or AirDropState.Starting;
        _updating = false;
        SetIcon(state == AirDropState.Visible);
    }

    private bool _updating;

    public void NotifyReceived(string path)
    {
        _lastReceived = path;
        _icon.ShowBalloonTip(5000, "Ontvangen via AirDrop", Path.GetFileName(path), ToolTipIcon.None);
    }

    public void NotifyText(string title, string text) => _icon.ShowBalloonTip(5000, title, text, ToolTipIcon.None);

    private void OnThemeChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General) Update(_state, _error);
    }

    /// <summary>Segoe Fluent's "Share" glyph, dimmed while AirDrop is off.</summary>
    private void SetIcon(bool on)
    {
        int size = SystemInformation.SmallIconSize.Width;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            bool lightTaskbar = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "SystemUsesLightTheme", 0) is int v && v == 1;
            var color = on ? Color.FromArgb(10, 132, 255) : lightTaskbar ? Color.FromArgb(140, 0, 0, 0) : Color.FromArgb(160, 255, 255, 255);
            using var font = new Font("Segoe Fluent Icons", size * 0.75f, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(color);
            var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("", font, brush, new RectangleF(0, 0, size, size), fmt);
        }
        var handle = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(handle).Clone();
        DestroyIcon(handle);
        _icon.Icon = icon;
        _current?.Dispose();
        _current = icon;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnThemeChanged;
        _icon.Visible = false;
        _icon.Dispose();
        _current?.Dispose();
    }
}
