using System.Text.Json;

namespace WinDrop;

/// <summary>User settings, stored in %APPDATA%\WinDrop\settings.json.</summary>
public sealed class Settings
{
    public string Distro { get; set; } = "ubuntu";
    /// <summary>The name iPhones and Macs see; the computer name when empty.</summary>
    public string DisplayName { get; set; } = "";
    public string ReceiveDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    /// <summary>Become visible to AirDrop when WinDrop starts.</summary>
    public bool VisibleOnStart { get; set; } = true;
    public bool StartWithWindows { get; set; }

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinDrop", "settings.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new();
        }
        catch (JsonException) { }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }
}
