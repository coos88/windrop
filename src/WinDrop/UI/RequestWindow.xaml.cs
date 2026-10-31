using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WinDrop.AirDrop;

namespace WinDrop.UI;

/// <summary>"iPhone van Coos wil 2 foto's delen" with Weigeren/Accepteren, bottom-right like a toast.</summary>
public partial class RequestWindow : Window
{
    private readonly IncomingRequest _request;
    private readonly DispatcherTimer _timeout;

    public RequestWindow(IncomingRequest request)
    {
        InitializeComponent();
        _request = request;
        Bubble.Fill = Theme.BubbleBrush(request.Sender);
        InitialsText.Text = Theme.Initials(request.Sender);
        TitleText.Text = $"‘{request.Sender}’ wil {Describe(request.Files)} delen.";
        FilesText.Text = string.Join(", ", request.Files.Take(3).Select(f => f.Name)) +
                         (request.Files.Count > 3 ? $" en nog {request.Files.Count - 3}" : "");

        // owly stops waiting for an answer after 15 s.
        _timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _timeout.Tick += (_, _) => { _request.Decline(); Close(); };
        Loaded += (_, _) => { Place(); _timeout.Start(); FadeIn(); };
    }

    internal static string Describe(IReadOnlyList<OfferedFile> files)
    {
        if (files.Count == 0) return "iets";
        bool images = files.All(f => f.Type.Contains("image", StringComparison.OrdinalIgnoreCase) || IsExt(f.Name, ".jpg", ".jpeg", ".heic", ".png", ".gif"));
        bool videos = files.All(f => f.Type.Contains("movie", StringComparison.OrdinalIgnoreCase) || f.Type.Contains("video", StringComparison.OrdinalIgnoreCase) || IsExt(f.Name, ".mov", ".mp4"));
        string noun = images ? (files.Count == 1 ? "foto" : "foto's")
                    : videos ? (files.Count == 1 ? "video" : "video's")
                    : (files.Count == 1 ? "bestand" : "bestanden");
        return files.Count == 1 ? $"een {noun}" : $"{files.Count} {noun}";
    }

    private static bool IsExt(string name, params string[] exts) =>
        exts.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    private void Place()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth;
        Top = area.Bottom - ActualHeight;
    }

    private void FadeIn()
    {
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        BeginAnimation(TopProperty, new DoubleAnimation(Top + 24, Top, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    private void Accept_Click(object sender, RoutedEventArgs e) { _request.Accept(); Close(); }
    private void Decline_Click(object sender, RoutedEventArgs e) { _request.Decline(); Close(); }

    protected override void OnClosed(EventArgs e)
    {
        _timeout.Stop();
        base.OnClosed(e);
    }
}
