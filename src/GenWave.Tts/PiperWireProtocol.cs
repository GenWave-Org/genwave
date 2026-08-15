namespace GenWave.Tts;

using System.Text;
using GenWave.Core.Domain;

/// <summary>
/// Shared "speak text against <c>piper.http_server</c>" wire mechanics (SPEC F70.1, F99.4) — the
/// single POST-text/plain-get-WAV protocol both a piper FALLBACK hop
/// (<see cref="PiperTtsSynthesizer"/>, endpoint from an operator's <see cref="TtsFallbackProfile"/>)
/// and Piper as the PRIMARY engine (<see cref="PiperPrimaryTtsSynthesizer"/>, endpoint from
/// <see cref="TtsOptions.PiperPrimaryEndpoint"/>) speak identically — only WHERE the endpoint comes
/// from differs between the two callers, never the wire shape itself. Extracted so that difference
/// never drifts into two copies of the same POST/strip/write logic (SPEC F96.3's markup-strip guard
/// included).
/// </summary>
static class PiperWireProtocol
{
    public static async Task<string> RenderAsync(
        HttpClient http, string endpoint, TtsRenderContext context, TtsOptions ttsCfg, CancellationToken ct)
    {
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
        //     network. No HTTP response, no rendering, no browser. This method returns a file
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
        var requestUri = EndpointUri.Combine(endpoint, "/");
        var response = await http.PostAsync(requestUri, content, ct);
        response.EnsureSuccessStatusCode();   // throws HttpRequestException on non-2xx

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        // See TransientRenderPath's remarks for the full root cause and why this is never
        // content-addressed.
        var path = TransientRenderPath.For(ttsCfg, subfolder: "piper");

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
