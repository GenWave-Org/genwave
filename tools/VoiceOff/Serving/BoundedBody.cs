using System.Text;
using Microsoft.AspNetCore.Http;

namespace GenWave.VoiceOff.Serving;

/// <summary>Reads a request body as UTF-8 text without ever holding more than a limit, whatever Content-Length said (or did not say).</summary>
internal static class BoundedBody
{
    /// <returns>The text, or null when the body is longer than <paramref name="limit"/> bytes.</returns>
    internal static async Task<string?> ReadAsync(Stream body, int limit, CancellationToken ct)
    {
        byte[] buffer = new byte[limit + 1];
        int length = 0;
        try
        {
            while (length < buffer.Length)
            {
                int read = await body.ReadAsync(buffer.AsMemory(length), ct);
                if (read == 0)
                {
                    break;
                }

                length += read;
            }
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            // Kestrel's own request size limit tripped first.
            return null;
        }

        return length > limit ? null : Encoding.UTF8.GetString(buffer, 0, length);
    }
}
