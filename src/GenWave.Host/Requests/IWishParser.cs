namespace GenWave.Host.Requests;

/// <summary>
/// Turns a listener's raw wish text into structured predicates (SPEC F87.4, STORY-225, PLAN T88).
/// Two implementations exist — <see cref="LlmWishParser"/> (Normal degradation mode, LLM-backed, the
/// mood-tagger prompt posture applied to free text) and <see cref="DeterministicWishParser"/>
/// (Soft/Hard mode, or the LLM unconfigured — F69 honored, zero calls either way).
/// <see cref="RequestParserService"/> picks between the two PER WISH, read fresh at parse time —
/// never cached across requests or across its own lifetime.
///
/// Never throws: an unparseable wish collapses to <see cref="ParsedWish.Empty"/> (or, for
/// <see cref="LlmWishParser"/>, delegates to <see cref="DeterministicWishParser"/> on a failed round
/// trip) — never an exception past this boundary, mirroring
/// <c>GenWave.MediaLibrary.Mood.OllamaMoodTagger</c>'s own "never throws" contract.
/// </summary>
interface IWishParser
{
    /// <summary>
    /// <paramref name="genreOptions"/> (gh-#131) is the live requestable-genre list
    /// (<c>IRequestCatalogProbe.ListRequestableGenresAsync</c>), fetched fresh per wish by
    /// <see cref="RequestParserService"/> — <see cref="DeterministicWishParser"/> recognizes a genre
    /// word only when it is a member (case-insensitive), and <see cref="LlmWishParser"/> forwards the
    /// same list to its own deterministic fallback. A wish parse never sees a stale list, and a
    /// picker-only request never triggers the fetch at all.
    /// </summary>
    Task<ParsedWish> ParseAsync(string wish, IReadOnlyList<string> genreOptions, CancellationToken ct);
}
