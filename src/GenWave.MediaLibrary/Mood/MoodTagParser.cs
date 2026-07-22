namespace GenWave.MediaLibrary.Mood;

using System.Text.RegularExpressions;
using GenWave.Core.Domain;

/// <summary>
/// The constrained-output parse (SPEC F85.4, STORY-216, T72): pure, no I/O — turns whatever raw text
/// an LLM completion returned into an already-filtered, already-bounded mood set, or an empty list
/// for a miss. Every <see cref="MoodVocabulary.Terms"/> entry is a single lowercase word (no spaces,
/// no hyphens — see that class's own remarks), so splitting the raw text on runs of non-letters and
/// lowercasing each token is enough to survive commas, newlines, quoted-list shapes, bullet points,
/// or a stray sentence wrapped around the answer (the "lenient-schema lesson" — a small local model
/// rarely returns clean JSON on the first try, so this parses prose, not a schema).
/// <para>
/// Unknown terms are silently dropped (never stored, F85.4); duplicates are dropped keeping the
/// FIRST occurrence's position (the model's own ordering is treated as a confidence ranking); the
/// result is truncated at <see cref="MoodVocabulary.MaxMoodsPerTrack"/>. Wrong-shaped output (no
/// vocabulary word appears anywhere in the text) and "fewer than one survivor" are the SAME outcome
/// here — an empty list — because both are a miss to the caller (never a partial write).
/// </para>
/// </summary>
static class MoodTagParser
{
    static readonly Regex TokenPattern = new("[a-z]+", RegexOptions.Compiled);

    public static IReadOnlyList<string> Parse(string raw)
    {
        var survivors = new List<string>();
        foreach (Match match in TokenPattern.Matches(raw.ToLowerInvariant()))
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
