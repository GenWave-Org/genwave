namespace GenWave.Core.Domain;

/// <summary>
/// Discriminated union expressing every outcome of <see cref="Abstractions.IPersonaImportStore.ImportAsync"/>
/// (SPEC F79.3, F79.6; STORY-209, PLAN T67). Mirrors <see cref="PersonaWriteResult"/>'s closed-hierarchy
/// shape: the private constructor on the abstract base closes the hierarchy so
/// <c>PersonaController.Import</c> can pattern-match exhaustively, no discard arm.
/// </summary>
public abstract record PersonaImportOutcome
{
    private PersonaImportOutcome() { }

    /// <summary>
    /// The import committed. <see cref="PersonaId"/> is the persona's id (existing, on a re-import
    /// onto a living persona, or newly assigned); <see cref="WasCreated"/> distinguishes the two so
    /// the controller can answer 201 vs 200, mirroring the rest of this controller's create/update
    /// status-code convention.
    /// </summary>
    public sealed record Imported(long PersonaId, bool WasCreated) : PersonaImportOutcome;

    /// <summary>
    /// Another persona already holds the card's <c>name</c> (station.persona's <c>UNIQUE(name)</c>,
    /// SPEC F35.4) — the whole transaction rolled back, nothing was written.
    /// </summary>
    public sealed record NameConflict : PersonaImportOutcome;
}
