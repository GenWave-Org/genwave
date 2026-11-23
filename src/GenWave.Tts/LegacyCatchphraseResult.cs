using GenWave.Core.Domain;

namespace GenWave.Tts;

/// <summary>
/// What the legacy <see cref="Core.Abstractions.IQuirkSelector"/> and <see cref="Core.Abstractions.ITaglineLedger"/>
/// decided for one break. Present on a <see cref="DjLineSnapshotInput"/> only while the old path still runs
/// (shadow); the snapshot source turns it into the <see cref="CatchphraseLimitsMemo"/>.
/// </summary>
/// <param name="Quirks">What the selector handed the writer.</param>
/// <param name="Allowed">Tagline spans the ledger lets be said now.</param>
/// <param name="Unavailable">Tagline spans over quota, cooling down, or spent.</param>
/// <param name="Spent">The capped rules the selector did not hand this break, exactly what the legacy repeat check flags.</param>
public sealed record LegacyCatchphraseResult(
    QuirkSelection Quirks,
    IReadOnlyList<string> Allowed,
    IReadOnlyList<string> Unavailable,
    IReadOnlyList<string> Spent)
{
    /// <summary>
    /// Builds it from the writer's own values, splitting taglines and finding the spent rules exactly as the legacy
    /// repeat check does.
    /// </summary>
    public static LegacyCatchphraseResult From(PersonaCard? card, QuirkSelection quirks, IReadOnlyList<TaglineStatus> taglines) =>
        new(
            quirks,
            taglines.Where(t => t.Available).Select(t => t.Span).ToList(),
            taglines.Where(t => !t.Available).Select(t => t.Span).ToList(),
            LlmCopyWriter.CappedNotSelectedRules(card, quirks));
}
