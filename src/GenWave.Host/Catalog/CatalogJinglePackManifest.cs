namespace GenWave.Host.Catalog;

/// <summary>
/// A jingle pack's manifest content (SPEC F165.1/F165.2/F165.4, STORY-399, PLAN T414) — mirrors
/// <see cref="CatalogVoicePackManifest"/>'s own "ephemeral, hardened, null-tolerant" shape for the
/// FOURTH assets-carrying kind: a display <see cref="PackName"/> and the <see cref="Assets"/> this
/// pack ships. Unlike a voice pack's shared single preview clip, every jingle asset IS its own
/// playable file — there is no separate preview field.
/// </summary>
public sealed record CatalogJinglePackManifest(string PackName, IReadOnlyList<CatalogJinglePackAsset> Assets)
{
    /// <summary>
    /// Structural equality over <see cref="Assets"/> — mirrors <see cref="CatalogVoicePackManifest"/>'s
    /// own remarks: the compiler-synthesized record equality would otherwise compare
    /// <see cref="IReadOnlyList{T}"/> by REFERENCE.
    /// </summary>
    public bool Equals(CatalogJinglePackManifest? other) =>
        other is not null &&
        string.Equals(PackName, other.PackName, StringComparison.Ordinal) &&
        Assets.SequenceEqual(other.Assets);

    /// <inheritdoc cref="Equals(CatalogJinglePackManifest?)"/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(PackName, StringComparer.Ordinal);
        foreach (var asset in Assets)
            hash.Add(asset);

        return hash.ToHashCode();
    }
}

/// <summary>
/// One asset a <see cref="CatalogJinglePackManifest"/> declares (SPEC F165.2/F165.3/F165.4).
/// <see cref="Role"/> is the same closed set <c>library.media.jingle_role</c>'s own CHECK enforces
/// (db/45: <c>bed</c>/<c>sting</c>/<c>station_id</c>) — validated here as defense in depth, ahead of
/// the database's own constraint. <see cref="Attribution"/> is present only when
/// <see cref="License"/> is <c>CC-BY</c> — a <c>CC0</c> asset carries none, and
/// <see cref="CatalogJinglePackManifestSerializer"/> refuses the whole manifest if the two disagree
/// either way.
/// </summary>
public sealed record CatalogJinglePackAsset(
    string File, string Sha256, string Role, string Title, string License,
    CatalogJinglePackAttribution? Attribution);

/// <summary>
/// Per-asset attribution (SPEC F165.4), required exactly when its owning
/// <see cref="CatalogJinglePackAsset.License"/> is <c>CC-BY</c>. Persisted only as part of the raw
/// manifest jsonb (<c>station.jingle_pack.definition</c>) — there is no separate
/// <c>station.attribution</c> table (db/45's own header remarks); the F169.2 attributions endpoint
/// (PLAN T419) reads it straight back out of that column.
/// </summary>
public sealed record CatalogJinglePackAttribution(string Creator, string SourceUrl, string License);
