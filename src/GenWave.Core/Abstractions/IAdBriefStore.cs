using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// The <c>station.ad_brief</c> seam (SPEC F159.1, F162.2; STORY-389; PLAN T398) — deliberately
/// narrow, exactly <see cref="IAdSpotStore"/>'s own "Core-level port a MediaLibrary repository
/// implements directly" placement, one table over. Read members are deferred to whichever future task
/// first needs one (PLAN T400's own prompt sampler, T404's Briefs tab) — this seam ships with only
/// the write every future consumer already needs the SAME way (STORY-389 AC1's own upsert fact).
/// </summary>
public interface IAdBriefStore
{
    /// <summary>
    /// Upserts one brief, keyed on <c>(pack_slug, brand)</c> (SPEC F162.2's own pack-install key,
    /// <c>station.ad_brief</c>'s <c>UNIQUE NULLS NOT DISTINCT (pack_slug, brand)</c> constraint,
    /// db/42). A SECOND call with the SAME <paramref name="packSlug"/>/<paramref name="brand"/> pair
    /// updates the existing row's <paramref name="premise"/>/<paramref name="tone"/>/
    /// <paramref name="structure"/>/<paramref name="enabled"/> in place — never a duplicate row, and
    /// <c>created_at</c> is untouched by the update half.
    ///
    /// <para>
    /// <b>Ratified by Dean 2026-09-02 (SPEC F159.1 rider, PLAN T398): the owner-brief cap.</b>
    /// <paramref name="packSlug"/> <see langword="null"/> means an owner-authored brief — Postgres's
    /// <c>NULLS NOT DISTINCT</c> modifier makes every such call collapse onto the SAME row for a
    /// given <paramref name="brand"/>, so re-authoring an existing owner brief for a brand UPDATES
    /// it, never forks a second one: a brand is a brand.
    /// </para>
    /// </summary>
    Task<AdBrief> UpsertAsync(
        string? packSlug, string brand, string? premise, string? tone, string? structure, bool enabled,
        CancellationToken ct);
}
