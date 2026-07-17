using System.Net;
using System.Net.Sockets;
using System.Text;

namespace GenWave.Host.Tests;

/// <summary>
/// A loopback stand-in for the Liquidsoap telnet socket. It accepts the short-lived one-command
/// connections <c>LiquidsoapControl</c> makes, <b>records every command line</b> (so tests can assert
/// that on-air determination issues only the output metadata command — never queue listing), and
/// replies with a canned body terminated by the <c>END</c> sentinel.
/// </summary>
sealed class FakeEngineServer : IAsyncDisposable
{
    readonly TcpListener listener;
    readonly Func<string, string> respond;
    readonly List<string> commands = [];
    readonly CancellationTokenSource cts = new();
    readonly Task loop;

    public FakeEngineServer(Func<string, string> respond)
    {
        this.respond = respond;
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        loop = AcceptLoopAsync(cts.Token);
    }

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    /// <summary>Every command line received, in order. Safe to read after the calls under test return
    /// (the command is recorded before its reply is sent).</summary>
    public IReadOnlyList<string> Commands
    {
        get { lock (commands) return commands.ToArray(); }
    }

    async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }

            // Connections are sequential (one command per connection), so handle inline.
            try { await HandleAsync(client, ct); }
            catch { /* a torn-down connection must not crash the listener */ }
        }
    }

    async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        await using (var stream = client.GetStream())
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
            var command = await reader.ReadLineAsync(ct);
            if (command is null) return;

            lock (commands) commands.Add(command);

            var payload = Encoding.UTF8.GetBytes(respond(command) + "\nEND\n");
            await stream.WriteAsync(payload, ct);
            await stream.FlushAsync(ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await cts.CancelAsync();
        listener.Stop();
        try { await loop; } catch { /* expected on shutdown */ }
        cts.Dispose();
    }
}
