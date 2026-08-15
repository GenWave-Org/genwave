using System.ComponentModel.DataAnnotations;

namespace GenWave.Tts;

public sealed class TtsOptions
{
    public const string Section = "Tts";

    [Required, Url]
    public string Endpoint { get; set; } = "http://kokoro:8880";

    [Required]
    public string Format { get; set; } = "wav";

    [Range(1, int.MaxValue)]
    public int RenderBudgetSeconds { get; set; } = 30;

    [Required]
    public string CacheRoot { get; set; } = "/tts";

    /// <summary>GC horizon for fresh-per-airing blurb audio under <c>blurbs/</c> (SPEC F34.6).</summary>
    [Range(1, int.MaxValue)]
    public int BlurbRetentionHours { get; set; } = 24;

    /// <summary>
    /// Digital-silence pause appended after each sentence on the KOKORO request path only
    /// (gh-#116): <see cref="KokoroTtsSynthesizer"/> and <see cref="KokoroFallbackRenderer"/>
    /// append <c>[pause:Ns]</c> markup that kokoro-fastapi v0.6.0 honors as true silence — see
    /// <see cref="KokoroPauseMarkup"/> for the exact insertion contract and why Piper hops must
    /// never see a tag (piper-tts speaks it aloud). 0 disables insertion.
    /// </summary>
    [Range(0.0, 5.0)]
    public double SentencePauseSeconds { get; set; } = 0.6;

    /// <summary>
    /// Piper's base URL when Piper is the PRIMARY engine, not merely a fallback hop (SPEC F99.4,
    /// STORY-257) — the piper-only topology's opt-in path (<c>compose.piper-only.yaml</c> sets
    /// this to <c>http://piper:5000</c>). Null/empty (the default, every other topology) means
    /// Kokoro is primary via <see cref="Endpoint"/> above, unchanged.
    ///
    /// Deliberately a SEPARATE key from <see cref="Endpoint"/>, never a re-point of it:
    /// <see cref="Endpoint"/> stays whatever <c>KokoroHealthProbe</c>/<c>KokoroVoiceLister</c>/
    /// <see cref="KokoroTtsSynthesizer"/> already read it as — on the piper-only topology that is
    /// deliberately the absent kokoro host (DEPLOYMENT.md's "expected on every piper-only box"
    /// note) — so those Kokoro-shaped probes are never fed a Piper response they would misread as
    /// healthy. That risk is real, not theoretical: <c>piper/server.py</c>'s <c>do_GET</c>
    /// answers ANY path with 200 (by design, so nothing GenWave GETs from it ever 404s), so a
    /// <c>KokoroHealthProbe</c> repointed at Piper would see a 200 from <c>GET /health</c> and
    /// report Kokoro falsely healthy — worse than the honest "unhealthy" the dead-hostname
    /// posture reports today. Deploy-time only, chosen once at DI composition
    /// (<see cref="TtsServiceCollectionExtensions"/>) — never live-editable through the settings
    /// API, unlike every other <c>Tts:*</c> leaf: swapping wire protocol mid-render is a different
    /// topology, not a live reroute.
    /// </summary>
    /// <remarks>
    /// T148 review finding F5: this was <c>[Url]</c>, which rejects <c>""</c> — boot-crashing the
    /// very default this property's own remarks above document as legal. Empty/null and an
    /// absolute http/https URL are the only two legal shapes; see
    /// <see cref="AbsoluteHttpUrlOrEmptyAttribute"/>.
    /// </remarks>
    [AbsoluteHttpUrlOrEmpty]
    public string? PiperPrimaryEndpoint { get; set; }
}
