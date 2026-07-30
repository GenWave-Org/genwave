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
}
