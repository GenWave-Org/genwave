namespace GenWave.Host.Catalog;

using System.Text.Json;

/// <summary>
/// The hardened, null-tolerant deserializer for a <see cref="CatalogAdPackManifest"/> (SPEC F162.2,
/// STORY-393, PLAN T405) — mirrors <see cref="CatalogAvatarPackManifestSerializer"/>'s own idiom:
/// reads into an ephemeral, all-nullable <see cref="CatalogAdPackManifestJson"/> projection first,
/// rather than deserializing straight into <see cref="CatalogAdPackManifest"/>'s own non-nullable
/// properties (which <c>System.Text.Json</c> would silently leave <see langword="null"/> for a
/// missing field despite the C# type saying otherwise). A malformed document, a missing/empty
/// <c>briefs</c> array, or ANY one declared brief failing its own shape/length gate degrades the
/// WHOLE manifest to <see langword="null"/> (never throws, never partially admits a pack) — the same
/// "a pack IS its files/briefs" all-or-nothing posture <see cref="CatalogAvatarPackManifestSerializer"/>
/// already holds for <c>items[]</c>, re-applied here to <c>briefs[]</c>.
///
/// <para>
/// <b>WHY THE CAPS BELOW EXIST — this kind is the FIRST manifest whose own parsed content becomes a
/// DURABLE database write.</b> Every sibling manifest parser (font/avatar/icon) only ever feeds a
/// READ path — a shelf card, a detail panel, an install-time re-encode of BYTES the index itself
/// already size-capped. <see cref="Api.AdPackController.Install"/> instead calls
/// <c>IAdBriefStore.UpsertAllAsync</c> with the WHOLE declared brief list — unbounded counts or
/// field lengths here would ride straight into <c>station.ad_brief</c> (unbounded <c>text</c>
/// columns, db/42) and, downstream, every future LLM prompt <c>AdScriptWriter</c> builds from a
/// sampled brief (SPEC F160.1/F160.2). The catalog is Dean-curated, but this parser is the actual
/// trust boundary a hostile or simply careless index still has to cross — capped sanely rather than
/// left open because nothing upstream enforces it: <see cref="MaxBriefsPerPack"/> (100 — an
/// operator-curated brand universe is dozens, not thousands, the SAME order of magnitude
/// <c>AdBriefRepository.MaxUnpagedRows</c>'s own remarks assume for the whole station);
/// <see cref="MaxBrandLength"/> (120 — round-3 review finding R2: a manifest brand becomes a
/// <c>station.sponsor.name</c> row via <c>ISponsorStore.UpsertPackSponsorsAsync</c>, so this parser's
/// own cap MUST match db/46's <c>sponsor_name_check</c> CHECK
/// (<c>char_length(trim(name)) between 1 and 120</c>) — a longer value here would parse clean and
/// then fail at the DB with an opaque 23514, rather than a clear 400 naming the field at the trust
/// boundary where it belongs); <see cref="MaxHintLength"/>
/// (500 — a generous one-paragraph prompt hint, shared by <see cref="CatalogAdPackBrief.Premise"/>/
/// <see cref="CatalogAdPackBrief.Tone"/>/<see cref="CatalogAdPackBrief.Structure"/>, which are all
/// the SAME free-text-hint shape at the wire — no reason to keep three independently-drifting
/// numbers for one field class).
/// </para>
///
/// <para>
/// <b>NO REPEATED BRAND, ORDINAL (round-4 review blocker — a same-manifest regression HEAD never
/// caught).</b> Two briefs whose <see cref="CatalogAdPackBrief.Brand"/> compare ordinal-equal fail the
/// WHOLE manifest, the same all-or-nothing posture as every cap above — because
/// <c>ISponsorStore.UpsertPackSponsorsAsync</c> folds an exact-duplicate brand string onto ONE sponsor
/// row, yet <see cref="Api.AdPackController.Install"/> would still hand
/// <c>IAdBriefStore.UpsertAllAsync</c> TWO separate brief writes for that one sponsor id — the second
/// silently overwriting the first's own premise under <c>station.ad_brief</c>'s own
/// <c>(pack_slug, sponsor_id)</c> conflict target, and the install response's own brand-per-sponsor
/// projection would carry that sponsor id twice. Deliberately ORDINAL, narrower than a FOLDED
/// comparison: two DIFFERENT brand spellings that merely fold onto the same sponsor identity (casing,
/// whitespace) are NOT this parser's job to refuse — that is
/// <c>SponsorPackWriteResult.NamesCollide</c>'s own arm one seam under here, resolved at WRITE time
/// against the fold, never the literal text this parser sees. This is the SAME rule the catalog side
/// already enforces before a pack ever ships (<c>ad-pack-duplicate-brand</c>, <c>genwave-catalog</c>'s
/// own <c>validate.py</c> KindSpec, PLAN T407/catalog#74) — this parser is simply the app's own
/// re-assertion of that twin rule at the ACTUAL trust boundary, since nothing upstream of this method
/// is guaranteed to have enforced it.
/// </para>
///
/// <para>
/// NO <c>Serialize</c> (deliberate asymmetry, mirrors <see cref="CatalogAvatarPackManifestSerializer"/>'s
/// own remarks verbatim): this app never WRITES an ad-pack manifest — packs are catalog-authored
/// content this app only ever reads through the guarded proxy door (SPEC F90.2-F90.4). The DURABLE
/// write this kind's install route performs targets <c>station.ad_brief</c>, a different shape
/// entirely (<c>IAdBriefStore.UpsertAsync</c>), never a re-serialized copy of this manifest itself.
/// </para>
/// </summary>
public static class CatalogAdPackManifestSerializer
{
    /// <summary>See this type's own class remarks for why this cap exists and how its magnitude was chosen.</summary>
    public const int MaxBriefsPerPack = 100;

    /// <summary>See this type's own class remarks — matches db/46's <c>sponsor_name_check</c> CHECK,
    /// since a manifest brand becomes a <c>station.sponsor.name</c> row.</summary>
    public const int MaxBrandLength = 120;

    /// <summary>See this type's own class remarks — shared by every optional prompt-hint field
    /// (<see cref="CatalogAdPackBrief.Premise"/>/<see cref="CatalogAdPackBrief.Tone"/>/
    /// <see cref="CatalogAdPackBrief.Structure"/>), one number for one field class.</summary>
    public const int MaxHintLength = 500;

    /// <summary>Case-insensitive read options (mirrors <c>CatalogIndexValidator</c>'s own untrusted-parsing options) — leniency on the READ side only.</summary>
    static readonly JsonSerializerOptions ParseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static CatalogAdPackManifest? Deserialize(string json)
    {
        CatalogAdPackManifestJson? raw;
        try
        {
            raw = JsonSerializer.Deserialize<CatalogAdPackManifestJson>(json, ParseOptions);
        }
        catch (JsonException)
        {
            // Malformed JSON, or a shape Deserialize can't convert (e.g. a `briefs` leaf typed as an
            // object instead of an array) — degrade to "no manifest", never throw out of a
            // detail-projection or install call.
            return null;
        }

        if (raw is null)
            return null;

        // packName is OPTIONAL and purely decorative (mirrors CatalogIndexValidator.TryParseFamily's
        // own "decorative, never fails validation" posture) — absent, empty, or whitespace-only all
        // fold to null rather than failing the whole manifest over a cosmetic field.
        var packName = string.IsNullOrWhiteSpace(raw.PackName) ? null : raw.PackName;

        if (raw.Briefs is not { Count: > 0 } rawBriefs)
            return null;

        if (rawBriefs.Count > MaxBriefsPerPack)
            return null;

        var briefs = new List<CatalogAdPackBrief>(rawBriefs.Count);
        // Ordinal — see this type's own "NO REPEATED BRAND, ORDINAL" class remarks for why exact
        // duplicates fail the whole manifest here while a merely FOLD-equal pair does not.
        var seenBrands = new HashSet<string>(rawBriefs.Count, StringComparer.Ordinal);
        foreach (var rawBrief in rawBriefs)
        {
            if (TryParseBrief(rawBrief) is not { } brief)
                return null;

            if (!seenBrands.Add(brief.Brand))
                return null;

            briefs.Add(brief);
        }

        return new CatalogAdPackManifest(packName, briefs);
    }

    static CatalogAdPackBrief? TryParseBrief(CatalogAdPackBriefJson? raw)
    {
        // `Length: > 0` alone admits a whitespace-only brand ("   ") — REQUIRED means non-blank, not
        // merely non-zero-length; string.IsNullOrWhiteSpace is the second half of that check (T405
        // review F3's own fact caught this).
        if (raw is not { Brand: { Length: > 0 and <= MaxBrandLength } brand } || string.IsNullOrWhiteSpace(brand))
            return null;

        if (!TryFoldOptionalHint(raw.Premise, out var premise)) return null;
        if (!TryFoldOptionalHint(raw.Tone, out var tone)) return null;
        if (!TryFoldOptionalHint(raw.Structure, out var structure)) return null;

        return new CatalogAdPackBrief(brand, premise, tone, structure);
    }

    /// <summary>
    /// An OPTIONAL prompt-hint field's own fold: absent/whitespace-only becomes
    /// <paramref name="folded"/> = <see langword="null"/> (a legitimate "no hint" — never a reject);
    /// present-and-non-empty must sit within <see cref="MaxHintLength"/> or this returns
    /// <see langword="false"/> — an over-length hint fails the WHOLE brief (and so, via
    /// <see cref="Deserialize"/>'s own all-or-nothing posture, the whole pack) rather than silently
    /// truncating remote content this station is about to hand an LLM.
    /// </summary>
    static bool TryFoldOptionalHint(string? raw, out string? folded)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            folded = null;
            return true;
        }

        if (raw.Length > MaxHintLength)
        {
            folded = null;
            return false;
        }

        folded = raw;
        return true;
    }

    /// <summary>Ephemeral, all-nullable projection of an untrusted <c>.ad-pack.json</c> document.</summary>
    sealed record CatalogAdPackManifestJson
    {
        public string? PackName { get; init; }
        public IReadOnlyList<CatalogAdPackBriefJson>? Briefs { get; init; }
    }

    /// <summary>Ephemeral, all-nullable projection of one raw <c>briefs[]</c> element.</summary>
    sealed record CatalogAdPackBriefJson
    {
        public string? Brand { get; init; }
        public string? Premise { get; init; }
        public string? Tone { get; init; }
        public string? Structure { get; init; }
    }
}
