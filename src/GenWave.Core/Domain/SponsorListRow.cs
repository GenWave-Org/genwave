namespace GenWave.Core.Domain;

/// <summary>
/// One row of <c>Abstractions.ISponsorStore.ListAsync</c>'s admin listing (SPEC F171.1, F172;
/// STORY-406, STORY-407; PLAN T432) — <see cref="Sponsor"/> plus the counts of everything currently
/// referencing it, computed at the store in the same round trip as the list itself (no N+1).
/// </summary>
/// <param name="Sponsor">The sponsor row.</param>
/// <param name="Briefs">How many <c>station.ad_brief</c> rows name this sponsor.</param>
/// <param name="SpotsByState">How many <c>station.ad_spot</c> rows name this sponsor, grouped by
/// <see cref="AdState"/> — a state with zero rows is simply absent from the dictionary, never an
/// explicit zero entry.</param>
/// <param name="Shows">How many <c>station.show</c> rows name this sponsor.</param>
public sealed record SponsorListRow(
    Sponsor Sponsor,
    int Briefs,
    IReadOnlyDictionary<AdState, int> SpotsByState,
    int Shows);
