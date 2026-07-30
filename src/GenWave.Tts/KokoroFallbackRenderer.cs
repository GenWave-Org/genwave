namespace GenWave.Tts;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

/// <summary>
/// Kokoro-engine hop renderer for the gh-#147 fallback chain — the same
/// <c>POST /v1/audio/speech</c> wire shape as <see cref="KokoroTtsSynthesizer"/>, but pointed at
/// the HOP's own <see cref="TtsFallbackProfile.Endpoint"/> (a second Kokoro deployment, a remote
/// bring-your-own HTTP server — F70.1's "remote" engine role) rather than <c>Tts:Endpoint</c>.
///
/// Voice IS honored on the wire here (the gh-#147 honest-labeling flip): kokoro-fastapi selects a
/// voice per request, so a non-empty <see cref="TtsFallbackProfile.Voice"/> overrides the caller's
/// per-request voice for this hop; empty forwards the caller's voice unchanged. Contrast
/// <see cref="PiperTtsSynthesizer"/>, where the profile voice is display-only by upstream design.
///
/// The cache path carries the hop endpoint in its hash (unlike the primary's formula): a hop and
/// the primary — or two kokoro-kind hops — can render the same (text, voice) pair concurrently as
/// different audio and must never collide on a transient file. The file is transient either way:
/// <see cref="TtsSegmentSource"/> moves it into its own final cache location (F70.4's identical
/// normalize → measure → cache pipeline).
///
/// No boot-frozen <see cref="HttpClient.BaseAddress"/>, same discipline as every other engine
/// client (SPEC F36.1–F36.2): the endpoint comes from the profile per call, itself resolved from
/// <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> upstream.
/// </summary>
public sealed class KokoroFallbackRenderer(
    HttpClient http,
    IOptionsMonitor<TtsOptions> ttsOptions) : IFallbackProfileRenderer
{
    public string Engine => DependencyNames.Kokoro;

    public async Task<string> RenderAsync(TtsFallbackProfile profile, string text, string requestVoice, CancellationToken ct)
    {
        var cfg = ttsOptions.CurrentValue;
        var voice = string.IsNullOrEmpty(profile.Voice) ? requestVoice : profile.Voice;
        var body = new { input = text, voice, response_format = cfg.Format };
        using var content = new StringContent(
            JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var requestUri = EndpointUri.Combine(profile.Endpoint, "/v1/audio/speech");
        var response = await http.PostAsync(requestUri, content, ct);
        response.EnsureSuccessStatusCode();   // throws HttpRequestException on non-2xx

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var path = GetCachePath(text, voice, profile.Endpoint, cfg);

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

    static string GetCachePath(string text, string voice, string endpoint, TtsOptions cfg)
    {
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(text + "|" + voice + "|" + endpoint)));
        return Path.Combine(cfg.CacheRoot, "fallback-kokoro", $"{hash}.{cfg.Format}");
    }
}
