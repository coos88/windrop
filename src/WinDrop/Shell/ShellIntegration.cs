using Microsoft.Win32;

namespace WinDrop.Shell;

/// <summary>Explorer's "Delen via AirDrop" entry and the Run key, both per user.</summary>
public static class ShellIntegration
{
    private const string MenuKey = @"Software\Classes\*\shell\WinDrop";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static string Exe => Environment.ProcessPath!;

    public static bool ContextMenuInstalled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(MenuKey + @"\command");
            return key?.GetValue(null) is string cmd && cmd.Contains(Exe, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Adds "Delen via AirDrop" to the classic context menu (Windows 11:
    /// "Meer opties weergeven"). Explorer starts one process per selected
    /// file; the running instance gathers them into one share.
    /// </summary>
    public static void InstallContextMenu()
    {
        using var key = Registry.CurrentUser.CreateSubKey(MenuKey);
        key.SetValue(null, "Delen via AirDrop");
        key.SetValue("Icon", $"\"{Exe}\",0");
        key.SetValue("MultiSelectModel", "Player");
        using var cmd = key.CreateSubKey("command");
        cmd.SetValue(null, $"\"{Exe}\" share \"%1\"");
    }

    public static void RemoveContextMenu() => Registry.CurrentUser.DeleteSubKeyTree(MenuKey, throwOnMissingSubKey: false);

    public static bool StartWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("WinDrop") is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue("WinDrop", $"\"{Exe}\"");
            else key.DeleteValue("WinDrop", throwOnMissingValue: false);
        }
    }
}
