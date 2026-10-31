using System.IO.Pipes;
using System.Text;

namespace WinDrop.Shell;

/// <summary>
/// Keeps one WinDrop running. Later launches (one per file from Explorer)
/// pass their arguments to it over a named pipe and exit.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private static readonly string PipeName = $"WinDrop-{Environment.UserName}";
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cts = new();

    public bool IsFirst { get; }

    public event Action<string[]>? ArgumentsReceived;

    public SingleInstance()
    {
        _mutex = new Mutex(true, $@"Local\WinDrop-{Environment.UserName}", out bool created);
        IsFirst = created;
    }

    public void Listen() => _ = Task.Run(async () =>
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(_cts.Token);
                using var reader = new StreamReader(server, Encoding.UTF8);
                var text = await reader.ReadToEndAsync(_cts.Token);
                ArgumentsReceived?.Invoke(text.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { }
        }
    });

    public static bool Send(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(3000);
            using var writer = new StreamWriter(client, Encoding.UTF8);
            writer.Write(string.Join('\n', args));
            return true;
        }
        catch (Exception e) when (e is TimeoutException or IOException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (IsFirst) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
