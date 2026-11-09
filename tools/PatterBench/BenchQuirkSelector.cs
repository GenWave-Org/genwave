namespace GenWave.PatterBench;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// The quirk pick for one persona's run, standing in for the app's selector (which lives in GenWave.Memory,
/// a project the bench does not reference). Same rules as F218.7: a quirk with a rule enters the pool by
/// its per-break chance (default 0.15), a per-shift cap is counted in memory, and the pool is sampled with
/// the shared <see cref="QuirkSampling"/>. The whole run is one shift.
/// </summary>
internal sealed class BenchQuirkSelector(Random random) : IQuirkSelector
{
    private const double DefaultPerBreakChance = 0.15;

    private readonly Dictionary<string, int> spends = new(StringComparer.Ordinal);

    public Task<QuirkSelection> SelectAsync(
        long? personaId, PersonaCard? card, ShiftKey? shiftKey, bool spend, CancellationToken ct)
    {
        if (card is not { Quirks.Count: > 0 })
        {
            return Task.FromResult(QuirkSelection.None);
        }

        var pool = card.Quirks.Where(text => EntersPool(text, card)).ToList();
        var chosen = QuirkSampling.Sample(pool, random);
        var capped = chosen.Where(text => RuleOf(card, text)?.MaxPerShift is not null).ToList();
        foreach (var text in capped)
        {
            spends[text] = spends.GetValueOrDefault(text) + 1;
        }

        return Task.FromResult(new QuirkSelection(chosen, capped));
    }

    private bool EntersPool(string text, PersonaCard card)
    {
        var rule = RuleOf(card, text);
        if (rule is null || (rule.MaxPerShift is null && rule.PerBreakChance is null))
        {
            return true;
        }

        if (random.NextDouble() >= (rule.PerBreakChance ?? DefaultPerBreakChance))
        {
            return false;
        }

        return rule.MaxPerShift is not { } max || spends.GetValueOrDefault(text) < max;
    }

    private static QuirkRule? RuleOf(PersonaCard card, string text) =>
        card.QuirkRules?.FirstOrDefault(r => r.Text == text);
}
