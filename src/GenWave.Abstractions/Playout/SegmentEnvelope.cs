namespace GenWave.Abstractions.Playout;

/// <summary>
/// SPEC F81.1 — the value object handed to the playout provider describing which tracks may enter
/// the candidate pool: a genre allow-list and an energy band over a scheduled time-of-day window.
/// The governing invariant (F81.2) is that this type only ever narrows a pool by construction — the
/// catalog query that consumes it never fetches a wider set and post-filters (see
/// <c>GenWave.MediaLibrary.Catalog.MediaRepository.GetEnvelopeCandidateAsync</c>). Exclusions and
/// artist/track separation rules are later, additive predicates layered on top of this shape — not
/// v1 fields (YAGNI: this type carries exactly what F81 needs today).
///
/// <para>
/// V1 ships exactly one 24/7 station-default envelope (F81.3, <see cref="StationDefault"/>) — no
/// schedule grid. <see cref="StartsAt"/>/<see cref="EndsAt"/> exist so a future <c>segment_schedule</c>
/// (designed-for, not built) can slice the day into more than one envelope without a shape change
/// here; nothing reads them yet.
/// </para>
///
/// <para>
/// <see cref="Genres"/> empty means "no genre constraint" — every track is admitted regardless of
/// whether it carries a genre tag at all. A non-empty list is a case-insensitive allow-list: a
/// track's genre must match one of the named genres. An untagged (<c>NULL</c> genre) track does
/// <em>not</em> satisfy a non-empty list — genre curation requires a positive tag, the opposite
/// judgment call from <see cref="EnergyRange"/>'s <c>NULL</c>-passes exemption (an absent genre is
/// simply unknown, not "enrichment lag" — see the query implementation's own remarks for that
/// distinction).
/// </para>
/// </summary>
public sealed record SegmentEnvelope(
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    IReadOnlyList<string> Genres,
    EnergyRange EnergyRange)
{
    /// <summary>
    /// The v1 station-default envelope (SPEC F81.3): the full day, every genre admitted, the full
    /// energy range — what a fresh install's <c>Station:Envelope:*</c> settings resolve to before an
    /// operator narrows either knob. <c>GenWave.Host</c>'s <c>StationEnvelopeOptions</c> mirrors this
    /// exact shape as its own property defaults.
    /// </summary>
    public static SegmentEnvelope StationDefault { get; } =
        new(TimeOnly.MinValue, TimeOnly.MaxValue, [], EnergyRange.Unconstrained);
}
