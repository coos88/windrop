using System.Windows;

namespace WinDrop.UI;

public partial class SetupWindow : Window
{
    private readonly Func<Action<string>, CancellationToken, Task<bool>> _run;
    private readonly CancellationTokenSource _cts = new();

    public bool Succeeded { get; private set; }

    public SetupWindow(Func<Action<string>, CancellationToken, Task<bool>> run)
    {
        InitializeComponent();
        _run = run;
    }

    private void Append(string line) => Dispatcher.BeginInvoke(() =>
    {
        LogBox.AppendText(line + Environment.NewLine);
        LogBox.ScrollToEnd();
    });

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        StartButton.IsEnabled = false;
        try
        {
            Succeeded = await Task.Run(() => _run(Append, _cts.Token));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Append("Fout: " + ex.Message);
        }
        StartButton.Content = Succeeded ? "Gereed" : "Opnieuw";
        StartButton.IsEnabled = !Succeeded;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _cts.Cancel();
        base.OnClosed(e);
    }
}
