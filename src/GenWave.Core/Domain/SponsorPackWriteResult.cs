namespace GenWave.Core.Domain;

/// <summary>
/// Discriminated union expressing every outcome of
/// <c>Abstractions.ISponsorStore.UpsertPackSponsorsAsync</c> (SPEC F171.1/F2/F3; STORY-406; PLAN
/// T432, round-3 finding R1) — a BATCH write over every brand a pack manifest declares, refused as
/// a whole before any row lands rather than partially applied. Mirrors <see cref="SponsorWriteResult"/>'s
/// own closed-hierarchy shape (a private base constructor closes the hierarchy), but <see cref="Ok"/>
/// wraps a LIST, not one <see cref="Sponsor"/> — the caller's own manifest install is inherently
/// many-brands-at-once, unlike every other <c>Abstractions.ISponsorStore</c> write.
/// </summary>
public abstract record SponsorPackWriteResult
{
    private SponsorPackWriteResult() { }

    /// <summary>Every brand upserted, in the SAME order <c>names</c> was passed — the caller's own
    /// manifest order, so a subsequent per-brief lookup by index never has to re-sort.</summary>
    public sealed record Ok(IReadOnlyList<Sponsor> Sponsors) : SponsorPackWriteResult;

    /// <summary>Two DIFFERENT input names fold to the SAME <c>station.sponsor_fold</c> key — refused
    /// before any row is written, never a partial install of the other brands in the same manifest.
    /// <see cref="First"/>/<see cref="Second"/> are the two colliding names, in the caller's own
    /// input order.</summary>
    public sealed record NamesCollide(string First, string Second) : SponsorPackWriteResult;

    /// <summary>A db/46 <c>CHECK</c> constraint rejected one name's shape (SQLSTATE 23514) —
    /// <see cref="FieldName"/> is parsed off the constraint's own <c>sponsor_&lt;field&gt;_check</c>
    /// name, the SAME parse <see cref="SponsorWriteResult.InvalidField"/> uses. <c>AdPackController</c>
    /// maps this to an HTTP 400 naming <c>brand</c>.</summary>
    public sealed record InvalidField(string FieldName) : SponsorPackWriteResult;
}
