namespace GenWave.Tts;

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;

/// <summary>
/// Piper local-fallback TTS client (SPEC F70.1, STORY-190) — the SAME <see cref="ITtsSynthesizer"/>
/// seam as <see cref="KokoroTtsSynthesizer"/>, routed to by <see cref="FallbackTtsSynthesizer"/>
/// when Kokoro is unhealthy or throws. Targets the upstream <c>piper.http_server</c> HTTP wrapper
/// the compose <c>piper</c> service runs: a single POST of the already-normalized text to the
/// endpoint's root path returns raw WAV bytes.
///
/// Content-Type MUST be something other than a form-encoded type: <c>piper.http_server</c> reads
/// the request body verbatim as the text to speak, but only when Flask hasn't already consumed it
/// parsing form data — a form-encoded POST leaves that body empty and always renders nothing
/// (verified against the real image). <c>text/plain</c> avoids that trap.
///
/// No per-request voice selector exists on that wrapper — exactly one voice model is baked into
/// the running container at start (compose.yaml's <c>MODEL_DOWNLOAD_LINK</c>) — so
/// <paramref name="voice"/> on <see cref="SynthesizeAsync"/> is accepted (the
/// <see cref="ITtsSynthesizer"/> contract shape every engine shares) but never put on the wire;
/// see <see cref="TtsFallbackOptions.Voice"/>.
///
/// No boot-frozen <see cref="HttpClient.BaseAddress"/>, same discipline as
/// <see cref="KokoroTtsSynthesizer"/> (SPEC F36.1-F36.2): <c>Tts:Fallback:Endpoint</c> is read from
/// <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> per call, so a live repoint applies to the
/// very next render with no api restart.
/// </summary>
public sealed class PiperTtsSynthesizer(
    HttpClient http,
    IOptionsMonitor<TtsOptions> ttsOptions,
    IOptionsMonitor<TtsFallbackOptions> fallbackOptions) : ITtsSynthesizer
{
    public async Task<string> SynthesizeAsync(string text, string voice, CancellationToken ct)
    {
        var fallbackCfg = fallbackOptions.CurrentValue;
        var ttsCfg = ttsOptions.CurrentValue;

        using var content = new StringContent(text, Encoding.UTF8, "text/plain");
        var requestUri = EndpointUri.Combine(fallbackCfg.Endpoint, "/");
        var response = await http.PostAsync(requestUri, content, ct);
        response.EnsureSuccessStatusCode();   // throws HttpRequestException on non-2xx

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var path = GetCachePath(text, voice, ttsCfg);

        // Path.GetDirectoryName always returns a non-null string when the path is produced
        // by Path.Combine with a non-empty CacheRoot; the guard below satisfies the compiler
        // without using the null-forgiving operator (mirrors KokoroTtsSynthesizer).
        var dir = Path.GetDirectoryName(path);
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllBytesAsync(path, bytes, ct);
        return path;
    }

    /// <summary>
    /// Shares <see cref="KokoroTtsSynthesizer"/>'s own (text, voice) hash formula and
    /// <see cref="TtsOptions.CacheRoot"/>/<see cref="TtsOptions.Format"/>, under a "piper/"
    /// subfolder — the ONLY thing that keeps a Piper-rendered temp file from ever colliding with a
    /// concurrent Kokoro one for the exact same (text, voice) pair. Both files are transient either
    /// way — <see cref="TtsSegmentSource"/> moves this path to its own final cache location
    /// (SPEC F70.4: the identical downstream measure/cue/cache pipeline), and
    /// <see cref="SafeSegmentAuthor"/> deletes it once the mixed artifact exists.
    /// </summary>
    static string GetCachePath(string text, string voice, TtsOptions cfg)
    {
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(text + "|" + voice)));
        return Path.Combine(cfg.CacheRoot, "piper", $"{hash}.{cfg.Format}");
    }
}
