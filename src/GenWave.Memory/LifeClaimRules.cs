using System.Text.RegularExpressions;

namespace GenWave.Memory;

/// <summary>
/// The code-side checks on a claim the extractor model returned (STORY-537, PLAN T704a). The model is asked for
/// only new, concrete, non-music details but is not trusted to obey, so every claim passes these before it is stored.
/// Pure: no clock, no store. Words are compared as <see cref="RepetitionText.Tokens"/> (lowercase, punctuation split).
/// <para>
/// Rules, in order: (1) music and listening words reject as <see cref="LifeClaimRejection.TrackTalk"/>; (2) fewer than
/// <see cref="MinWords"/> words, or only bare things ("pet", "car"), reject as vague; (3) a claim that gives a name
/// already given in a known row or an earlier claim of the reply rejects as a duplicate: the name is taken, so the claim
/// is either the same fact reworded or a contradiction ("a dog named Rebel" vs "a beagle named Rebel"). A name is the
/// word after "named" or "called", or a capitalised word right after a pet or person noun ("my beagle Gus"); never a
/// roster persona's name, a weekday or a month; compared case-insensitively; (4) content-word overlap (Jaccard over crude stems) of at least <see cref="NearDuplicateOverlap"/>
/// with a known row rejects as a near-duplicate.
/// </para>
/// <para>
/// Known costs, accepted: the music words also drop true life details ("played hockey as a kid", "heard a fox in the
/// yard", "her sister is an artist", "goes to spin class", "tunes up his bike"); a two-word claim ("loves hiking")
/// counts as vague; and stemming is crude ("ing", "ed", "es", "s" only). A name given any other way ("a pony, Pickle")
/// is not caught, so that pet can be invented twice.
/// </para>
/// </summary>
public static partial class LifeClaimRules
{
    /// <summary>A claim of fewer words than this is vague ("has a pet" is three words and caught by the possession rule as well).</summary>
    public const int MinWords = 3;

    /// <summary>Two claims whose content words overlap at least this much (Jaccard) say the same thing.</summary>
    public const double NearDuplicateOverlap = 0.6;

    // Grammar glue is the repetition check's stop words, so the two lists cannot drift, plus the possession and
    // naming verbs and the determiners/relatives that are never the detail itself.
    static readonly HashSet<string> Glue = new(RepetitionCheck.StopWords, StringComparer.Ordinal)
    {
        "his", "her", "their", "some", "owns", "own", "owned", "got", "being", "since", "named", "called", "who",
    };

    static readonly string[] StemSuffixes = ["ing", "ed", "es", "s"];

    // A possession or place with no name, kind or detail: "has a pet", "owns a vehicle", "has a playlist".
    static readonly HashSet<string> BareThings = new(StringComparer.Ordinal)
    {
        "pet", "pets", "animal", "animals", "vehicle", "vehicles", "car", "cars", "truck", "bike", "playlist", "playlists",
        "hobby", "hobbies", "habit", "habits", "family", "friend", "friends", "relative", "relatives", "house", "home",
        "place", "job", "routine", "thing", "things", "life", "collection",
    };

    // Music and listening talk. "plays" and "play" stay allowed ("plays the banjo"); the past tense "played" is
    // a track play ("played Bohemian Rhapsody"). "records" and "vinyl" stay allowed ("collects vinyl records").
    static readonly HashSet<string> TrackWords = new(StringComparer.Ordinal)
    {
        "listen", "listens", "listened", "listening", "heard", "hearing", "spin", "spins", "spinning", "spun",
        "played", "song", "songs", "track", "tracks", "album", "albums", "playlist", "playlists",
        "artist", "artists", "tune", "tunes", "lyrics", "setlist",
    };

    // Nouns a capitalised word may name directly: "my beagle Gus", "his sister Karen".
    static readonly HashSet<string> NameCarriers = new(StringComparer.Ordinal)
    {
        "dog", "dogs", "cat", "cats", "beagle", "puppy", "kitten", "parrot", "bird", "horse", "tortoise", "rabbit",
        "hamster", "fish", "son", "daughter", "wife", "husband", "partner", "sister", "brother", "mom", "mum", "dad",
        "grandma", "grandpa", "aunt", "uncle", "cousin", "niece", "nephew", "friend", "neighbour", "neighbor",
    };

    static readonly HashSet<string> CalendarNames = new(StringComparer.Ordinal)
    {
        "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday",
        "january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december",
    };

    static readonly HashSet<string> NamingWords = new(StringComparer.Ordinal) { "named", "called" };

    [GeneratedRegex(@"\p{L}[\p{L}']*")]
    private static partial Regex Word();

    /// <summary>
    /// Why <paramref name="claim"/> must not be stored, or null when it passes. <paramref name="known"/> are the
    /// persona's existing life rows (forgotten included) and the claims already accepted from the same reply.
    /// </summary>
    public static LifeClaimRejection? Reject(string claim, LifeKnownClaims known)
    {
        var tokens = RepetitionText.Tokens(claim);
        if (tokens.Any(TrackWords.Contains))
            return LifeClaimRejection.TrackTalk;

        var details = tokens.Where(t => !Glue.Contains(t)).ToList();
        if (tokens.Length < MinWords || details.All(BareThings.Contains))
            return LifeClaimRejection.Vague;

        if (GivenNames(claim, known.RosterWords).Any(known.Names.Contains))
            return LifeClaimRejection.NearDuplicate;

        var stems = Stems(details);
        return known.StemSets.Any(k => Overlap(stems, k) >= NearDuplicateOverlap)
            ? LifeClaimRejection.NearDuplicate
            : null;
    }

    internal static IReadOnlySet<string> ContentStems(string claim) =>
        Stems(RepetitionText.Tokens(claim).Where(t => !Glue.Contains(t)));

    /// <summary>
    /// The names a claim gives: the word after "named"/"called", and a capitalised word right after a pet or person
    /// noun ("my beagle Gus"). Never a word of a roster persona's name (<paramref name="rosterWords"/>, lowercase),
    /// a weekday or a month.
    /// </summary>
    internal static IEnumerable<string> GivenNames(string claim, IReadOnlySet<string> rosterWords)
    {
        var words = Word().Matches(claim).Select(m => m.Value).ToArray();
        for (var i = 0; i + 1 < words.Length; i++)
        {
            var before = words[i].ToLowerInvariant();
            var next = words[i + 1];
            var isName = NamingWords.Contains(before) && !Glue.Contains(next.ToLowerInvariant())
                || NameCarriers.Contains(before) && char.IsUpper(next[0]);
            if (!isName)
                continue;

            var name = string.Concat(RepetitionText.Tokens(next));
            if (name.Length > 0 && !rosterWords.Contains(name) && !CalendarNames.Contains(name))
                yield return name;
        }
    }

    static HashSet<string> Stems(IEnumerable<string> details) => details.Select(Stem).ToHashSet(StringComparer.Ordinal);

    // Crude on purpose: "collecting", "collects" and "collected" meet at "collect".
    static string Stem(string word)
    {
        foreach (var suffix in StemSuffixes)
            if (word.Length > suffix.Length + 3 && word.EndsWith(suffix, StringComparison.Ordinal))
                return word[..^suffix.Length];
        return word;
    }

    static double Overlap(IReadOnlySet<string> a, IReadOnlySet<string> b)
    {
        var shared = a.Count(b.Contains);
        var union = a.Count + b.Count - shared;
        return union == 0 ? 0 : (double)shared / union;
    }
}
