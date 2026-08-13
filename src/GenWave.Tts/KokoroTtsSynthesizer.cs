namespace GenWave.Tts;

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// No boot-frozen <see cref="HttpClient.BaseAddress"/> (SPEC F36.1–F36.2) — <c>Tts:Endpoint</c> is
/// read from <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> and an absolute URI is built per
/// call (<see cref="EndpointUri"/>), so a live PUT to <c>Tts:Endpoint</c> applies to the very next
/// render with no api restart.
/// </summary>
public sealed class KokoroTtsSynthesizer(
    HttpClient http,
    IOptionsMonitor<TtsOptions> optionsMonitor,
    // Optional, defaulted (not a required collaborator): production DI (TtsServiceCollectionExtensions)
    // always supplies the real PronunciationRuleHitReporter; a null default keeps every existing test
    // construction site (this class is built directly, with no fake, throughout GenWave.Tts.Tests)
    // compiling unchanged, mirroring TtsSegmentSource's own IStationEventSink? seam one class over.
    PronunciationRuleHitReporter? ruleHits = null) : ITtsSynthesizer
{
    public Task<string> SynthesizeAsync(string text, string voice, CancellationToken ct) =>
        RenderAsync(text, voice, PronunciationRuleSet.Empty, TtsPace.EngineDefault, null, isAudition: false, ct);

    /// <summary>
    /// Context-aware overload (SPEC F70.3, F97.6, F98.2): reads <see cref="TtsRenderContext.Rules"/>
    /// and <see cref="TtsRenderContext.Pace"/> off the context now that both ride with the request,
    /// rather than the hardcoded "no rules"/engine-default pace the plain overload above always
    /// used. A caller that constructs a context without setting either gets that exact same default
    /// (<see cref="TtsRenderContext.Rules"/>/<see cref="TtsRenderContext.Pace"/>'s own defaults), so
    /// this override renders byte-identically to before this widening for every caller that has not
    /// opted in. As of T137 (STORY-253, SPEC F97.6) and T140 (STORY-255, SPEC F98.2)
    /// <see cref="TtsSegmentSource"/> resolves a real merged rule set and a real, already-validated
    /// persona pace onto the context, so both paths are live.
    ///
    /// <see cref="TtsRenderContext.Pace"/> is sent on the wire unchanged, with no clamping here:
    /// <see cref="TtsSegmentSource"/> has already validated it (<see cref="TtsPace.Clamp"/>, run
    /// inside <see cref="ActivePersonaPaceCache"/>'s own refresh) before ever stamping it onto a
    /// context, so this adapter trusts the value exactly the way it already trusts
    /// <see cref="TtsRenderContext.Rules"/> — resolved once, upstream, never re-checked at the
    /// engine.
    /// </summary>
    public Task<string> SynthesizeAsync(TtsRenderContext context, CancellationToken ct) =>
        RenderAsync(
            context.Text, context.Voice, PronunciationRuleSet.FromContext(context.Rules), context.Pace,
            context.Kind, context.IsAudition, ct);

    async Task<string> RenderAsync(
        string text, string voice, PronunciationRuleSet rules, double pace, SegmentKind? kind, bool isAudition,
        CancellationToken ct)
    {
        var cfg = optionsMonitor.CurrentValue;

        // Engine-aware speech markup (gh-#116 pauses + SPEC F97 pronunciation, composed by
        // KokoroSpeechMarkup): applied HERE, at Kokoro request build — below the
        // NormalizingTtsSynthesizer chokepoint, so normalized text and every upstream cache key
        // stay byte-identical — and never on the Piper path, which would speak either markup form
        // aloud. The out-matches overload (SPEC F97.5) reports exactly which rules fired, with no
        // second PronunciationRuleSet.Match call over the same text; ruleHits is null only for a
        // caller that constructed this class directly with no PronunciationRuleHitReporter (test
        // fakes throughout GenWave.Tts.Tests), never in production (TtsServiceCollectionExtensions
        // always wires the real one).
        var speech = KokoroSpeechMarkup.Render(text, rules, cfg.SentencePauseSeconds, out var matches);
        // speed (SPEC F98.1-F98.2, PLAN T140): kokoro-fastapi's OpenAI-compatible speaking-rate
        // field. Always present, even at the engine default — see
        // Story255_DjsSpeakAtTheirOwnPace's The_default_pace_is_sent_as_the_engine_default.
        var body = new { input = speech, voice, response_format = cfg.Format, speed = pace };
        using var content = new StringContent(
            JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var requestUri = EndpointUri.Combine(cfg.Endpoint, "/v1/audio/speech");
        var response = await http.PostAsync(requestUri, content, ct);
        response.EnsureSuccessStatusCode();   // throws HttpRequestException on non-2xx

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        // See TransientRenderPath's remarks for the full root cause and why this is never
        // content-addressed.
        var path = TransientRenderPath.For(cfg);

        // Path.GetDirectoryName always returns a non-null string when the path is produced
        // by Path.Combine with a non-empty CacheRoot; the guard below satisfies the compiler
        // without using the null-forgiving operator.
        var dir = Path.GetDirectoryName(path);
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllBytesAsync(path, bytes, ct);

        // ONLY now — after the engine accepted this render AND the audio actually landed on disk
        // (review finding, PLAN T142): "fired" means "aired", never merely "the markup composer
        // found a match" or "the engine returned 2xx". Reporting any earlier double-counts the one
        // line that actually airs when this primary throws (here or at the write above) and a
        // fallback hop (KokoroFallbackRenderer) goes on to render the identical text successfully —
        // both would have reported, one aired — and would count a hit for a render that never airs
        // at all when the whole chain fails and the segment is dropped, including the narrower
        // sliver where the engine returns 2xx but the subsequent file write fails. See
        // ScenarioAFiredHitIsNeverDoubleCounted (Story253) for the failing-primary/succeeding-hop
        // probe this ordering exists to satisfy. isAudition (PLAN T274) still gates the report even
        // though this render genuinely landed on disk — an audition clip existing on disk briefly
        // is not the same fact as a rule airing (SPEC F97.5, F126.1).
        ruleHits?.Report(matches, kind, isAudition);

        return path;
    }
}
