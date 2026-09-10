namespace GenWave.Core.Domain;

/// <summary>
/// Discriminated union expressing every outcome of
/// <c>Abstractions.ISponsorStore.DeleteIfUnreferencedAsync</c> (SPEC F171, F172; STORY-406,
/// STORY-410; PLAN T432) — mirrors <see cref="FontPackDeleteResult"/>'s own closed-hierarchy shape one
/// table over.
/// </summary>
public abstract record SponsorDeleteResult
{
    private SponsorDeleteResult() { }

    /// <summary>The sponsor was removed.</summary>
    public sealed record Deleted : SponsorDeleteResult;

    /// <summary>No sponsor with the requested id exists.</summary>
    public sealed record NotFound : SponsorDeleteResult;

    /// <summary>
    /// The delete was refused because at least one <c>station.ad_brief</c>, <c>station.ad_spot</c>, or
    /// <c>station.show</c> row still names this sponsor (every one of the three FKs carries
    /// <c>ON DELETE RESTRICT</c>, db/46). <see cref="Briefs"/>/<see cref="Spots"/>/<see cref="Shows"/>
    /// are the referencing counts; <see cref="Titles"/> names up to the first ten referencing rows
    /// (brief premise, spot title, show name — any stable order) — the
    /// <c>PersonaWriteResult.ScheduledElsewhere</c>/<see cref="FontPackDeleteResult.Referenced"/>
    /// "name every offending row" precedent, capped rather than unbounded.
    /// </summary>
    public sealed record InUse(
        int Briefs, int Spots, int Shows, IReadOnlyList<string> Titles) : SponsorDeleteResult;
}
