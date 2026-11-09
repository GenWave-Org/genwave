using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Memory;

/// <summary>
/// Default <see cref="IRepetitionCheck"/> (SPEC F218.11). Pure.
/// <list type="bullet">
/// <item>Utterance hit: a maximal run of at least <see cref="MinRunWords"/> consecutive words shared with a
/// recent text (lowercased, punctuation stripped), kept only if some four-word window of it holds enough fresh
/// words: <see cref="MinFreshWordsNearTrackFacts"/> when the window holds a track fact (title, artist, album or
/// year fact), else <see cref="MinFreshWords"/>. Fresh means not a stop word, not a persona/station/title/artist/album
/// word or its "s"/"ed" form (stop words get no forms), and not a year fact. A year fact is a year digit token, or a
/// year number word beside another year word or digit; a lone one ("this one is for you") is ordinary text.
/// A longer run is one hit, named by the whole run.</item>
/// <item>Quirk hit: the spent rule's key phrase (longest quoted span, else last four words). "At any length"
/// means no minimum on the phrase itself: the candidate must hold a contiguous, in-order run of
/// min(3, key-phrase words) of its words, so "banger, log it" hits "officially a banger, log it". Exclusions and
/// stop words do not apply; the hit names the phrase as written in the rule.</item>
/// </list>
/// </summary>
public sealed class RepetitionCheck : IRepetitionCheck
{
    /// <summary>Shortest shared run that counts as a repeat.</summary>
    public const int MinRunWords = 4;

    /// <summary>Fresh words a window with no track fact needs for its run to count.</summary>
    public const int MinFreshWords = 1;

    /// <summary>Fresh words a window holding a track fact needs; one is just a fact word plus filler.</summary>
    public const int MinFreshWordsNearTrackFacts = 2;

    /// <summary>Words of a quirk key phrase the candidate must reproduce in order.</summary>
    public const int QuirkRunWords = 3;

    // Articles, pronouns, prepositions, auxiliaries, and/or/but. Kept short on purpose.
    static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "i", "you", "he", "she", "it", "we", "they", "me", "my", "your", "our", "us", "this", "that",
        "of", "in", "on", "at", "to", "for", "with", "from", "by", "up", "as", "into",
        "is", "are", "was", "were", "be", "been", "am", "do", "does", "did", "have", "has", "had", "will", "can",
        "and", "or", "but",
    };

    public RepetitionResult Check(string copy, RepetitionContext context)
    {
        var candidate = RepetitionText.Tokens(copy);
        var hits = new List<RepetitionHit>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var excluded = ExclusionWords(context);
        var yearFacts = excluded.YearFacts(candidate);
        var runs = new List<Range>();
        foreach (var recent in context.RecentTexts)
            runs.AddRange(SharedRuns(candidate, RepetitionText.Tokens(recent), excluded, yearFacts));

        foreach (var range in OneHitPerStretch(runs))
        {
            var phrase = string.Join(' ', candidate[range]);
            if (seen.Add(phrase))
                hits.Add(new RepetitionHit(phrase));
        }

        foreach (var rule in context.SpentQuirkRules)
        {
            var key = RepetitionText.KeyPhrase(rule);
            if (key.Length > 0 && ContainsKeyRun(candidate, RepetitionText.Tokens(key)) && seen.Add("quirk:" + key))
                hits.Add(new RepetitionHit(key));
        }

        return hits.Count == 0 ? RepetitionResult.None : new RepetitionResult(hits);
    }

    // Longest first; a run inside a reported range is dropped, an overlapping one is merged into the union.
    static List<Range> OneHitPerStretch(List<Range> runs)
    {
        var reported = new List<Range>();
        foreach (var run in runs.OrderByDescending(r => r.End.Value - r.Start.Value).ThenBy(r => r.Start.Value))
        {
            if (reported.Any(r => r.Start.Value <= run.Start.Value && run.End.Value <= r.End.Value))
                continue;
            var start = run.Start.Value;
            var end = run.End.Value;
            foreach (var r in reported.Where(r => r.Start.Value < end && start < r.End.Value).ToList())
            {
                start = Math.Min(start, r.Start.Value);
                end = Math.Max(end, r.End.Value);
                reported.Remove(r);
            }
            reported.Add(start..end);
        }
        return reported.OrderBy(r => r.Start.Value).ToList();
    }

    static ExcludedWords ExclusionWords(RepetitionContext context)
    {
        return new ExcludedWords(
            StopWords,
            NameWords(context.PersonaName, context.StationName),
            NameWords(context.TrackTitle, context.Artist, context.Album),
            YearWords.For(context.Year));
    }

    // Stop words are dropped first so they never grow "s"/"ed" forms ("it" must not exclude "its").
    static HashSet<string> NameWords(params string?[] names)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
            words.UnionWith(RepetitionText.Tokens(name));
        words.ExceptWith(StopWords);
        return words;
    }

    static IEnumerable<Range> SharedRuns(string[] candidate, string[] recent, ExcludedWords excluded, bool[] yearFacts)
    {
        for (var i = 0; i < candidate.Length; i++)
            for (var j = 0; j < recent.Length; j++)
            {
                if (candidate[i] != recent[j] || (i > 0 && j > 0 && candidate[i - 1] == recent[j - 1]))
                    continue;
                var len = 0;
                while (i + len < candidate.Length && j + len < recent.Length && candidate[i + len] == recent[j + len])
                    len++;
                if (len >= MinRunWords && HasFreshWindow(candidate, i, len, excluded, yearFacts))
                    yield return i..(i + len);
            }
    }

    // The run survives if any four-word window has enough fresh words for what it holds.
    static bool HasFreshWindow(string[] candidate, int start, int length, ExcludedWords excluded, bool[] yearFacts)
    {
        for (var w = start; w + MinRunWords <= start + length; w++)
        {
            var fresh = 0;
            var nearTrackFacts = false;
            for (var p = w; p < w + MinRunWords; p++)
            {
                if (excluded.IsFresh(candidate[p], yearFacts[p]))
                    fresh++;
                nearTrackFacts |= excluded.IsTrackFact(candidate[p], yearFacts[p]);
            }
            if (fresh >= (nearTrackFacts ? MinFreshWordsNearTrackFacts : MinFreshWords))
                return true;
        }
        return false;
    }

    static bool ContainsKeyRun(string[] candidate, string[] key)
    {
        if (key.Length == 0)
            return false;
        var size = Math.Min(QuirkRunWords, key.Length);
        for (var k = 0; k + size <= key.Length; k++)
            for (var c = 0; c + size <= candidate.Length; c++)
                if (candidate.AsSpan(c, size).SequenceEqual(key.AsSpan(k, size)))
                    return true;
        return false;
    }
}
