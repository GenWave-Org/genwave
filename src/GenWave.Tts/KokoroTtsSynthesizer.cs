namespace GenWave.Tts;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using ContextPronunciationRule = GenWave.Core.Domain.PronunciationRule;

/// <summary>
/// No boot-frozen <see cref="HttpClient.BaseAddress"/> (SPEC F36.1–F36.2) — <c>Tts:Endpoint</c> is
/// read from <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> and an absolute URI is built per
/// call (<see cref="EndpointUri"/>), so a live PUT to <c>Tts:Endpoint</c> applies to the very next
/// render with no api restart.
/// </summary>
public sealed class KokoroTtsSynthesizer(HttpClient http, IOptionsMonitor<TtsOptions> optionsMonitor) : ITtsSynthesizer
{
    public Task<string> SynthesizeAsync(string text, string voice, CancellationToken ct) =>
        RenderAsync(text, voice, PronunciationRuleSet.Empty, ct);

    /// <summary>
    /// Context-aware overload (SPEC F70.3, F97.6): reads <see cref="TtsRenderContext.Rules"/> off
    /// the context now that it rides with the request, rather than the hardcoded "no rules" the
    /// plain overload above always used. A caller that constructs a context without setting Rules
    /// gets that exact same default (<see cref="TtsRenderContext.Rules"/>'s own default), so this
    /// override renders byte-identically to before this widening for every caller that has not
    /// opted in. No caller resolves a REAL rule set onto the context yet — that wiring is a later
    /// task's job (STORY-253); this override only reads whatever the context already carries.
    ///
    /// <see cref="TtsRenderContext.Pace"/> also rides the context (T134) but is deliberately NOT
    /// read here: folding it into the Kokoro <c>speed</c> field and both cache keys (the engine
    /// file cache below and <see cref="TtsSegmentSource"/>'s segment cache) is T140's job, not
    /// this override's — see <c>docs/PLAN.md</c> T140.
    /// </summary>
    public Task<string> SynthesizeAsync(TtsRenderContext context, CancellationToken ct) =>
        RenderAsync(context.Text, context.Voice, ToRuleSet(context.Rules), ct);

    async Task<string> RenderAsync(
        string text, string voice, PronunciationRuleSet rules, CancellationToken ct)
    {
        var cfg = optionsMonitor.CurrentValue;

        // Engine-aware speech markup (gh-#116 pauses + SPEC F97 pronunciation, composed by
        // KokoroSpeechMarkup): applied HERE, at Kokoro request build — below the
        // NormalizingTtsSynthesizer chokepoint, so normalized text and every upstream cache key
        // stay byte-identical — and never on the Piper path, which would speak either markup form
        // aloud.
        var speech = KokoroSpeechMarkup.Render(text, rules, cfg.SentencePauseSeconds);
        var body = new { input = speech, voice, response_format = cfg.Format };
        using var content = new StringContent(
            JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var requestUri = EndpointUri.Combine(cfg.Endpoint, "/v1/audio/speech");
        var response = await http.PostAsync(requestUri, content, ct);
        response.EnsureSuccessStatusCode();   // throws HttpRequestException on non-2xx

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        // Hashing the TAGGED speech (what was actually rendered), not the caller's text: two
        // renders under different pause settings are different audio and must never collide on a
        // transient file. Transient either way — TtsSegmentSource moves this file into its own
        // final cache slot (keyed on pre-synthesis copy text, tag-free), and TtsPreviewController
        // deletes it after streaming the bytes.
        var path = GetCachePath(speech, voice, cfg);

        // Path.GetDirectoryName always returns a non-null string when the path is produced
        // by Path.Combine with a non-empty CacheRoot; the guard below satisfies the compiler
        // without using the null-forgiving operator.
        var dir = Path.GetDirectoryName(path);
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllBytesAsync(path, bytes, ct);
        return path;
    }

    // The resolved-rule shape riding on TtsRenderContext (GenWave.Core.Domain.PronunciationRule)
    // cannot be the same type PronunciationRuleSet.Create compiles (GenWave.Core.Domain lives in
    // the zero-dependency MIT contract project, which cannot reference GenWave.Tts) — see
    // ContextPronunciationRule's own remarks. An empty list maps straight to
    // PronunciationRuleSet.Empty rather than paying for Create's own (equivalent) empty compile, so
    // the overwhelmingly common "no rules" case allocates nothing new.
    static PronunciationRuleSet ToRuleSet(IReadOnlyList<ContextPronunciationRule> rules) =>
        rules.Count == 0
            ? PronunciationRuleSet.Empty
            : PronunciationRuleSet.Create(rules.Select(rule => new PronunciationRule(rule.Pattern, rule.Word, rule.Ipa)));

    static string GetCachePath(string text, string voice, TtsOptions cfg)
    {
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(text + "|" + voice)));
        return Path.Combine(cfg.CacheRoot, $"{hash}.{cfg.Format}");
    }
}
