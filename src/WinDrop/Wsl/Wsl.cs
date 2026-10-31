using System.Diagnostics;
using System.Text;

namespace WinDrop.Wsl;

/// <summary>Runs commands in the WSL distro that hosts owly.</summary>
public sealed class Wsl(string distro)
{
    public string Distro { get; } = distro;

    /// <summary>Starts a long-running command with redirected stdio.</summary>
    public Process Start(IEnumerable<string> args, bool root = false)
    {
        var psi = new ProcessStartInfo("wsl.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-d");
        psi.ArgumentList.Add(Distro);
        if (root)
        {
            psi.ArgumentList.Add("-u");
            psi.ArgumentList.Add("root");
        }
        psi.ArgumentList.Add("--");
        foreach (var a in args) psi.ArgumentList.Add(a);
        return Process.Start(psi) ?? throw new InvalidOperationException("wsl.exe kon niet starten");
    }

    /// <summary>Runs a command to completion and returns its exit code and output.</summary>
    public async Task<(int Code, string Out, string Err)> RunAsync(IEnumerable<string> args, bool root = false, CancellationToken ct = default)
    {
        using var p = Start(args, root);
        p.StandardInput.Close();
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return (p.ExitCode, await stdout, await stderr);
    }

    /// <summary>C:\Users\x → /mnt/c/Users/x (WSL's default automount).</summary>
    public static string ToWslPath(string windowsPath)
    {
        var full = Path.GetFullPath(windowsPath);
        if (full.Length < 2 || full[1] != ':') throw new ArgumentException($"Geen lokaal pad: {windowsPath}");
        return "/mnt/" + char.ToLowerInvariant(full[0]) + full[2..].Replace('\\', '/');
    }

    /// <summary>/mnt/c/Users/x → C:\Users\x; null for paths outside /mnt/&lt;drive&gt;.</summary>
    public static string? ToWindowsPath(string wslPath)
    {
        if (!wslPath.StartsWith("/mnt/", StringComparison.Ordinal) || wslPath.Length < 6) return null;
        char drive = char.ToUpperInvariant(wslPath[5]);
        if (wslPath.Length > 6 && wslPath[6] != '/') return null;
        return drive + ":" + (wslPath.Length > 6 ? wslPath[6..].Replace('/', '\\') : "\\");
    }
}
