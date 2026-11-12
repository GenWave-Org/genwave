using GenWave.Core;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Tts;

/// <summary>
/// SPEC F218.35 — the "trim first" rule for a copy that repeats. Cuts the repeated sentences and says whether
/// what is left is fit to air. Pure; the truth gate comes in as a delegate so this stays out of the ladder's state.
/// </summary>
internal static class RepeatTrim
{
    /// <summary>Fewer words than this is not a break worth airing; ask again instead.</summary>
    public const int MinWords = 8;

    /// <summary>
    /// The trimmed copy, or <see langword="null"/> when the caller must re-ask: no trimmer, nothing dropped,
    /// nothing or under <see cref="MinWords"/> words left, the track named before the cut and not after it
    /// (<paramref name="trackTitle"/> or <paramref name="trackArtist"/>, possessive-insensitive), or the remainder
    /// not clearing <paramref name="clears"/> (the truth gate and the repeat check).
    /// </summary>
    public static string? Remainder(
        ISentenceTrimmer? trimmer, string copy, IReadOnlyList<string> phrases,
        string? trackTitle, string? trackArtist, Func<string, bool> clears)
    {
        if (Cut(trimmer, copy, phrases) is not { } text || WordCount(text) < MinWords)
            return null;
        if (NamesTrack(copy, trackTitle, trackArtist) && !NamesTrack(text, trackTitle, trackArtist))
            return null;
        return clears(text) ? text : null;
    }

    /// <summary>
    /// The bare cut every trim shares: <paramref name="copy"/> with the sentences matching <paramref name="phrases"/>
    /// removed, trimmed; <see langword="null"/> with no trimmer, no phrases, or nothing dropped. No fitness rules.
    /// </summary>
    public static string? Cut(ISentenceTrimmer? trimmer, string copy, IReadOnlyList<string> phrases)
    {
        if (trimmer is null || phrases.Count == 0)
            return null;

        var result = trimmer.Trim(copy, phrases.Select(p => new RepetitionHit(p)).ToList());
        return result.DroppedCount == 0 ? null : result.Text.Trim();
    }

    static int WordCount(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Count(w => w.Any(char.IsLetterOrDigit));

    static bool NamesTrack(string text, string? title, string? artist) =>
        WordTokens.ContainsPhrase(text, title) || WordTokens.ContainsPhrase(text, artist);
}
