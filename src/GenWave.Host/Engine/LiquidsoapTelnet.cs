using System.Net.Sockets;
using System.Text;

namespace GenWave.Host.Engine;

/// <summary>
/// One-shot Liquidsoap telnet transport: connect, write a single command line, read until the
/// protocol's <c>END</c> sentinel line, close. Extracted so <see cref="LiquidsoapControl"/> (queue
/// push / on-air metadata) and <see cref="LiquidsoapTuningReader"/> (<c>gw_tuning</c>, SPEC F213.5)
/// implement the wire protocol exactly once — behaviourally identical to
/// <see cref="LiquidsoapControl"/>'s own pre-extraction transport.
/// </summary>
static class LiquidsoapTelnet
{
    public static async Task<string> SendAsync(string host, int port, string command, CancellationToken ct)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, ct);
        await using var stream = client.GetStream();

        await stream.WriteAsync(Encoding.UTF8.GetBytes(command + "\n"), ct);

        var sb = new StringBuilder();
        var buf = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(buf, ct)) > 0)
        {
            sb.Append(Encoding.UTF8.GetString(buf, 0, read));
            // Each Liquidsoap response is terminated by "END" on its own line.
            if (sb.ToString().Replace("\r", "").Split('\n').Contains("END")) break;
        }

        var lines = sb.ToString().Replace("\r", "").Split('\n');
        return string.Join('\n', lines.TakeWhile(l => l != "END")).Trim();
    }
}
