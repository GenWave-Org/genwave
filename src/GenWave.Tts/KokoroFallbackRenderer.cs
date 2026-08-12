namespace GenWave.Tts;

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
/// The transient write path (see <see cref="TransientRenderPath"/> for the shared helper and the
/// full root cause) is a fresh Guid per call under a <c>"fallback-kokoro"</c> subfolder — a hop
/// and the primary, or two kokoro-kind hops, must never collide on a transient file's name. The
/// file is transient either way: <see cref="TtsSegmentSource"/> moves it into its own final cache
/// location (F70.4's identical normalize → measure → cache pipeline).
///
/// No boot-frozen <see cref="HttpClient.BaseAddress"/>, same discipline as every other engine
/// client (SPEC F36.1–F36.2): the endpoint comes from the profile per call, itself resolved from
/// <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> upstream.
///
/// <see cref="TtsRenderContext"/> (T134) now reaches this render (T137 widened
/// <see cref="IFallbackProfileRenderer.RenderAsync"/> to carry it instead of a bare
/// <c>(text, requestVoice)</c> pair): a kokoro-kind hop reads <see cref="TtsRenderContext.Rules"/>
/// the identical way <see cref="KokoroTtsSynthesizer"/>'s own context-aware overload does (SPEC
/// F97.6), so the same DJ line carries the same pronunciation whichever Kokoro-kind renderer
/// actually renders it — primary or fallback hop. This hop still never reads
/// <see cref="TtsRenderContext.Pace"/>: that field's consumption is T140's job, not this one's — see
/// <c>docs/PLAN.md</c> T140.
/// </summary>
public sealed class KokoroFallbackRenderer(
    HttpClient http,
    IOptionsMonitor<TtsOptions> ttsOptions) : IFallbackProfileRenderer
{
    public string Engine => DependencyNames.Kokoro;

    public async Task<string> RenderAsync(TtsFallbackProfile profile, TtsRenderContext context, CancellationToken ct)
    {
        var cfg = ttsOptions.CurrentValue;
        var voice = string.IsNullOrEmpty(profile.Voice) ? context.Voice : profile.Voice;

        // Engine-aware sentence pauses AND pronunciation markup (gh-#116, F97): a kokoro-KIND hop
        // shares the same composed KokoroSpeechMarkup.Render seam as the primary
        // (KokoroTtsSynthesizer), so both Kokoro request paths tag identically — Piper hops
        // (PiperTtsSynthesizer) never do. context.Rules rides in from TtsSegmentSource (SPEC
        // F97.6) exactly like the primary's own context-aware overload reads it — this hop never
        // resolves a rule set of its own from any provider or ambient accessor.
        var speech = KokoroSpeechMarkup.Render(context.Text, PronunciationRuleSet.FromContext(context.Rules), cfg.SentencePauseSeconds);
        // No `speed` field: this hop never reads TtsRenderContext.Pace (T140's job).
        var body = new { input = speech, voice, response_format = cfg.Format };
        using var content = new StringContent(
            JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var requestUri = EndpointUri.Combine(profile.Endpoint, "/v1/audio/speech");
        var response = await http.PostAsync(requestUri, content, ct);
        response.EnsureSuccessStatusCode();   // throws HttpRequestException on non-2xx

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        // See TransientRenderPath's remarks for the full root cause and why this is never
        // content-addressed.
        var path = TransientRenderPath.For(cfg, subfolder: "fallback-kokoro");

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
}
