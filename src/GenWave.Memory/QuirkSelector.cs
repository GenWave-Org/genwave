using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Memory;

/// <summary>
/// Default <see cref="IQuirkSelector"/> (SPEC F71.3, F218.7). A card without <see cref="PersonaCard.QuirkRules"/>
/// samples exactly as it always did; rules add the per-shift cap and the chance roll.
/// </summary>
public sealed class QuirkSelector(IQuirkSpendLedger ledger, Random? random = null) : IQuirkSelector
{
    /// <summary>Chance a <c>maxPerShift</c> quirk enters the pool when its rule sets none (F218.7).</summary>
    public const double DefaultPerBreakChance = 0.15;

    readonly Random random = random ?? Random.Shared;

    sealed record Candidate(string Text, QuirkRule? Rule)
    {
        public bool IsCapped => Rule?.MaxPerShift is not null;
    }

    public async Task<QuirkSelection> SelectAsync(
        long? personaId, PersonaCard? card, ShiftKey? shiftKey, bool spend, CancellationToken ct)
    {
        if (card is not { Quirks.Count: > 0 })
            return QuirkSelection.None;

        var trackedPersona = personaId is not null && shiftKey is not null ? personaId : null;
        var pool = new List<Candidate>();
        foreach (var text in card.Quirks)
        {
            var candidate = new Candidate(text, card.QuirkRules?.FirstOrDefault(r => r.Text == text));
            // A capped quirk cannot air where its spend cannot be recorded (on air, untracked).
            if (spend && trackedPersona is null && candidate.IsCapped)
                continue;
            if (await EntersPoolAsync(candidate, trackedPersona, shiftKey, ct))
                pool.Add(candidate);
        }

        var chosen = QuirkSampling.Sample(pool, random);
        var capped = chosen.Where(c => c.IsCapped).Select(c => c.Text).ToList();

        // Mark before render (F218.7): every spend row is awaited before the caller can dispatch.
        if (spend && trackedPersona is { } id && shiftKey is not null)
        {
            foreach (var text in capped)
                await ledger.RecordSpendAsync(id, SpendContent(text, shiftKey), ct);
        }

        return new QuirkSelection(chosen.Select(c => c.Text).ToList(), capped);
    }

    async Task<bool> EntersPoolAsync(Candidate candidate, long? personaId, ShiftKey? shiftKey, CancellationToken ct)
    {
        var rule = candidate.Rule;
        if (rule is null || (rule.MaxPerShift is null && rule.PerBreakChance is null))
            return true;

        var chance = rule.PerBreakChance ?? DefaultPerBreakChance;
        if (random.NextDouble() >= chance)
            return false;

        if (rule.MaxPerShift is not { } max || personaId is not { } id || shiftKey is null)
            return true;

        return await ledger.CountSpendsAsync(id, SpendContent(candidate.Text, shiftKey), ct) < max;
    }

    static string SpendContent(string text, ShiftKey shiftKey) => $"{text} @ {shiftKey}";
}
