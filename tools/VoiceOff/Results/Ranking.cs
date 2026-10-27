using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Serving;

namespace GenWave.VoiceOff.Score;

/// <summary>Puts the rendered cards of a run in the judge's order (SPEC F217.8). Pure: no files, no clock.</summary>
internal static class Ranking
{
    /// <summary>
    /// The cards that rendered, best first. The judgement order comes first (an id the run lacks, did not render or repeats is skipped);
    /// the cards it does not name follow in id order. Ranks count from 1.
    /// </summary>
    internal static IReadOnlyList<RankedCard> Of(RunManifest manifest, Judgement judgement)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(judgement);

        IEnumerable<string> named = judgement.Order.Where(id => IsRendered(manifest, id)).Distinct(StringComparer.Ordinal);
        IEnumerable<string> unnamed = manifest.Cards
            .Where(c => c.Value.Rendered)
            .Select(c => c.Key)
            .Except(named, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        return [.. named.Concat(unnamed).Select((id, index) =>
            new RankedCard(index + 1, id, manifest.Cards[id], judgement.Cards.GetValueOrDefault(id)))];
    }

    private static bool IsRendered(RunManifest manifest, string id) =>
        manifest.Cards.TryGetValue(id, out CardEntry? entry) && entry.Rendered;
}
