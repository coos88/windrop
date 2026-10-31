namespace WinDrop;

/// <summary>Appends to %LOCALAPPDATA%\WinDrop\windrop.log.</summary>
public static class Log
{
    private static readonly object Gate = new();

    public static string FilePath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinDrop", "windrop.log");

    public static void Write(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 2_000_000)
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
            catch (IOException) { }
        }
    }
}
