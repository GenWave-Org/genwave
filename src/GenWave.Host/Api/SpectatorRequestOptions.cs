namespace GenWave.Host.Api;

/// <summary>
/// The body of <c>GET /spectator/api/request-options</c> (gh-#131): exactly the two pick lists the
/// spectator request form's dropdowns render, and nothing else. Design ruling (gh-#131):
/// genre-granularity disclosure ONLY — the genres are the case-insensitively distinct genres of
/// request-eligible catalog rows (safe-scope excluded, operator vetoes honored — the probe's own
/// law scoping), never track titles or artists, so this payload reveals nothing a fulfilled request
/// would not already put on air. Moods are <c>MoodVocabulary.Terms</c> verbatim — a fixed public
/// vocabulary, not catalog data.
/// </summary>
/// <param name="Genres">Distinct requestable genres, canonical casing, ordered case-insensitively;
/// empty when no eligible row carries a genre.</param>
/// <param name="Moods"><c>MoodVocabulary.Terms</c>, verbatim and in vocabulary order.</param>
public sealed record SpectatorRequestOptions(IReadOnlyList<string> Genres, IReadOnlyList<string> Moods);
