using WinDrop.Wsl;

namespace WinDrop;

/// <summary>
/// One-time installation: builds owly and the stick's driver in WSL,
/// installs them, and shares the stick with WSL (one UAC prompt).
/// </summary>
public sealed class Setup(Wsl.Wsl wsl, Action<string> log)
{
    private static string ScriptDir => Path.Combine(AppContext.BaseDirectory, "wsl");

    public async Task<bool> RunAsync(CancellationToken ct = default)
    {
        if (!UsbStick.UsbipdInstalled)
        {
            log("usbipd-win ontbreekt. Installeer het met: winget install dorssel.usbipd-win");
            return false;
        }

        var (code, user, _) = await wsl.RunAsync(["whoami"], ct: ct);
        if (code != 0) { log($"WSL-distro '{wsl.Distro}' is niet bereikbaar."); return false; }
        user = user.Trim();
        var scripts = Wsl.Wsl.ToWslPath(ScriptDir);

        log("Bouwpakketten installeren (root)…");
        if (!await StepAsync(["bash", "-c",
                "DEBIAN_FRONTEND=noninteractive apt-get update -qq && DEBIAN_FRONTEND=noninteractive apt-get install -y -qq " +
                "build-essential flex bison libssl-dev libelf-dev bc dwarves git pkg-config curl clang >/dev/null"], true, ct))
            return false;

        log("owly en de wifidrivers bouwen (dit duurt 10 à 20 minuten)…");
        if (!await StepAsync(["bash", $"{scripts}/build.sh"], false, ct)) return false;

        log("Installeren in WSL (root)…");
        var home = (await wsl.RunAsync(["bash", "-c", "echo $HOME"], ct: ct)).Out.Trim();
        if (!await StepAsync(["bash", $"{scripts}/setup.sh", user, $"{home}/windrop-build/omdrop-owl", $"{home}/windrop-build/modules"], true, ct))
            return false;

        var stick = await UsbStick.FindAsync();
        if (stick is null)
        {
            log("Klaar, maar er is nog geen ondersteunde wifistick gevonden. Prik hem in en kies opnieuw ‘Installatie en wifistick…’.");
            return true;
        }
        log($"Wifistick gevonden: {UsbStick.Known[stick.VidPid]} ({stick.BusId}).");
        if (!stick.Shared)
        {
            log("Stick delen met WSL (Windows vraagt om toestemming)…");
            if (!await UsbStick.BindAsync(stick)) { log("Delen is niet gelukt of geweigerd."); return false; }
        }
        log("Klaar.");
        return true;
    }

    private async Task<bool> StepAsync(string[] args, bool root, CancellationToken ct)
    {
        using var p = wsl.Start(args, root);
        p.StandardInput.Close();
        var err = Task.Run(async () =>
        {
            string? line;
            while ((line = await p.StandardError.ReadLineAsync(ct)) is not null) log("  " + line);
        }, ct);
        string? outLine;
        while ((outLine = await p.StandardOutput.ReadLineAsync(ct)) is not null) log("  " + outLine);
        await err;
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0) log($"Mislukt (code {p.ExitCode}).");
        return p.ExitCode == 0;
    }
}
