using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WinDrop.AirDrop;

namespace WinDrop.UI;

/// <summary>The AirDrop sheet: nearby receivers as bubbles; click one to send.</summary>
public partial class ShareWindow : Window
{
    private readonly AirDropService _airdrop;
    private readonly List<string> _files;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, TextBlock> _status = new();
    private int _sending;

    public ShareWindow(AirDropService airdrop, IEnumerable<string> files)
    {
        InitializeComponent();
        _airdrop = airdrop;
        _files = files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        SubtitleText.Text = _files.Count == 1 ? System.IO.Path.GetFileName(_files[0]) : $"{_files.Count} bestanden";
        Loaded += async (_, _) => await BrowseLoopAsync();
    }

    public void AddFiles(IEnumerable<string> files)
    {
        foreach (var f in files)
            if (!_files.Contains(f, StringComparer.OrdinalIgnoreCase)) _files.Add(f);
        SubtitleText.Text = _files.Count == 1 ? System.IO.Path.GetFileName(_files[0]) : $"{_files.Count} bestanden";
    }

    /// <summary>For <c>WinDrop render</c>: fake receivers instead of browsing.</summary>
    internal bool Demo { get; init; }

    internal void ShowDemo(params string[] names)
    {
        foreach (var n in names) AddPeer(new Peer(n, n, ""));
        SearchingPanel.Visibility = Visibility.Collapsed;
        if (Peers.Children.Count > 1 && Peers.Children[1] is StackPanel second && second.Children[2] is TextBlock s) s.Text = "Verstuurd";
    }

    private async Task BrowseLoopAsync()
    {
        if (Demo) return;
        if (_airdrop.State != AirDropState.Visible)
        {
            SearchingText.Text = "AirDrop staat uit. Zet hem aan via het WinDrop-icoon.";
            return;
        }
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                // Don't browse over a send in progress; both use the radio.
                if (_sending > 0) { await Task.Delay(500, _cts.Token); continue; }
                foreach (var peer in await _airdrop.BrowseAsync(6, _cts.Token))
                    if (!_status.ContainsKey(peer.Name)) AddPeer(peer);
                SearchingPanel.Visibility = _status.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (OperationCanceledException) { break; }
        }
    }

    private void AddPeer(Peer peer)
    {
        var bubble = new Grid { Width = 64, Height = 64 };
        bubble.Children.Add(new Ellipse { Fill = Theme.BubbleBrush(peer.Name) });
        bubble.Children.Add(new TextBlock
        {
            Text = Theme.Initials(peer.Name), Foreground = Brushes.White, FontSize = 26, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        var name = new TextBlock
        {
            Text = peer.Name, FontSize = 12, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
            MaxWidth = 96, Margin = new Thickness(0, 6, 0, 0),
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        var status = new TextBlock { FontSize = 11, TextAlignment = TextAlignment.Center };
        status.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        _status[peer.Name] = status;

        var item = new StackPanel { Width = 104, Margin = new Thickness(6), Cursor = Cursors.Hand };
        item.Children.Add(bubble);
        item.Children.Add(name);
        item.Children.Add(status);
        item.MouseLeftButtonUp += async (_, _) => await SendToAsync(peer, status, bubble);
        Peers.Children.Add(item);
    }

    private async Task SendToAsync(Peer peer, TextBlock status, Grid bubble)
    {
        if (status.Text == "Wachten…") return;
        status.Text = "Wachten…";
        bubble.Opacity = 0.6;
        _sending++;
        (SendResult result, string detail) = (SendResult.Failed, "");
        try { (result, detail) = await _airdrop.SendAsync(peer.Name, _files, _cts.Token); }
        catch (OperationCanceledException) { return; }
        finally { _sending--; }
        bubble.Opacity = 1;
        status.Text = result switch
        {
            SendResult.Delivered => "Verstuurd",
            SendResult.Declined => "Geweigerd",
            SendResult.NotFound => "Niet gevonden",
            _ => "Mislukt",
        };
        status.ToolTip = string.IsNullOrEmpty(detail) ? null : detail;
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();

    private void Card_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
        base.OnKeyDown(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts.Cancel();
        base.OnClosed(e);
    }
}
