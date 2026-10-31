using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace WinDrop.UI;

/// <summary>Follows the Windows app theme (light/dark).</summary>
public static class Theme
{
    public static bool IsDark
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
    }

    public static void Apply(ResourceDictionary resources)
    {
        bool dark = IsDark;
        void Set(string key, string light, string darkValue) =>
            resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? darkValue : light));
        Set("CardBrush", "#F7F7F8", "#2C2C2E");
        Set("CardBorderBrush", "#1F000000", "#33FFFFFF");
        Set("TextBrush", "#1D1D1F", "#F5F5F7");
        Set("SubtleTextBrush", "#6E6E73", "#98989D");
        Set("ButtonBrush", "#E5E5EA", "#48484A");
    }

    /// <summary>Initials for a device bubble: "iPhone van Coos" → "C", "Jane's MacBook" → "J".</summary>
    public static string Initials(string name)
    {
        var words = name.Replace("’s", "").Replace("'s", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "iPhone", "iPad", "MacBook", "Mac", "iMac", "van", "de", "Air", "Pro", "mini" };
        var word = words.FirstOrDefault(w => !skip.Contains(w)) ?? words.FirstOrDefault() ?? "?";
        return char.ToUpperInvariant(word[0]).ToString();
    }

    private static readonly string[] Palette = ["#FF9F0A", "#30D158", "#0A84FF", "#BF5AF2", "#FF375F", "#64D2FF", "#FFD60A"];

    public static Brush BubbleBrush(string name)
    {
        int h = 0;
        foreach (char c in name) h = h * 31 + c;
        var color = (Color)ColorConverter.ConvertFromString(Palette[Math.Abs(h % Palette.Length)]);
        return new LinearGradientBrush(color, Color.Multiply(color, 0.75f), 90);
    }
}
