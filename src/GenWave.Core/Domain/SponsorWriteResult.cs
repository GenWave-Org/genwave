namespace GenWave.Core.Domain;

/// <summary>
/// Discriminated union expressing every outcome of an <c>Abstractions.ISponsorStore</c> write
/// (SPEC F171.1; STORY-406; PLAN T432). Mirrors <see cref="ShowWriteResult"/>'s own closed-hierarchy
/// shape: a private base constructor closes the hierarchy so callers can write exhaustive
/// pattern-match switches without a discard arm.
/// </summary>
public abstract record SponsorWriteResult
{
    private SponsorWriteResult() { }

    /// <summary>The write applied; <see cref="Sponsor"/> is the row after the write.</summary>
    public sealed record Ok(Sponsor Sponsor) : SponsorWriteResult;

    /// <summary>Another sponsor already holds the same folded name in the same <c>pack_slug</c>
    /// namespace (<c>sponsor_pack_slug_name_key</c>, <c>UNIQUE NULLS NOT DISTINCT</c>) — caught as
    /// SQLSTATE 23505.</summary>
    public sealed record NameTaken : SponsorWriteResult;

    /// <summary>An <c>UpdateAsync</c> call tried to change <c>SponsorEdit.Name</c> on a pack-owned row
    /// (<see cref="Sponsor.PackSlug"/> non-null) — refused at the store: only the installing pack
    /// (<c>UpsertPackSponsorsAsync</c>) may rename a pack sponsor.</summary>
    public sealed record NamePackOwned : SponsorWriteResult;

    /// <summary>No sponsor with the requested id exists.</summary>
    public sealed record NotFound : SponsorWriteResult;

    /// <summary>The caller's <c>expectedVersion</c> no longer matches the row's current <c>xmin</c> —
    /// re-read before trying again.</summary>
    public sealed record VersionConflict : SponsorWriteResult;

    /// <summary>A db/46 <c>CHECK</c> constraint rejected one field's shape (SQLSTATE 23514) —
    /// <see cref="FieldName"/> is parsed off the constraint's own <c>sponsor_&lt;field&gt;_check</c>
    /// name. Every caller that can produce this outcome (<c>CreateOwnerAsync</c>,
    /// <c>UpdateAsync</c>, <c>FindOrCreateOwnerAsync</c>) maps it to an HTTP 400 naming
    /// <see cref="FieldName"/> — both <c>AdsController</c> and <c>AdBriefsController</c> (PLAN T432,
    /// round 3) do this identically for their own <c>FindOrCreateOwnerAsync</c> calls.</summary>
    public sealed record InvalidField(string FieldName) : SponsorWriteResult;
}
