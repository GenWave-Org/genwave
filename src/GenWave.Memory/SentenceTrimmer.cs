using GenWave.Core;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Memory;

/// <summary>
/// Default <see cref="ISentenceTrimmer"/> (SPEC F218.35). Pure. Sentences come from the shared
/// <see cref="SentenceBoundaries"/> rule (no cut at "Dr.", "33.3", "B.B."). A hit is its phrase's tokens as a
/// contiguous run in the whole copy's token stream (<see cref="RepetitionText.Tokens"/> on both sides, never a raw
/// substring); every sentence the run overlaps is dropped, so a hit crossing a boundary takes both sentences.
/// </summary>
public sealed class SentenceTrimmer : ISentenceTrimmer
{
    public TrimResult Trim(string copy, IReadOnlyList<RepetitionHit> hits)
    {
        var phrases = hits
            .Select(h => RepetitionText.Tokens(h.Phrase))
            .Where(p => p.Length > 0)
            .ToList();
        if (phrases.Count == 0)
            return new TrimResult(copy, 0);

        var sentences = Split(copy);
        var tokens = new List<string>();
        var owner = new List<int>();
        for (var i = 0; i < sentences.Count; i++)
            foreach (var token in RepetitionText.Tokens(sentences[i]))
            {
                tokens.Add(token);
                owner.Add(i);
            }

        var hit = new bool[sentences.Count];
        var stream = tokens.ToArray();
        foreach (var phrase in phrases)
            for (var start = 0; start + phrase.Length <= stream.Length; start++)
                if (stream.AsSpan(start, phrase.Length).SequenceEqual(phrase))
                    for (var t = start; t < start + phrase.Length; t++)
                        hit[owner[t]] = true;

        var dropped = hit.Count(h => h);
        if (dropped == 0)
            return new TrimResult(copy, 0);
        var kept = sentences.Where((_, i) => !hit[i]);
        return new TrimResult(string.Join(' ', kept), dropped);
    }

    static List<string> Split(string copy)
    {
        var sentences = new List<string>();
        var from = 0;
        foreach (var cutAt in SentenceBoundaries.CutPoints(copy, spoken: true).Append(copy.Length))
        {
            if (cutAt <= from)
                continue;
            var sentence = copy[from..cutAt].Trim();
            if (sentence.Length > 0)
                sentences.Add(sentence);
            from = cutAt;
        }
        return sentences;
    }
}
