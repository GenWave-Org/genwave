namespace GenWave.Tts;

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using GenWave.Core.Domain;

/// <summary>
/// Piper-engine hop renderer (SPEC F70.1, STORY-190, gh-#147) — the <c>"piper"</c>
/// <see cref="IFallbackProfileRenderer"/> the chain executor <see cref="FallbackTtsSynthesizer"/>
/// resolves for piper-kind hops (including the implicit legacy single-hop chain the flat
/// <c>Tts:Fallback:Endpoint</c>/<c>Voice</c> keys form). Targets the upstream
/// <c>piper.http_server</c> HTTP wrapper the compose <c>piper</c> service runs: a single POST of
/// the already-normalized, <see cref="PiperSpeechMarkup"/>-stripped text (SPEC F96.3) to the hop
/// endpoint's root path returns raw WAV bytes.
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

    public async Task<string> RenderAsync(TtsFallbackProfile profile, TtsRenderContext context, CancellationToken ct)
    {
        var ttsCfg = ttsOptions.CurrentValue;
        var requestVoice = context.Voice;

        // Defense-in-depth strip guard (F96.3): piper-tts speaks any [...]-shaped token aloud
        // (a pause tag, a pronunciation override, or any other bracket-shaped form), and an
        // operator's correction replacement or an authored segment can carry brackets no
        // LLM-copy filter ever saw. See PiperSpeechMarkup for the full contract. Piper has no
        // markup mechanism of its own, so context.Rules (T137, SPEC F97.6) never reaches this
        // engine either — mirrors the pre-T137 shape exactly, mechanical widening only.
        var speech = PiperSpeechMarkup.Strip(context.Text);

        // CodeQL cs/web/xss reports HIGH here (alert #25, first raised on PR #325). It is a
        // MISCLASSIFIED SINK, not a false flow — the distinction matters, so the evidence is
        // recorded rather than re-derived next time:
        //
        //   * The flow is REAL. All four traced paths start at an operator's HTTP request body
        //     (SafeSegmentsController.Text, TtsPreviewController.Text) and end at this line.
        //     That is not a defect — it is what a TTS engine IS. Text arrives, text gets spoken.
        //   * The SINK is wrong. cs/web/xss means "written to a web page". This is an OUTBOUND
        //     request body: text/plain, to a fixed configured endpoint on the internal `core`
        //     network. No HTTP response, no rendering, no browser. RenderAsync returns a file
        //     path; the text becomes WAV bytes and is never echoed to any client.
        //   * Both sources are [AdminSurface] + [Authorize(Policy = Operator)], and the appliance
        //     topology gives the admin plane no public route at all (DEPLOYMENT.md).
        //
        // Why it appeared when it did: nothing about the data path changed. gh-#161's markup work
        // routed Strip through the shared SpeechText.CollapseWhitespace, and THAT call is the
        // dataflow edge CodeQL needed to connect controller to sink (confirmed in the analysis
        // SARIF — every one of the four flows passes through PiperSpeechMarkup's call to it).
        //
        // Not suppressed inline: CodeQL suppression comments are not honoured by code scanning's
        // default setup (github/codeql#9298), so a `// codeql[...]` marker here would be a comment
        // that silently does nothing. The alert is dismissed in the Security tab; this note exists
        // so the reasoning lives in git next to the code rather than only in that UI state.
        using var content = new StringContent(speech, Encoding.UTF8, "text/plain");
        var requestUri = EndpointUri.Combine(profile.Endpoint, "/");
        var response = await http.PostAsync(requestUri, content, ct);
        response.EnsureSuccessStatusCode();   // throws HttpRequestException on non-2xx

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var path = GetCachePath(speech, requestVoice, ttsCfg);

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
    /// formula, so upgrade never orphans or double-renders a cached temp file. <paramref
    /// name="text"/> is the FINAL (post-<see cref="PiperSpeechMarkup.Strip"/>) text — what was
    /// actually sent — mirroring <see cref="KokoroTtsSynthesizer"/>'s own tagged-speech hash.
    /// Both files are transient either way — <see cref="TtsSegmentSource"/> moves this path to
    /// its own final cache location (SPEC F70.4: the identical downstream measure/cue/cache
    /// pipeline), and <see cref="SafeSegmentAuthor"/> deletes it once the mixed artifact exists.
    /// </summary>
    static string GetCachePath(string text, string voice, TtsOptions cfg)
    {
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(text + "|" + voice)));
        return Path.Combine(cfg.CacheRoot, "piper", $"{hash}.{cfg.Format}");
    }
}
