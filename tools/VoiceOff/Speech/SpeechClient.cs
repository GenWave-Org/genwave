using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GenWave.Tts;

namespace GenWave.VoiceOff.Speech;

/// <summary>
/// The one HTTP client the tool renders through (SPEC F214.1): <c>POST /v1/audio/speech</c> and
/// <c>GET /v1/audio/voices</c> against an engine endpoint. Failures come back as typed results.
/// The client owns the deadline (the profile's timeout), so the <see cref="HttpClient"/> it is given
/// must not carry one of its own: use <see cref="CreateHttpClient"/>.
/// </summary>
internal sealed class SpeechClient
{
    internal const string ResponseFormat = "wav";
    private const int ErrorBodyChars = 200;
    private const int ReadChunkBytes = 81920;
    private const long ErrorBodyBytes = 4096;

    private readonly HttpClient http;
    private readonly Uri endpoint;
    private readonly TimeSpan timeout;
    private readonly TimeProvider time;
    private readonly SpeechLimits limits;

    /// <exception cref="ArgumentException"><paramref name="http"/> has its own timeout, which would silently cap <paramref name="timeout"/>.</exception>
    internal SpeechClient(HttpClient http, Uri endpoint, TimeSpan timeout, TimeProvider? time = null, SpeechLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(endpoint);
        if (http.Timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentException(
                $"The HttpClient has a {http.Timeout.TotalSeconds:0.#} second timeout of its own, which would cap the profile's; use SpeechClient.CreateHttpClient().",
                nameof(http));
        }

        this.http = http;
        this.endpoint = endpoint;
        this.timeout = timeout;
        this.time = time ?? TimeProvider.System;
        this.limits = limits ?? SpeechLimits.Default;
    }

    /// <summary>An <see cref="HttpClient"/> with no timeout of its own, so the profile's timeout is the only deadline.</summary>
    internal static HttpClient CreateHttpClient() => new() { Timeout = Timeout.InfiniteTimeSpan };

    private sealed record SpeechRequest(
        [property: JsonPropertyName("input")] string Input,
        [property: JsonPropertyName("voice")] string Voice,
        [property: JsonPropertyName("speed")] double Speed,
        [property: JsonPropertyName("response_format")] string ResponseFormat);

    /// <summary>Renders <paramref name="text"/> with <paramref name="voice"/>; the caller's own cancellation still propagates.</summary>
    internal async Task<SpeechResult> RenderAsync(string text, string voice, double speed, CancellationToken ct)
    {
        long start = time.GetTimestamp();
        (byte[]? audio, SpeechError? error) = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, EndpointUri.Combine(endpoint.AbsoluteUri, "v1/audio/speech"))
            {
                Content = JsonContent.Create(new SpeechRequest(text, voice, speed, ResponseFormat)),
            },
            limits.MaxAudioBytes,
            ct);
        double wall = time.GetElapsedTime(start).TotalMilliseconds;
        return new SpeechResult(audio ?? [], wall, error);
    }

    internal async Task<VoiceListResult> ListVoicesAsync(CancellationToken ct)
    {
        (byte[]? body, SpeechError? error) = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, EndpointUri.Combine(endpoint.AbsoluteUri, "v1/audio/voices")),
            limits.MaxVoicesBytes,
            ct);
        if (error is not null)
        {
            return new VoiceListResult([], error);
        }

        try
        {
            KokoroVoicesResponse? parsed = JsonSerializer.Deserialize<KokoroVoicesResponse>(body ?? []);
            IReadOnlyList<string> ids = parsed?.VoiceIds() ?? [];
            return ids.Count > 0
                ? new VoiceListResult(ids, null)
                : new VoiceListResult([], new SpeechError(SpeechErrorKind.EmptyBody, "The voices listing had no voices."));
        }
        catch (JsonException ex)
        {
            return new VoiceListResult([], new SpeechError(SpeechErrorKind.EmptyBody, $"The voices listing was not readable: {ex.Message}"));
        }
    }

    private async Task<(byte[]? Body, SpeechError? Error)> SendAsync(Func<HttpRequestMessage> build, long maxBytes, CancellationToken ct)
    {
        using CancellationTokenSource deadline = new(timeout, time);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        try
        {
            using HttpRequestMessage request = build();
            using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            (byte[] body, bool truncated) = await ReadBoundedAsync(
                response.Content, response.IsSuccessStatusCode ? maxBytes : ErrorBodyBytes, linked.Token);
            if (!response.IsSuccessStatusCode)
            {
                string text = Encoding.UTF8.GetString(body);
                string head = text.Length > ErrorBodyChars ? text[..ErrorBodyChars] : text;
                return (null, new SpeechError(SpeechErrorKind.HttpStatus, $"HTTP {(int)response.StatusCode}: {head}"));
            }

            if (truncated)
            {
                return (null, new SpeechError(SpeechErrorKind.TooLarge, $"The engine's answer was over the {maxBytes} byte limit."));
            }

            return body.Length == 0
                ? (null, new SpeechError(SpeechErrorKind.EmptyBody, "The engine answered with an empty body."))
                : (body, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (null, new SpeechError(SpeechErrorKind.Timeout, $"No answer within {timeout.TotalSeconds:0.#} seconds."));
        }
        catch (HttpRequestException ex)
        {
            return (null, new SpeechError(SpeechErrorKind.Unreachable, ex.Message));
        }
        catch (IOException ex)
        {
            // The engine dropped the connection mid-body (HttpIOException is an IOException, not an HttpRequestException).
            return (null, new SpeechError(SpeechErrorKind.Unreachable, $"The connection dropped mid-answer: {ex.Message}"));
        }
    }

    // Reads at most maxBytes; Truncated says the body had more.
    private static async Task<(byte[] Body, bool Truncated)> ReadBoundedAsync(HttpContent content, long maxBytes, CancellationToken ct)
    {
        await using Stream stream = await content.ReadAsStreamAsync(ct);
        using MemoryStream buffer = new();
        byte[] chunk = new byte[ReadChunkBytes];
        while (true)
        {
            long room = maxBytes - buffer.Length;
            int read = await stream.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, room + 1)), ct);
            if (read == 0)
            {
                return (buffer.ToArray(), false);
            }

            if (read > room)
            {
                buffer.Write(chunk, 0, (int)room);
                return (buffer.ToArray(), true);
            }

            buffer.Write(chunk, 0, read);
        }
    }
}
