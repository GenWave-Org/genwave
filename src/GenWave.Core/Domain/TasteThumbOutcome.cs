namespace GenWave.Core.Domain;

/// <summary>
/// Discriminated union expressing every outcome of
/// <see cref="Abstractions.IPersonaTasteAccrualStore.ThumbAsync"/> (SPEC F84.1, F84.5, F84.6;
/// STORY-215, PLAN T70). Mirrors <see cref="PersonaImportOutcome"/>'s closed-hierarchy shape: the
/// private constructor on the abstract base closes the hierarchy so <c>BoothLogController.ThumbTaste</c>
/// can pattern-match exhaustively, no discard arm.
/// </summary>
public abstract record TasteThumbOutcome
{
    private TasteThumbOutcome() { }

    /// <summary>
    /// The thumb was new (F84.5) and the accrued artist rule for <paramref name="PersonaId"/> was
    /// created or nudged. <paramref name="Weight"/> is the rule's weight AFTER this nudge, already
    /// clamped to <c>[-1, 1]</c> (F84.1).
    /// </summary>
    public sealed record Nudged(long PersonaId, double Weight) : TasteThumbOutcome;

    /// <summary>
    /// A thumb for this exact (persona, airing, direction) triple was already recorded (F84.5) — a
    /// double-tap, or a tap that arrived via the other surface for the same airing. Nothing was
    /// written; the weight did not move a second time.
    /// </summary>
    public sealed record AlreadyRecorded(long PersonaId) : TasteThumbOutcome;

    /// <summary>No booth-log row exists with the given id.</summary>
    public sealed record RowNotFound : TasteThumbOutcome;

    /// <summary>
    /// The row exists but is not thumbable for taste (F84.6): it is not a track-start row, no
    /// persona was stamped on it at air time, or it carries no known artist to attribute a rule to.
    /// </summary>
    public sealed record NotThumbable : TasteThumbOutcome;
}
