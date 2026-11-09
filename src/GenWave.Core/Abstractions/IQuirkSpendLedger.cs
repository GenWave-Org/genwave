namespace GenWave.Core.Abstractions;

/// <summary>
/// The per-shift quirk spend marks (SPEC F218.7): <c>station.persona_memory</c> rows of kind
/// <c>quirk</c>, source accrued, content = quirk text + shift key. Kept apart from
/// <see cref="IPersonaMemory"/> so that interface (and its implementers) stays untouched.
/// </summary>
/// <remarks>
/// Accrued rows are evicted oldest-first past the per-kind cap (F71.6). A current-shift spend row is
/// the newest by construction, so eviction reaches it only when one shift alone spends more rows than
/// the cap (50 by default) - far beyond any card's sum of <c>maxPerShift</c>.
/// </remarks>
public interface IQuirkSpendLedger
{
    /// <summary>How many spend rows exist for exactly <paramref name="content"/>.</summary>
    Task<int> CountSpendsAsync(long personaId, string content, CancellationToken ct);

    /// <summary>Writes one spend row for <paramref name="content"/>.</summary>
    Task RecordSpendAsync(long personaId, string content, CancellationToken ct);
}
