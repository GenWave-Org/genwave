namespace GenWave.Host.Requests;

using System.Text.RegularExpressions;
using GenWave.Core.Domain;

/// <summary>
/// The F69-honoring half of <see cref="IWishParser"/> (SPEC F87.4, STORY-225, PLAN T88): a pure,
/// no-I/O regex/token scan over the wish text — no LLM call, ever. Used directly by
/// <see cref="RequestParserService"/> whenever the LLM path is unavailable this wish (Soft/Hard
/// degradation, or an unconfigured <c>Llm:Endpoint</c>), and internally by
/// <see cref="LlmWishParser"/> as its OWN failure fallback (never a second, different algorithm for
/// that case).
///
/// <para>
/// Conservative by design, mirroring F87.4's own "unparseable ⇒ empty predicates" contract: a
/// predicate this scan cannot confidently spot is left null/empty rather than guessed. Three
/// independent scans, each free to miss on its own:
/// </para>
/// <list type="bullet">
/// <item>a quoted <c>"..."</c> substring becomes the title candidate;</item>
/// <item>the tail after the LAST standalone <c>by</c> becomes the artist candidate (a listener
/// naturally phrases "play some jazz by Miles Davis" — taking the tail, not the first match, means a
/// wish with an earlier incidental "by" still reads the artist correctly);</item>
/// <item>every case-insensitive token that is a literal, exact <see cref="MoodVocabulary"/> word is
/// collected, deduplicated, and capped at <see cref="MoodVocabulary.MaxMoodsPerTrack"/> — the exact
/// filtering discipline <c>GenWave.MediaLibrary.Mood.MoodTagParser</c> already established for the
/// catalog's own tagger.
/// </item>
/// </list>
///
/// Never throws — the public <see cref="Parse"/> method is pure and synchronous, safe to unit test
/// directly with no async ceremony; <see cref="ParseAsync"/> is a thin <see cref="IWishParser"/>
/// wrapper around it.
/// </summary>
sealed class DeterministicWishParser : IWishParser
{
    static readonly Regex QuotedTitlePattern = new("\"([^\"]+)\"", RegexOptions.Compiled);
    static readonly Regex ByArtistPattern = new(@"\bby\s+(.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex WordPattern = new("[a-z]+", RegexOptions.Compiled);

    public Task<ParsedWish> ParseAsync(string wish, CancellationToken ct) => Task.FromResult(Parse(wish));

    /// <summary>The pure parse — see the class remarks for the three independent scans.</summary>
    public static ParsedWish Parse(string wish)
    {
        if (string.IsNullOrWhiteSpace(wish)) return ParsedWish.Empty;

        var title = ExtractTitle(wish);
        var artist = ExtractArtist(wish);
        var moods = ExtractMoods(wish);

        return new ParsedWish(artist, title, moods);
    }

    static string? ExtractTitle(string wish)
    {
        var match = QuotedTitlePattern.Match(wish);
        if (!match.Success) return null;

        var candidate = match.Groups[1].Value.Trim();
        return candidate.Length == 0 ? null : candidate;
    }

    static string? ExtractArtist(string wish)
    {
        var match = ByArtistPattern.Match(wish);
        if (!match.Success) return null;

        // Strip a trailing/embedded quoted fragment and stray punctuation the greedy tail may have
        // swept up (e.g. a title-then-artist ordering the quoted-title scan already claimed above).
        var candidate = QuotedTitlePattern.Replace(match.Groups[1].Value, string.Empty)
            .Trim()
            .TrimEnd('.', '!', '?', ',');
        return candidate.Length == 0 ? null : candidate;
    }

    static IReadOnlyList<string> ExtractMoods(string wish)
    {
        var survivors = new List<string>();
        foreach (Match match in WordPattern.Matches(wish.ToLowerInvariant()))
        {
            var term = match.Value;
            if (!MoodVocabulary.Contains(term)) continue;
            if (survivors.Contains(term)) continue;

            survivors.Add(term);
            if (survivors.Count == MoodVocabulary.MaxMoodsPerTrack) break;
        }

        return survivors;
    }
}
