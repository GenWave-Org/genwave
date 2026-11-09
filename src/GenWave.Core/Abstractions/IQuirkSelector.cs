using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F71.3, F218.7, gh-#293) — picks the quirks one break's prompt shows. Plain quirks are
/// sampled 2-3 at a time (F71.3); a <c>maxPerShift</c> quirk enters the pool only while its spend
/// rows for the shift are below the max, rolled at its <c>perBreakChance</c> (default 0.15).
/// </summary>
public interface IQuirkSelector
{
    /// <summary>
    /// Selects the quirks for one break. When <paramref name="spend"/> is true and both
    /// <paramref name="personaId"/> and <paramref name="shiftKey"/> are known, every chosen
    /// <c>maxPerShift</c> quirk has its spend row written BEFORE this returns (the caller dispatches
    /// the render afterwards). Without a shift key a spend cannot be recorded, so on air
    /// (<c>spend: true</c>) <c>maxPerShift</c> quirks are excluded from the pool. The preview path
    /// passes <c>spend: false</c>, never writes, and rolls them by chance alone.
    /// </summary>
    Task<QuirkSelection> SelectAsync(
        long? personaId, PersonaCard? card, ShiftKey? shiftKey, bool spend, CancellationToken ct);
}
