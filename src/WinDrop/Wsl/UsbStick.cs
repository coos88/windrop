using System.Diagnostics;
using System.Text.RegularExpressions;

namespace WinDrop.Wsl;

/// <summary>A USB Wi-Fi adapter usbipd can hand to WSL.</summary>
public sealed record UsbDevice(string BusId, string VidPid, string Description, bool Shared, bool Attached);

/// <summary>
/// Finds a supported USB Wi-Fi stick and attaches it to WSL with usbipd-win.
/// Binding (sharing) needs admin once per device; attaching doesn't.
/// </summary>
public static partial class UsbStick
{
    public const string UsbipdPath = @"C:\Program Files\usbipd-win\usbipd.exe";

    /// <summary>VID:PIDs the omdrop-owl hardware table lists as working, preferred first.</summary>
    public static readonly IReadOnlyDictionary<string, string> Known = new Dictionary<string, string>
    {
        ["0e8d:7612"] = "MediaTek MT7612U",
        ["35bc:0108"] = "Realtek RTL8852BU",
    };

    [GeneratedRegex(@"^(?<bus>\d+-\d+(?:\.\d+)*)\s+(?<id>[0-9a-fA-F]{4}:[0-9a-fA-F]{4})\s+(?<desc>.+?)\s{2,}(?<state>Not shared|Shared|Attached)\s*$")]
    private static partial Regex ListLine();

    public static bool UsbipdInstalled => File.Exists(UsbipdPath);

    public static IReadOnlyList<UsbDevice> ParseList(string output)
    {
        var list = new List<UsbDevice>();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("Persisted", StringComparison.Ordinal)) break;
            var m = ListLine().Match(line);
            if (!m.Success) continue;
            var state = m.Groups["state"].Value;
            list.Add(new UsbDevice(m.Groups["bus"].Value, m.Groups["id"].Value.ToLowerInvariant(),
                m.Groups["desc"].Value, state != "Not shared", state == "Attached"));
        }
        return list;
    }

    public static async Task<IReadOnlyList<UsbDevice>> ListAsync()
    {
        var (_, output) = await RunAsync("list");
        return ParseList(output);
    }

    /// <summary>The first connected stick with a known chip.</summary>
    public static async Task<UsbDevice?> FindAsync() =>
        (await ListAsync()).Where(d => Known.ContainsKey(d.VidPid))
            .OrderBy(d => Known.Keys.ToList().IndexOf(d.VidPid))
            .FirstOrDefault();

    /// <summary>Shares the device with WSL; shows a UAC prompt.</summary>
    public static async Task<bool> BindAsync(UsbDevice device)
    {
        var psi = new ProcessStartInfo(UsbipdPath, $"bind --busid {device.BusId}")
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        try
        {
            using var p = Process.Start(psi)!;
            await p.WaitForExitAsync();
            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // UAC declined
        }
    }

    /// <summary>
    /// Attaches the device to WSL and keeps re-attaching it after replugs
    /// for as long as the returned process runs.
    /// </summary>
    public static Process AutoAttach(UsbDevice device)
    {
        // All WSL 2 distros share one kernel, so no distro is named.
        var psi = new ProcessStartInfo(UsbipdPath, $"attach --wsl --busid {device.BusId} --auto-attach")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        return Process.Start(psi)!;
    }

    private static async Task<(int, string)> RunAsync(string args)
    {
        var psi = new ProcessStartInfo(UsbipdPath, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        using var p = Process.Start(psi)!;
        var output = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (p.ExitCode, output);
    }
}
