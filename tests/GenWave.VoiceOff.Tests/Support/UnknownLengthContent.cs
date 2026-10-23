using System.Net;

namespace GenWave.VoiceOff.Tests.Support;

/// <summary>A request body that does not know its own length, so the client sends it chunked with no Content-Length.</summary>
internal sealed class UnknownLengthContent(byte[] bytes) : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes, 0, bytes.Length);

    protected override bool TryComputeLength(out long length)
    {
        length = -1;
        return false;
    }
}
