namespace GenWave.Core.Abstractions;

/// <summary>
/// Classifies whether a track's title/artist indicate explicit content, via the configured LLM
/// endpoint (SPEC F95.3, STORY-251, T113) — the offline, second-tier batch counterpart to
/// <see cref="IMoodTagger"/>, one column pair later. This is the committed seam
/// <c>GenWave.MediaLibrary.Enrich.EnrichmentService</c>'s explicit-classification sweep consumes;
/// only one implementation ships today
/// (<c>GenWave.MediaLibrary.ExplicitClassification.OllamaExplicitClassifier</c>, an OpenAI-compatible
/// chat-completions client), mirroring where <see cref="IMoodTagger"/>/<c>OllamaMoodTagger</c> and
/// <see cref="IYearLookup"/>/<c>MusicBrainzYearLookup</c> sit relative to each other.
///
/// Never throws past this boundary for an ordinary miss (F95.3): returns <see langword="null"/>
/// whenever the round trip completes but the constrained-output parse produces neither a confident
/// "yes" nor "no" — including an explicit "unknown" answer or any wrong-shaped reply, both of which
/// are the SAME legal "can't tell from title/artist alone" outcome, mirroring <see cref="IYearLookup"/>'s
/// nullable return and <see cref="IMoodTagger"/>'s empty-list miss. An implementation MAY also
/// implement an optional, MediaLibrary-internal diagnostics seam (mirroring
/// <c>GenWave.MediaLibrary.YearLookup.IYearLookupDiagnostics</c> /
/// <c>GenWave.MediaLibrary.Mood.IMoodTaggerDiagnostics</c>) so a caller can distinguish that legal
/// miss from an endpoint-level failure (timeout, connect failure, non-2xx, malformed body) —
/// deliberately never folded into THIS committed contract, which stays as narrow as
/// <see cref="IYearLookup"/>'s own.
/// </summary>
public interface IExplicitClassifier
{
    /// <summary>
    /// Returns <see langword="true"/>/<see langword="false"/> for a confident yes/no verdict on
    /// whether <paramref name="artist"/>/<paramref name="title"/> indicate explicit content, or
    /// <see langword="null"/> for a miss — never an exception. Either argument may be null/blank;
    /// the classifier builds the best prompt context it can from whatever is present. Deliberately
    /// NEVER given a file path (gh-#174's lesson: the judgment call is about the title/artist text
    /// itself, never the file on disk).
    /// </summary>
    Task<bool?> ClassifyAsync(string? artist, string? title, CancellationToken ct);
}
