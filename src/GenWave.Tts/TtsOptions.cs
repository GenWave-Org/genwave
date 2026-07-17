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
}
