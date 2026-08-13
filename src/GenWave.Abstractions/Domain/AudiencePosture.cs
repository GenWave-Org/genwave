namespace GenWave.Core.Domain;

/// <summary>
/// SPEC F95.1/F95.4 (STORY-250, PLAN T111/T114) — the two-value audience posture a station commits
/// to. <see cref="Everyone"/> excludes <c>explicit = true</c> rows from every candidate pool by
/// construction (rotation, request matcher, boundary bias); <see cref="Mature"/> plays everything,
/// unmasked. A track classified unknown (<c>explicit IS NULL</c>) plays under either posture —
/// unknown-is-explicit was declined at /explore.
///
/// This is the enum every pool-predicate consumer switches on, resolved from the raw
/// <c>StationOptions.Audience</c> string through the ONE fail-closed seam,
/// <see cref="AudiencePostureParser.Parse"/> — never a bare string comparison against the setting
/// (T114 review: a bare <c>==</c>/<c>!=</c> against <c>StationOptions.Audience</c> anywhere is a FAIL).
///
/// Deliberately its own tiny domain type rather than sharing a vocabulary with
/// <c>GenWave.Host.Catalog.CatalogAudience</c> (the persona-catalog shelf's content rating): same two
/// words, unrelated concepts — a station's live playout posture vs. a shelf entry's self-declared
/// audience rating. Coupling them would tie two independently-evolving axes to one enum's future
/// (the reviewer's altitude ruling at T114).
/// </summary>
public enum AudiencePosture
{
    /// <summary>The default, safe-for-everyone posture: excludes <c>explicit = true</c> rows from every candidate pool.</summary>
    Everyone,

    /// <summary>Plays everything, unmasked — including rows classified <c>explicit = true</c>.</summary>
    Mature,
}
