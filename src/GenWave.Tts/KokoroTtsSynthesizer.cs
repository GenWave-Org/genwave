namespace GenWave.Tts;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;

/// <summary>
/// No boot-frozen <see cref="HttpClient.BaseAddress"/> (SPEC F36.1–F36.2) — <c>Tts:Endpoint</c> is
/// read from <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> and an absolute URI is built per
/// call (<see cref="EndpointUri"/>), so a live PUT to <c>Tts:Endpoint</c> applies to the very next
/// render with no api restart.
/// </summary>
public sealed class KokoroTtsSynthesizer(HttpClient http, IOptionsMonitor<TtsOptions> optionsMonitor) : ITtsSynthesizer
{
    public async Task<string> SynthesizeAsync(string text, string voice, CancellationToken ct)
    {
        var cfg = optionsMonitor.CurrentValue;

        // Engine-aware speech markup (gh-#116 pauses + SPEC F97 pronunciation, composed by
        // KokoroSpeechMarkup): applied HERE, at Kokoro request build — below the
        // NormalizingTtsSynthesizer chokepoint, so normalized text and every upstream cache key
        // stay byte-identical — and never on the Piper path, which would speak either markup form
        // aloud. No caller can supply a resolved PronunciationRuleSet yet — that wiring is T137
        // (resolving the rule set where the persona is known) — so Empty is passed here, which
        // keeps this path byte-identical to the pre-T133 pause-only behaviour until T137 lands.
        var speech = KokoroSpeechMarkup.Render(text, PronunciationRuleSet.Empty, cfg.SentencePauseSeconds);
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

    static string GetCachePath(string text, string voice, TtsOptions cfg)
    {
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(text + "|" + voice)));
        return Path.Combine(cfg.CacheRoot, $"{hash}.{cfg.Format}");
    }
}
