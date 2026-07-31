namespace GenWave.Tts;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using GenWave.Core.Domain;

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
///
/// <see cref="TtsRenderContext"/> (T134) now carries a resolved rule set and pace, but neither
/// reaches THIS render — <see cref="IFallbackProfileRenderer.RenderAsync"/>'s contract is
/// <c>(profile, text, requestVoice, ct)</c>, with no context parameter at all, so this hop keeps
/// passing <see cref="PronunciationRuleSet.Empty"/> and sending no <c>speed</c> field, byte-identical
/// to its pre-T134 behaviour. Widening that contract so a fallback hop can read either fact is
/// later work, not this task's.
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

        // Engine-aware sentence pauses AND pronunciation markup (gh-#116, F97): a kokoro-KIND hop
        // shares the same composed KokoroSpeechMarkup.Render seam as the primary
        // (KokoroTtsSynthesizer), so both Kokoro request paths tag identically — Piper hops
        // (PiperTtsSynthesizer) never do. Empty here, unlike the primary's now-context-aware
        // overload (T134): this hop has no TtsRenderContext to read a resolved rule set from (see
        // the class remarks), not merely an un-populated one.
        var speech = KokoroSpeechMarkup.Render(text, PronunciationRuleSet.Empty, cfg.SentencePauseSeconds);
        // No `speed` field either, for the same reason — this hop never sees TtsRenderContext.Pace.
        var body = new { input = speech, voice, response_format = cfg.Format };
        using var content = new StringContent(
            JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var requestUri = EndpointUri.Combine(profile.Endpoint, "/v1/audio/speech");
        var response = await http.PostAsync(requestUri, content, ct);
        response.EnsureSuccessStatusCode();   // throws HttpRequestException on non-2xx

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        // Tagged speech in the hash, mirroring KokoroTtsSynthesizer: what was rendered is what
        // names the transient file (see that class's remark; the file is moved or deleted by the
        // caller either way).
        var path = GetCachePath(speech, voice, profile.Endpoint, cfg);

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

    // No pace term: this hop never resolves a real pace value to fold in (see the class remarks)
    // — every render here is the same "engine default" constant, so adding it would be a no-op
    // key change, not a correctness fix. T140 is where that stops being true on the PRIMARY path;
    // this one needs IFallbackProfileRenderer widened first (docs/PLAN.md T140 precondition).
    static string GetCachePath(string text, string voice, string endpoint, TtsOptions cfg)
    {
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(text + "|" + voice + "|" + endpoint)));
        return Path.Combine(cfg.CacheRoot, "fallback-kokoro", $"{hash}.{cfg.Format}");
    }
}
