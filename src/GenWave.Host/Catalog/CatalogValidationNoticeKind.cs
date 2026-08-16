namespace GenWave.Host.Catalog;

/// <summary>
/// What kind of non-fatal issue a <see cref="CatalogValidationNotice"/> describes (round-1 review
/// findings 1/3, PLAN T292) — see
/// <see cref="CatalogIndexValidator.TryValidatePersonaAvatarAsset"/>'s own remarks for the full
/// three-rung ladder each value maps to.
/// </summary>
internal enum CatalogValidationNoticeKind
{
    /// <summary>The whole entry is excluded from <see cref="CatalogIndexValidator.TryValidate"/>'s
    /// returned list; the rest of the index still loads (STORY-331 AC6's own "that ENTRY is
    /// rejected" wording — an entry-scoped outcome, never <see cref="CatalogIndexValidator"/>'s own
    /// whole-index reject).</summary>
    EntryWithheld,

    /// <summary>The entry survives and lists; one optional field degraded to absent instead (SPEC
    /// F128.9's own "absent ⇒ a neutral placeholder" posture, extended to a present-but-broken
    /// field).</summary>
    FieldDegraded,
}
