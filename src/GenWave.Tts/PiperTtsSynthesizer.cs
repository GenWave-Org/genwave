namespace GenWave.Tts;

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

/// <summary>
/// Piper-engine hop renderer (SPEC F70.1, STORY-190, gh-#147) — the <c>"piper"</c>
/// <see cref="IFallbackProfileRenderer"/> the chain executor <see cref="FallbackTtsSynthesizer"/>
/// resolves for piper-kind hops (including the implicit legacy single-hop chain the flat
/// <c>Tts:Fallback:Endpoint</c>/<c>Voice</c> keys form). Targets the upstream
/// <c>piper.http_server</c> HTTP wrapper the compose <c>piper</c> service runs: a single POST of
/// the already-normalized text to the hop endpoint's root path returns raw WAV bytes.
///
/// Content-Type MUST be something other than a form-encoded type: <c>piper.http_server</c> reads
/// the request body verbatim as the text to speak, but only when Flask hasn't already consumed it
/// parsing form data — a form-encoded POST leaves that body empty and always renders nothing
/// (verified against the real image). <c>text/plain</c> avoids that trap.
///
/// No per-request voice selector exists on that wrapper — exactly one voice model is baked into
/// the running container at start (compose.yaml's <c>MODEL_DOWNLOAD_LINK</c>) — so neither the
/// caller's <paramref name="requestVoice"/> nor the hop's display-only
/// <see cref="TtsFallbackProfile.Voice"/> is ever put on the wire; see that property's
/// schema-level remarks (gh-#147's honest-labeling contract).
///
/// No boot-frozen <see cref="HttpClient.BaseAddress"/>, same discipline as
/// <see cref="KokoroTtsSynthesizer"/> (SPEC F36.1-F36.2): the hop endpoint arrives per call from
/// the profile, itself resolved from <see cref="IOptionsMonitor{TOptions}.CurrentValue"/>
/// upstream, so a live repoint of the legacy <c>Tts:Fallback:Endpoint</c> applies to the very
/// next render with no api restart.
/// </summary>
public sealed class PiperTtsSynthesizer(
    HttpClient http,
    IOptionsMonitor<TtsOptions> ttsOptions) : IFallbackProfileRenderer
{
    public string Engine => DependencyNames.Piper;

    public async Task<string> RenderAsync(TtsFallbackProfile profile, string text, string requestVoice, CancellationToken ct)
    {
        var ttsCfg = ttsOptions.CurrentValue;

        using var content = new StringContent(text, Encoding.UTF8, "text/plain");
        var requestUri = EndpointUri.Combine(profile.Endpoint, "/");
        var response = await http.PostAsync(requestUri, content, ct);
        response.EnsureSuccessStatusCode();   // throws HttpRequestException on non-2xx

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var path = GetCachePath(text, requestVoice, ttsCfg);

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
    /// concurrent Kokoro one for the exact same (text, voice) pair. The caller's request voice
    /// (not the profile's display-only voice) feeds the hash — unchanged from the pre-gh-#147
    /// formula, so upgrade never orphans or double-renders a cached temp file. Both files are
    /// transient either way — <see cref="TtsSegmentSource"/> moves this path to its own final
    /// cache location (SPEC F70.4: the identical downstream measure/cue/cache pipeline), and
    /// <see cref="SafeSegmentAuthor"/> deletes it once the mixed artifact exists.
    /// </summary>
    static string GetCachePath(string text, string voice, TtsOptions cfg)
    {
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(text + "|" + voice)));
        return Path.Combine(cfg.CacheRoot, "piper", $"{hash}.{cfg.Format}");
    }
}
