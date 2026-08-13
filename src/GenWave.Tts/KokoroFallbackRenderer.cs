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
/// actually renders it — primary or fallback hop. As of T140 (SPEC F98.2) this hop ALSO reads
/// <see cref="TtsRenderContext.Pace"/> the same way, so a fallback hop never renders a persona's
/// line at the wrong rate (the same class of primary/fallback parity gap T137 closed for
/// pronunciation). <see cref="TtsSegmentSource"/> has already validated the value before it ever
/// reaches this context (<see cref="TtsPace.Clamp"/>), so it is sent on the wire unchanged.
/// </summary>
public sealed class KokoroFallbackRenderer(
    HttpClient http,
    IOptionsMonitor<TtsOptions> ttsOptions,
    // Optional, defaulted — same "production DI always wires it, no existing test construction site
    // needs to change" posture as KokoroTtsSynthesizer's own PronunciationRuleHitReporter parameter;
    // see its remarks for the full rationale.
    PronunciationRuleHitReporter? ruleHits = null) : IFallbackProfileRenderer
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
        // resolves a rule set of its own from any provider or ambient accessor. The out-matches
        // overload (SPEC F97.5) reports exactly which rules fired, through the same
        // PronunciationRuleHitReporter the primary uses — NOT identically, though: this hop only
        // ever runs a render the PRIMARY already failed (FallbackTtsSynthesizer's own routing), so
        // "identical reporting" cannot mean "both engines report the same fired hit" — it means
        // each reports its OWN successful render, and only the one that actually airs ever does
        // (see the ordering note beside the call below).
        var speech = KokoroSpeechMarkup.Render(
            context.Text, PronunciationRuleSet.FromContext(context.Rules), cfg.SentencePauseSeconds, out var matches);
        // speed (SPEC F98.1-F98.2, PLAN T140): same field, same already-validated value the primary
        // sends — see this class's own remarks on why no clamping happens here either.
        var body = new { input = speech, voice, response_format = cfg.Format, speed = context.Pace };
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

        // ONLY now — after THIS hop's own engine accepted the render AND the audio landed on disk
        // (review finding, PLAN T142; mirrors KokoroTtsSynthesizer's identical ordering note).
        // Reporting any earlier would double-count the one line that airs: the primary composes the
        // same markup, reports, THEN its own POST fails and FallbackTtsSynthesizer retries here,
        // where this hop composes the identical markup again and would report again for the one
        // line that ultimately airs once. See ScenarioAFiredHitIsNeverDoubleCounted (Story253) for
        // the probe. context.IsAudition (PLAN T274) rides through unchanged, same parity discipline
        // as context.Rules (T137) and context.Pace (T140): an audition that falls over onto a
        // fallback hop must never count a hit here either — a preview reaches THIS renderer whenever
        // the primary fails mid-audition, and without this the fallback hop would silently re-enable
        // the exact counting KokoroTtsSynthesizer's own isAudition gate just excluded.
        ruleHits?.Report(matches, context.Kind, context.IsAudition);

        return path;
    }
}
