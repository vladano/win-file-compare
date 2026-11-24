using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace FileCompare.Core.Shell;

/// <summary>
/// Keeps one running copy of the app per user session. The first copy owns a named mutex and listens on a
/// named pipe; later copies send their command-line arguments through the pipe and exit.
/// The pipe only accepts connections from the same user account.
/// </summary>
public sealed class SingleInstanceChannel : IDisposable
{
    /// <summary>Messages larger than this are ignored (a command line is far smaller).</summary>
    private const int MaxMessageBytes = 64 * 1024;

    private readonly string _name;
    private Mutex? _mutex;
    private CancellationTokenSource? _listening;

    public SingleInstanceChannel(string name) => _name = name;

    /// <summary>A name that is unique per user and Windows session, and safe for mutex and pipe names.</summary>
    public static string NameFor(string app)
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        string raw = $"{app}-{Environment.UserDomainName}-{Environment.UserName}-{process.SessionId}";
        var safe = new StringBuilder(raw.Length);
        foreach (char c in raw) safe.Append(char.IsLetterOrDigit(c) || c is '-' or '.' ? c : '_');
        return safe.ToString();
    }

    /// <summary>True when this is the first copy. It must then call <see cref="StartListening"/>.</summary>
    public bool TryBecomePrimary()
    {
        _mutex = new Mutex(initiallyOwned: true, @"Local\" + _name, out bool created);
        if (created) return true;

        _mutex.Dispose();
        _mutex = null;
        return false;
    }

    /// <summary>Receives the arguments other copies send. <paramref name="onMessage"/> runs on a background thread.</summary>
    public void StartListening(Action<string[]> onMessage)
    {
        _listening = new CancellationTokenSource();
        var ct = _listening.Token;
        _ = Task.Run(() => ListenAsync(onMessage, ct), ct);
    }

    private async Task ListenAsync(Action<string[]> onMessage, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(_name, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(ct);

                var args = await ReadMessageAsync(server, ct);
                if (args is not null) onMessage(args);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A client that disconnected early, or a pipe name taken by someone else: try again shortly.
                try { await Task.Delay(200, ct); } catch (OperationCanceledException) { return; }
            }
        }
    }

    private static async Task<string[]?> ReadMessageAsync(Stream stream, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxMessageBytes) return null;
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(buffer.ToArray());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Sends arguments to the first copy. Waits up to <paramref name="timeout"/> because it may still be starting.
    /// Returns false when no copy answered.
    /// </summary>
    public static bool TrySend(string name, string[] args, TimeSpan timeout)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect((int)timeout.TotalMilliseconds);
            client.Write(JsonSerializer.SerializeToUtf8Bytes(args));
            client.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _listening?.Cancel();
        _listening?.Dispose();
        _listening = null;

        if (_mutex is not null)
        {
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { /* released already, or owned by another thread at shutdown */ }
            _mutex.Dispose();
            _mutex = null;
        }
    }
}
