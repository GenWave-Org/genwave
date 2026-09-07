namespace GenWave.Host.Catalog;

using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// The hardened, null-tolerant deserializer for a <see cref="CatalogJinglePackManifest"/> (SPEC
/// F165.1/F165.2/F165.3/F165.4, STORY-399, PLAN T414) — mirrors
/// <see cref="CatalogVoicePackManifestSerializer"/>'s own idiom exactly: reads into an ephemeral,
/// all-nullable projection first, rather than deserializing straight into non-nullable properties. A
/// malformed document, or ANY declared field failing its own shape/length/closed-set gate, degrades
/// the WHOLE manifest to <see langword="null"/> (never throws, never partially admits a pack) — the
/// same "a pack IS its files" all-or-nothing posture every sibling manifest parser already holds.
/// This is genuinely a SECOND, independent gate behind the genwave-catalog repo's own CI schema
/// validation (<c>schemas/jingle-pack-manifest.schema.json</c>) — defense in depth against a
/// compromised or stale catalog origin, not a duplicate of that check for its own sake.
///
/// <para>
/// <b><see cref="CatalogJinglePackAsset.Role"/> is the ONE closed-set membership check this parser
/// itself enforces</b> (unlike <see cref="CatalogVoicePackManifestSerializer"/>'s own <c>engine</c>,
/// which is shape-only — see that type's own remarks for why <c>engine</c> is different). There is no
/// controller-side "is this station's engine right now" question for a jingle role the way there is
/// for a TTS engine: <c>bed</c>/<c>sting</c>/<c>station_id</c> is a closed, permanent set mirroring
/// <c>library.media.jingle_role</c>'s own CHECK (db/45) verbatim, so refusing an unknown role here,
/// before a single byte is fetched, is strictly better than discovering the same refusal only once
/// the database rejects the insert.
/// </para>
///
/// <para>
/// NO <c>Serialize</c> (mirrors every sibling manifest serializer's own asymmetry): this app never
/// WRITES a jingle-pack manifest — packs are catalog-authored content this app only ever reads
/// through the guarded proxy door (SPEC F90.2-F90.4). The durable write
/// <c>Api.JinglePackController.Install</c> performs stores this manifest's own already-validated JSON
/// verbatim as <c>station.jingle_pack.definition</c>, never a re-serialized copy built from this
/// type.
/// </para>
/// </summary>
public static partial class CatalogJinglePackManifestSerializer
{
    /// <summary>A pack name is a short display label, never a sentence.</summary>
    public const int MaxPackNameLength = 64;

    /// <summary>The genwave-catalog schema's own bound (<c>schemas/jingle-pack-manifest.schema.json</c>,
    /// PR #76) — a pack ships between 1 and this many assets.</summary>
    public const int MaxAssetsPerPack = 32;

    /// <summary>The genwave-catalog schema's own <c>file</c> length bound.</summary>
    public const int MaxFileLength = 96;

    /// <summary>The genwave-catalog schema's own <c>title</c> length bound — mirrors
    /// <c>library.media.title</c>'s own practical display-label ceiling, not a database CHECK.</summary>
    public const int MaxTitleLength = 120;

    /// <summary>The genwave-catalog schema's own <c>attribution.creator</c> length bound.</summary>
    public const int MaxCreatorLength = 120;

    /// <summary>The genwave-catalog schema's own <c>attribution.sourceUrl</c> length bound.</summary>
    public const int MaxSourceUrlLength = 2048;

    /// <summary>
    /// SPEC F165.3's closed set, mirroring <c>library.media.jingle_role</c>'s own CHECK (db/45:
    /// <c>check (jingle_role is null or jingle_role in ('bed', 'sting', 'station_id'))</c>) verbatim —
    /// this is the ONE place this app declares that set for parse-time validation; a value outside it
    /// degrades the whole manifest to <see langword="null"/> rather than reaching the database at all.
    /// </summary>
    static readonly string[] ValidRoles = ["bed", "sting", "station_id"];

    /// <summary>The genwave-catalog schema's own closed license set.</summary>
    static readonly string[] ValidLicenses = ["CC0", "CC-BY"];

    /// <summary>Attribution is required exactly when an asset's own license is this value (SPEC
    /// F165.4) — a <c>CC0</c> asset needs none.</summary>
    const string AttributionRequiringLicense = "CC-BY";

    /// <summary>A safe lowercase file name, one of the three shipped audio containers — matches the
    /// genwave-catalog schema's own <c>assets[].file</c> pattern exactly (lowercase-only, stricter than
    /// <see cref="CatalogIndexValidator"/>'s own broader, case-insensitive asset-path pattern, which
    /// governs the CDN path rather than the manifest's own declared file name).</summary>
    [GeneratedRegex(@"\A[a-z0-9][a-z0-9._-]*\.(?:wav|mp3|flac)\z")]
    private static partial Regex FileFormat();

    /// <summary>The same lowercase-hex sha256 shape <see cref="CatalogIndexValidator"/>'s own
    /// <c>Sha256Pattern</c> enforces on every other asset reference — not composed from that type's
    /// own private regex (different project layer, same shape by construction), mirrored verbatim.</summary>
    [GeneratedRegex(@"\A[a-f0-9]{64}\z")]
    private static partial Regex Sha256Format();

    /// <summary>The genwave-catalog schema's own <c>attribution.sourceUrl</c> pattern — an http(s) URL
    /// containing no whitespace or the characters that would let it break out of an HTML attribute if
    /// ever rendered unescaped.</summary>
    [GeneratedRegex(@"\Ahttps?://[^\s<>""']+\z")]
    private static partial Regex SourceUrlFormat();

    /// <summary>Case-insensitive read options (mirrors every sibling manifest serializer's own
    /// untrusted-parsing options) — leniency on the READ side only.</summary>
    static readonly JsonSerializerOptions ParseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static CatalogJinglePackManifest? Deserialize(string json)
    {
        CatalogJinglePackManifestJson? raw;
        try
        {
            raw = JsonSerializer.Deserialize<CatalogJinglePackManifestJson>(json, ParseOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (raw is null)
            return null;

        if (raw is not { PackName: { Length: > 0 and <= MaxPackNameLength } packName } || string.IsNullOrWhiteSpace(packName))
            return null;

        if (raw.Assets is not { Count: > 0 and <= MaxAssetsPerPack } rawAssets)
            return null;

        var assets = new List<CatalogJinglePackAsset>(rawAssets.Count);
        var seenFiles = new HashSet<string>(StringComparer.Ordinal);
        var seenTitles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawAsset in rawAssets)
        {
            if (TryParseAsset(rawAsset) is not { } asset)
                return null;

            // Duplicate file or title within one manifest both refuse the whole pack (STORY-399's own
            // brief) — a duplicate title would silently collide at install time on the very
            // `(pack_slug, title)` upsert key db/45 exists to enforce; a duplicate file would silently
            // shadow one asset's own bytes with another's during staging.
            if (!seenFiles.Add(asset.File) || !seenTitles.Add(asset.Title))
                return null;

            assets.Add(asset);
        }

        return new CatalogJinglePackManifest(packName, assets);
    }

    static CatalogJinglePackAsset? TryParseAsset(CatalogJinglePackAssetJson? raw)
    {
        if (raw is not { File: { Length: > 0 and <= MaxFileLength } file } || !FileFormat().IsMatch(file))
            return null;

        if (raw.Sha256 is not { } sha256 || !Sha256Format().IsMatch(sha256))
            return null;

        if (raw.Role is not { } role || !ValidRoles.Contains(role))
            return null;

        if (raw.Title is not { Length: > 0 and <= MaxTitleLength } title || string.IsNullOrWhiteSpace(title))
            return null;

        if (raw.License is not { } license || !ValidLicenses.Contains(license))
            return null;

        var requiresAttribution = string.Equals(license, AttributionRequiringLicense, StringComparison.Ordinal);

        if (requiresAttribution)
        {
            if (TryParseAttribution(raw.Attribution) is not { } attribution)
                return null;

            return new CatalogJinglePackAsset(file, sha256, role, title, license, attribution);
        }

        // A CC0 asset carries NO attribution — one declared anyway (rather than being simply absent)
        // is refused too, the same "the two must not disagree" posture SPEC F165.4 calls for.
        if (raw.Attribution is not null)
            return null;

        return new CatalogJinglePackAsset(file, sha256, role, title, license, null);
    }

    static CatalogJinglePackAttribution? TryParseAttribution(CatalogJinglePackAttributionJson? raw)
    {
        if (raw is not { Creator: { Length: > 0 and <= MaxCreatorLength } creator } || string.IsNullOrWhiteSpace(creator))
            return null;

        if (raw.SourceUrl is not { Length: > 0 and <= MaxSourceUrlLength } sourceUrl || !SourceUrlFormat().IsMatch(sourceUrl))
            return null;

        if (raw.License is not { } license || !ValidLicenses.Contains(license))
            return null;

        return new CatalogJinglePackAttribution(creator, sourceUrl, license);
    }

    /// <summary>Ephemeral, all-nullable projection of an untrusted jingle-pack manifest document.</summary>
    sealed record CatalogJinglePackManifestJson
    {
        public string? PackName { get; init; }
        public IReadOnlyList<CatalogJinglePackAssetJson>? Assets { get; init; }
    }

    /// <summary>Ephemeral, all-nullable projection of one raw <c>assets[]</c> element.</summary>
    sealed record CatalogJinglePackAssetJson
    {
        public string? File { get; init; }
        public string? Sha256 { get; init; }
        public string? Role { get; init; }
        public string? Title { get; init; }
        public string? License { get; init; }
        public CatalogJinglePackAttributionJson? Attribution { get; init; }
    }

    /// <summary>Ephemeral, all-nullable projection of one raw <c>attribution</c> object.</summary>
    sealed record CatalogJinglePackAttributionJson
    {
        public string? Creator { get; init; }
        public string? SourceUrl { get; init; }
        public string? License { get; init; }
    }
}
