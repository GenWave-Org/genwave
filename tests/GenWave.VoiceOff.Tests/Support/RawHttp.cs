using System.Net.Sockets;
using System.Text;

namespace GenWave.VoiceOff.Tests.Support;

/// <summary>A bare GET over a socket, so the request path reaches the server exactly as written (HttpClient tidies <c>/../</c> away).</summary>
internal static class RawHttp
{
    /// <summary>The status code the server answers to <c>GET path</c>.</summary>
    internal static int GetStatus(int port, string path)
    {
        using TcpClient client = new();
        client.Connect("127.0.0.1", port);
        using NetworkStream stream = client.GetStream();
        stream.ReadTimeout = 5000;
        byte[] request = Encoding.ASCII.GetBytes($"GET {path} HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n");
        stream.Write(request);
        using StreamReader reader = new(stream, Encoding.ASCII);
        string statusLine = reader.ReadLine() ?? string.Empty;
        return int.Parse(statusLine.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture);
    }
}
