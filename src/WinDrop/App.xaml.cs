using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using WinDrop.AirDrop;
using WinDrop.Shell;
using WinDrop.UI;
using WinDrop.Wsl;

namespace WinDrop;

public partial class App : Application
{
    private SingleInstance? _instance;
    private Settings _settings = new();
    private Wsl.Wsl _wsl = null!;
    private AirDropService _airdrop = null!;
    private TrayIcon _tray = null!;
    private Process? _attach;
    private ShareWindow? _share;
    private readonly List<string> _pendingShare = new();
    private DispatcherTimer? _shareDebounce;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;

        if (args is ["ble-test", ..])
        {
            await BleTestAsync();
            Shutdown();
            return;
        }
        if (args is ["render", var outDir, ..])
        {
            await RenderAsync(outDir);
            Shutdown();
            return;
        }

        _instance = new SingleInstance();
        if (!_instance.IsFirst)
        {
            SingleInstance.Send(args);
            Shutdown();
            return;
        }

        Theme.Apply(Resources);
        _settings = Settings.Load();
        _wsl = new Wsl.Wsl(_settings.Distro);
        _airdrop = new AirDropService(_wsl, _settings);
        _airdrop.Log += Log.Write;
        _airdrop.StateChanged += s => Dispatcher.BeginInvoke(() => _tray.Update(s, _airdrop.LastError));
        _airdrop.RequestReceived += r => Dispatcher.BeginInvoke(() => new RequestWindow(r).Show());
        _airdrop.FileReceived += p => Dispatcher.BeginInvoke(() => _tray.NotifyReceived(p));
        _airdrop.UrlReceived += u => Dispatcher.BeginInvoke(() => _tray.NotifyText("Link ontvangen via AirDrop", u));

        _tray = new TrayIcon();
        _tray.SetToggles(ShellIntegration.ContextMenuInstalled, ShellIntegration.StartWithWindows);
        _tray.VisibleToggled += async on =>
        {
            _settings.VisibleOnStart = on;
            _settings.Save();
            if (on) await GoVisibleAsync(); else await _airdrop.StopAsync();
        };
        _tray.SendFilesClicked += PickAndShare;
        _tray.OpenReceivedClicked += () => Process.Start("explorer.exe", $"\"{_settings.ReceiveDirectory}\"");
        _tray.LogClicked += () => { if (File.Exists(Log.FilePath)) Process.Start("notepad.exe", $"\"{Log.FilePath}\""); };
        _tray.SetupClicked += ShowSetup;
        _tray.ContextMenuToggled += on => { if (on) ShellIntegration.InstallContextMenu(); else ShellIntegration.RemoveContextMenu(); };
        _tray.AutostartToggled += on => ShellIntegration.StartWithWindows = on;
        _tray.ExitClicked += () => Shutdown();

        _instance.ArgumentsReceived += a => Dispatcher.BeginInvoke(() => HandleArgs(a));
        _instance.Listen();
        HandleArgs(args);

        if (_settings.VisibleOnStart) await GoVisibleAsync();
    }

    private void HandleArgs(string[] args)
    {
        if (args is ["share", .. var files] && files.Length > 0)
        {
            // Explorer launches us once per selected file; gather them for a moment.
            _pendingShare.AddRange(files.Where(File.Exists));
            _shareDebounce ??= new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Normal, (_, _) =>
            {
                _shareDebounce!.Stop();
                var batch = _pendingShare.ToList();
                _pendingShare.Clear();
                if (batch.Count > 0) Share(batch);
            }, Dispatcher);
            _shareDebounce.Stop();
            _shareDebounce.Start();
        }
        else if (args is ["setup", ..])
        {
            ShowSetup();
        }
    }

    /// <summary>Attaches the stick to WSL (if present) and starts AirDrop.</summary>
    private async Task GoVisibleAsync()
    {
        if (UsbStick.UsbipdInstalled && _attach is null or { HasExited: true })
        {
            var stick = await UsbStick.FindAsync();
            if (stick is null)
            {
                _tray.Update(AirDropState.Error, "geen wifistick gevonden");
                return;
            }
            if (!stick.Shared)
            {
                _tray.Update(AirDropState.Error, "stick nog niet gedeeld, kies ‘Installatie en wifistick…’");
                return;
            }
            if (!stick.Attached)
            {
                _attach = UsbStick.AutoAttach(stick);
                await Task.Delay(3000); // let the device enumerate in WSL
            }
        }
        await _airdrop.StartAsync();
    }

    private void PickAndShare()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Versturen via AirDrop" };
        if (dialog.ShowDialog() == true) Share(dialog.FileNames);
    }

    private void Share(IEnumerable<string> files)
    {
        if (_share is { IsLoaded: true })
        {
            _share.AddFiles(files);
            _share.Activate();
            return;
        }
        _share = new ShareWindow(_airdrop, files);
        _share.Closed += (_, _) => _share = null;
        _share.Show();
        _share.Activate();
    }

    private void ShowSetup()
    {
        var setup = new Setup(_wsl, Log.Write);
        var window = new SetupWindow(async (log, ct) =>
        {
            var runner = new Setup(_wsl, m => { Log.Write(m); log(m); });
            return await runner.RunAsync(ct);
        });
        window.Closed += async (_, _) =>
        {
            if (window.Succeeded && _settings.VisibleOnStart) await GoVisibleAsync();
        };
        window.Show();
    }

    /// <summary>Checks that Windows will send an Apple BLE advert (owly's AirDrop wake-up) for 10 s.</summary>
    private static async Task BleTestAsync()
    {
        var lines = new List<string>();
        using var ble = new Ble.BleAdvertiser();
        ble.Log += lines.Add;
        try
        {
            // owly's Advert::anonymous layout is opaque to us; any Apple-company payload proves the path.
            ble.Start(Ble.BleAdvertiser.AppleCompanyId, Convert.FromHexString("0512000000000000000001000000000000"));
            await Task.Delay(10_000);
            lines.Add($"status na 10 s: {ble.Status}");
        }
        catch (Exception ex)
        {
            lines.Add("fout: " + ex);
        }
        File.WriteAllLines(Path.Combine(Path.GetTempPath(), "windrop-ble-test.txt"), lines);
    }

    /// <summary>Renders the request and share windows to PNGs, in light and dark, for checking the design.</summary>
    private async Task RenderAsync(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var request = new IncomingRequest("iPhone van Coos", "iPhone",
            [new("IMG_0042.HEIC", "public.heic", 2_400_000), new("IMG_0043.HEIC", "public.heic", 2_100_000)], _ => { });
        foreach (var dark in new[] { false, true })
        {
            ApplyRenderTheme(dark);
            var suffix = dark ? "dark" : "light";
            var req = new RequestWindow(request) { Left = -4000, Top = 0 };
            req.Show();
            await Task.Delay(400);
            SaveVisual(req, Path.Combine(outDir, $"request-{suffix}.png"));
            req.Close();

            var share = new ShareWindow(new AirDropService(new Wsl.Wsl("ubuntu"), new Settings()), [@"C:\foto.jpg", @"C:\verslag.pdf"]) { Demo = true, Left = -4000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual };
            share.Show();
            share.ShowDemo("iPhone van Coos", "MacBook Pro van Coos", "iPad");
            await Task.Delay(400);
            SaveVisual(share, Path.Combine(outDir, $"share-{suffix}.png"));
            share.Close();
        }
    }

    private void ApplyRenderTheme(bool dark)
    {
        void Set(string key, string color) => Resources[key] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
        Set("CardBrush", dark ? "#2C2C2E" : "#F7F7F8");
        Set("CardBorderBrush", dark ? "#33FFFFFF" : "#1F000000");
        Set("TextBrush", dark ? "#F5F5F7" : "#1D1D1F");
        Set("SubtleTextBrush", dark ? "#98989D" : "#6E6E73");
        Set("ButtonBrush", dark ? "#48484A" : "#E5E5EA");
    }

    private static void SaveVisual(Window window, string path)
    {
        // The card's margin (room for its shadow) is part of the content's offset, so size to the window.
        var content = (FrameworkElement)window.Content;
        double w = window.ActualWidth, h = window.ActualHeight;
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(w * 2), (int)(h * 2), 192, 192, System.Windows.Media.PixelFormats.Pbgra32);
        var bg = new System.Windows.Media.DrawingVisual();
        using (var dc = bg.RenderOpen())
            dc.DrawRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(128, 128, 140)), null, new Rect(0, 0, w, h));
        bmp.Render(bg);
        bmp.Render(content);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _airdrop?.Dispose();
        try { _attach?.Kill(); } catch { }
        _tray?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
