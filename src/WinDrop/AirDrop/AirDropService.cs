using System.Diagnostics;
using WinDrop.Ble;

namespace WinDrop.AirDrop;

/// <summary>An incoming AirDrop request waiting for an answer.</summary>
public sealed class IncomingRequest(string sender, string model, IReadOnlyList<OfferedFile> files, Action<bool> answer)
{
    public string Sender { get; } = sender;
    public string Model { get; } = model;
    public IReadOnlyList<OfferedFile> Files { get; } = files;
    private int _answered;

    public void Accept() { if (Interlocked.Exchange(ref _answered, 1) == 0) answer(true); }
    public void Decline() { if (Interlocked.Exchange(ref _answered, 1) == 0) answer(false); }
}

public enum SendResult { Delivered, Declined, NotFound, Failed }

public enum AirDropState { Off, Starting, Visible, Error }

/// <summary>
/// Drives owly in WSL: holds the radio (<c>owly radio up</c>), receives
/// (<c>owly receive</c>), browses and sends, and runs the BlueZ shim so
/// owly's BLE advert goes out over Windows' Bluetooth.
/// </summary>
public sealed class AirDropService(Wsl.Wsl wsl, Settings settings) : IDisposable
{
    private Process? _radio, _receiver, _shim;
    private readonly BleAdvertiser _ble = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AirDropState State { get; private set; } = AirDropState.Off;
    public string? LastError { get; private set; }

    public event Action<AirDropState>? StateChanged;
    public event Action<IncomingRequest>? RequestReceived;
    public event Action<string>? FileReceived;   // Windows path
    public event Action<string>? UrlReceived;
    public event Action<string>? Log;

    private string DisplayName => string.IsNullOrWhiteSpace(settings.DisplayName) ? Environment.MachineName : settings.DisplayName;

    public async Task StartAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (State is AirDropState.Visible or AirDropState.Starting) return;
            SetState(AirDropState.Starting);
            _ble.Log += m => Log?.Invoke(m);
            StartShim();

            var (code, output, err) = await wsl.RunAsync(["/usr/local/sbin/windrop-load-modules"], root: true);
            Log?.Invoke(output.Trim() + err.Trim());
            if (code != 0) throw new InvalidOperationException("Geen ondersteunde wifistick in WSL. Zit het stickje erin en is het gekoppeld?");

            _radio = wsl.Start(["owly", "radio", "up"]);
            Pump(_radio.StandardError, l => Log?.Invoke("radio: " + l));
            Pump(_radio.StandardOutput, l => Log?.Invoke("radio: " + l));
            if (!await WaitForAwdlAsync(TimeSpan.FromSeconds(30)))
                throw new InvalidOperationException("De AirDrop-radio kwam niet op. Bekijk 'owly doctor' in WSL.");

            StartReceiver();
            SetState(AirDropState.Visible);
        }
        catch (Exception e)
        {
            LastError = e.Message;
            Log?.Invoke("fout: " + e.Message);
            StopProcesses();
            SetState(AirDropState.Error);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            StopProcesses();
            SetState(AirDropState.Off);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Looks for AirDrop receivers for a few seconds.</summary>
    public async Task<IReadOnlyList<Peer>> BrowseAsync(int seconds = 8, CancellationToken ct = default)
    {
        var (_, output, err) = await wsl.RunAsync(["owly", "browse", "--seconds", seconds.ToString(), "--name", DisplayName], ct: ct);
        Log?.Invoke("browse: " + err.Trim());
        return output.Split('\n').Select(l => OwlyOutput.ParsePeer(l.TrimEnd('\r'))).OfType<Peer>()
            .DistinctBy(p => p.Name).ToList();
    }

    /// <summary>Sends files to the receiver named <paramref name="to"/>; resolves when it accepts, declines or fails.</summary>
    public async Task<(SendResult Result, string Detail)> SendAsync(string to, IReadOnlyList<string> windowsPaths, CancellationToken ct = default)
    {
        var args = new List<string> { "owly", "send", "--to", to, "--name", DisplayName };
        args.AddRange(windowsPaths.Select(Wsl.Wsl.ToWslPath));
        var (code, output, err) = await wsl.RunAsync(args, ct: ct);
        Log?.Invoke("send: " + err.Trim());
        var last = output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? "";
        if (last == "delivered") return (SendResult.Delivered, "");
        if (last == "declined") return (SendResult.Declined, "");
        if (last.StartsWith("failed:", StringComparison.Ordinal)) return (SendResult.Failed, last[7..].Trim());
        if (err.Contains("looking for", StringComparison.Ordinal) && code != 0) return (SendResult.NotFound, err.Trim());
        return (SendResult.Failed, err.Trim());
    }

    private void StartReceiver()
    {
        Directory.CreateDirectory(settings.ReceiveDirectory);
        _receiver = wsl.Start(["owly", "receive", "--dir", Wsl.Wsl.ToWslPath(settings.ReceiveDirectory), "--name", DisplayName]);
        var stdin = _receiver.StandardInput;

        // Requests arrive on stderr as a header line plus one line per file.
        (string Sender, string Model)? pending = null;
        var files = new List<OfferedFile>();
        void Flush()
        {
            if (pending is not { } p) return;
            var request = new IncomingRequest(p.Sender, p.Model, files.ToList(), ok =>
            {
                try { stdin.WriteLine(ok ? "y" : "n"); stdin.Flush(); } catch (IOException) { }
            });
            pending = null;
            files.Clear();
            RequestReceived?.Invoke(request);
        }

        Pump(_receiver.StandardError, line =>
        {
            Log?.Invoke("receive: " + line);
            if (OwlyOutput.ParseRequest(line) is { } header) { pending = header; files.Clear(); return; }
            if (pending is not null && OwlyOutput.ParseFile(line) is { } file) { files.Add(file); return; }
            // owly prints "accept? [y/N] " without a newline, so the next line
            // or a short pause ends the file list.
            Flush();
        }, onIdle: Flush);

        Pump(_receiver.StandardOutput, line =>
        {
            if (line.StartsWith('"')) { UrlReceived?.Invoke(OwlyOutput.Unquote(line)); return; }
            var path = Wsl.Wsl.ToWindowsPath(line.Trim());
            if (path is not null) FileReceived?.Invoke(path);
        });

        _receiver.EnableRaisingEvents = true;
        _receiver.Exited += (_, _) =>
        {
            if (State == AirDropState.Visible)
            {
                LastError = "owly receive is gestopt.";
                SetState(AirDropState.Error);
            }
        };
    }

    private void StartShim()
    {
        _shim = wsl.Start(["python3", "/opt/windrop/bluez-shim.py"], root: true);
        Pump(_shim.StandardError, l => Log?.Invoke("shim: " + l));
        Pump(_shim.StandardOutput, line =>
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            try
            {
                if (parts is ["ADV", var company, var hex])
                    _ble.Start(Convert.ToUInt16(company, 16), Convert.FromHexString(hex));
                else if (parts is ["STOP"])
                    _ble.Stop();
            }
            catch (Exception e)
            {
                Log?.Invoke("BLE: " + e.Message);
            }
        });
    }

    private async Task<bool> WaitForAwdlAsync(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (_radio is { HasExited: true }) return false;
            var (code, _, _) = await wsl.RunAsync(["ip", "link", "show", "awdl0"]);
            if (code == 0) return true;
            await Task.Delay(1000);
        }
        return false;
    }

    /// <summary>Reads lines on a background thread; <paramref name="onIdle"/> runs after 400 ms without output.</summary>
    private static void Pump(StreamReader reader, Action<string> onLine, Action? onIdle = null)
    {
        _ = Task.Run(async () =>
        {
            var buffer = new char[1];
            var line = new System.Text.StringBuilder();
            while (true)
            {
                var read = reader.ReadAsync(buffer, 0, 1);
                if (onIdle is not null && line.Length > 0 && await Task.WhenAny(read, Task.Delay(400)) != read)
                {
                    onLine(line.ToString());
                    line.Clear();
                    onIdle();
                }
                if (await read == 0) break;
                if (buffer[0] == '\n')
                {
                    onLine(line.ToString().TrimEnd('\r'));
                    line.Clear();
                }
                else line.Append(buffer[0]);
            }
            if (line.Length > 0) onLine(line.ToString());
        });
    }

    private void StopProcesses()
    {
        // Killing wsl.exe doesn't reliably stop the Linux side; owly gives
        // the radio back on Ctrl-C, so send SIGINT there first.
        if (_radio is not null || _receiver is not null)
        {
            try { wsl.RunAsync(["pkill", "-INT", "-x", "owly"]).Wait(3000); } catch { }
        }
        foreach (var p in new[] { _receiver, _radio, _shim })
        {
            if (p is null) continue;
            try { p.StandardInput.Close(); } catch { }
            try { if (!p.WaitForExit(1500)) p.Kill(entireProcessTree: true); } catch { }
            p.Dispose();
        }
        _receiver = _radio = _shim = null;
        _ble.Stop();
    }

    private void SetState(AirDropState s)
    {
        State = s;
        StateChanged?.Invoke(s);
    }

    public void Dispose()
    {
        StopProcesses();
        _ble.Dispose();
    }
}
