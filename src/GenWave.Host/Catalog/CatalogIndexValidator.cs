namespace GenWave.Host.Catalog;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GenWave.Host.Theming;

/// <summary>
/// Pure, I/O-free validation of a fetched index.json payload against SPEC F90.2's strict shape
/// rules — no HTTP, no caching, no logging. <see cref="CatalogProxyService"/> is the only caller,
/// and owns turning a rejection into the one WARN log line F90.2 asks for; this class only ever
/// answers "is this shape trustworthy", never "should I serve/cache it" (single responsibility,
/// and independently testable without a fake HTTP handler in play).
///
/// <para>
/// THE `kind` SEAM (SPEC F103.1, widened to <c>"font"</c> by F104.1): each entry declares a
/// <c>kind</c> (<c>"persona"</c> | <c>"theme"</c> | <c>"font"</c>); a missing field defaults to
/// <see cref="CatalogEntryKind.Persona"/> (back-compat for every entry authored before the field
/// existed). A <c>kind</c> naming none of these is forward-compat, not fatal — that ONE entry is
/// silently dropped and the rest of the index still loads (<see cref="TryValidateEntry"/>'s own
/// early return) — deliberately unlike an unrecognised <c>audience</c> below, which still rejects
/// the WHOLE index (audience is content-safety; kind is forward-compat). The per-kind manifest
/// file pattern (<see cref="PersonaManifestPathPattern"/> / <see cref="ThemeManifestPathPattern"/> /
/// <see cref="FontManifestPathPattern"/>) is picked only once an entry's kind is known. A font
/// entry ALSO carries <c>assets[]</c> (SPEC F104.1) — validated once the manifest/meta refs pass,
/// with its own reject-vs-degrade posture: see <see cref="TryValidateAssets"/>'s own remarks.
/// </para>
///
/// <para>
/// THE SSRF BOUNDARY THIS CLASS ENFORCES (see <see cref="CatalogProxyService"/>'s own remarks for
/// the full ruling): the operator-controlled index URL is trusted, but REMOTE content — the index
/// body itself — never chooses a fetch target. Every entry path is checked against the entry's own
/// manifest pattern / <see cref="MetaPathPattern"/> (no absolute URL, no <c>..</c>, no leading
/// <c>/</c> — the pattern shape itself rules all three out, it can never match anything but a plain
/// <c>entries/&lt;slug&gt;/&lt;name&gt;.(persona|theme|meta).json</c> relative path), that its own
/// <c>&lt;slug&gt;</c> segment equals the entry's own declared slug, and then resolved ONLY against
/// the index URL's own directory, with a second, independent "still starts with that directory"
/// check on the resolved absolute URI (<see cref="TryResolveWithinDirectory"/>) — belt and braces,
/// SPEC F90.2. A single bad entry rejects the WHOLE index, never just that one entry.
/// </para>
/// </summary>
internal static partial class CatalogIndexValidator
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    // The one slug shape (schemas/index.schema.json's own `slug`/directory-segment pattern) — used
    // for the top-level `slug` field AND both path-segment positions below, so it exists exactly
    // ONCE as source text (review finding: was inlined three times). A `const` (not `static
    // readonly`) because [GeneratedRegex] attribute arguments must be compile-time constants;
    // string `+` on `const string` operands is itself a compile-time constant expression, so this
    // still composes normally into the manifest/meta path text below. `internal` (T101 review
    // finding): this IS the catalog's own slug vocabulary — CatalogController.SlugFormat composes
    // its route-parameter check from this same const rather than inventing a second copy of the shape.
    internal const string SlugSegment = "[a-z0-9]+(-[a-z0-9]+)*";

    // entries/<slug>/<name>.persona.json / entries/<slug>/<name>.theme.json / entries/<slug>/<name>.font.json
    // — the per-kind manifest shape (SPEC F103.2, F104.1): the filename segment is the SAME shape
    // as the slug segment (SPEC F90.2/F89.2: schemas/index.schema.json's card/meta path patterns
    // use this one shape for both segments, not the looser "any run of [a-z0-9-]" a prior version
    // allowed here, which would have tolerated a leading/trailing/doubled hyphen the real schema
    // rejects).
    const string PersonaManifestPathText = @"\Aentries/" + SlugSegment + "/" + SlugSegment + @"\.persona\.json\z";
    const string ThemeManifestPathText = @"\Aentries/" + SlugSegment + "/" + SlugSegment + @"\.theme\.json\z";
    const string FontManifestPathText = @"\Aentries/" + SlugSegment + "/" + SlugSegment + @"\.font\.json\z";
    const string MetaPathText = @"\Aentries/" + SlugSegment + "/" + SlugSegment + @"\.meta\.json\z";

    // entries/<slug>/<filename> — a font pack's binary asset (SPEC F104.1): 1-2 latin-subsetted
    // woff2 faces and the pack's OFL licence text, sitting alongside (never inside) its
    // <slug>.font.json manifest. UNLIKE the manifest/meta filename segment above (which must equal
    // the slug itself), an asset's filename is the pack's OWN file name (e.g.
    // "space-grotesk-variable-latin.woff2", "OFL.txt") — so this pattern constrains character set
    // and extension only, not the slug shape. It still gives the SAME SSRF-shaped guarantee (no
    // absolute URL, no scheme, no leading slash, no ".." traversal): no '/' appears anywhere in the
    // character class, so a value can never introduce a second path segment to traverse with, and
    // the leading-character class rules out a value starting with '.'. Extensions are the closed
    // set F104.1 actually ships — woff2 (the subsetted faces) and txt (the licence file) — anything
    // else is a shape this app does not expect a font pack to carry.
    const string AssetFileNameText = @"[A-Za-z0-9][A-Za-z0-9._-]*\.(?:woff2|txt)";
    const string AssetPathText = @"\Aentries/" + SlugSegment + "/" + AssetFileNameText + @"\z";

    [GeneratedRegex(@"\A" + SlugSegment + @"\z")]
    private static partial Regex SlugPattern();

    [GeneratedRegex(@"\A[a-f0-9]{64}\z")]
    private static partial Regex Sha256Pattern();

    // The SAME hex-colour shape ThemeManifestParser enforces on a theme manifest's own token
    // values (review finding, T185) — composed from its `internal const`, not a second copy, so a
    // shelf-preview swatch off an untrusted index.json is held to the exact rule a manifest's real
    // colour tokens already are, rather than reaching the wire (and an inline CSS `style` attribute
    // in the Admin UI) unchecked. See ThemeManifestParser.TokenValueText's own remarks.
    [GeneratedRegex(ThemeManifestParser.TokenValueText)]
    private static partial Regex SwatchHexPattern();

    // Split per kind (and per field) so a manifest can't masquerade as a meta (or vice versa), and
    // a persona's manifest can't masquerade as a theme's (or vice versa), even though all three
    // share the same entries/<slug>/<name>.EXT.json shape.
    [GeneratedRegex(PersonaManifestPathText)]
    private static partial Regex PersonaManifestPathPattern();

    [GeneratedRegex(ThemeManifestPathText)]
    private static partial Regex ThemeManifestPathPattern();

    [GeneratedRegex(FontManifestPathText)]
    private static partial Regex FontManifestPathPattern();

    [GeneratedRegex(MetaPathText)]
    private static partial Regex MetaPathPattern();

    [GeneratedRegex(AssetPathText)]
    private static partial Regex AssetPathPattern();

    /// <summary>
    /// Parses and strictly validates a raw index.json payload. On success, every returned
    /// <see cref="CatalogEntrySummary"/> has already passed kind/slug/audience/sha256-shape/path
    /// validation AND the belt-and-braces directory-prefix check — an entry naming an unrecognised
    /// <c>kind</c> is simply absent from the result (F103.1, forward-compat), never a rejection
    /// reason. On failure, the WHOLE index is rejected — <paramref name="rejectionReason"/> names
    /// the first offending value (a malformed entry path names that exact path, per F90.2).
    /// </summary>
    public static bool TryValidate(
        byte[] json, Uri directory,
        [NotNullWhen(true)] out IReadOnlyList<CatalogEntrySummary>? entries,
        [NotNullWhen(false)] out string? rejectionReason)
    {
        CatalogIndexJson? document;
        try
        {
            document = JsonSerializer.Deserialize<CatalogIndexJson>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            entries = null;
            rejectionReason = $"malformed JSON ({ex.Message})";
            return false;
        }

        if (document?.Entries is not { } rawEntries)
        {
            entries = null;
            rejectionReason = "missing an 'entries' array";
            return false;
        }

        var validated = new List<CatalogEntrySummary>(rawEntries.Count);
        foreach (var raw in rawEntries)
        {
            switch (TryValidateEntry(raw, directory, out var summary, out var entryRejectionReason))
            {
                case EntryValidationOutcome.Valid:
                    validated.Add(summary
                        ?? throw new UnreachableException($"{nameof(EntryValidationOutcome.Valid)} without a summary."));
                    break;

                case EntryValidationOutcome.Skip:
                    // F103.1/AC6: an entry naming a kind this app does not recognise is dropped —
                    // forward-compat for a future font/icon/avatar kind — the rest of the index
                    // still loads.
                    break;

                case EntryValidationOutcome.Reject:
                default:
                    entries = null;
                    rejectionReason = entryRejectionReason
                        ?? throw new UnreachableException($"{nameof(EntryValidationOutcome.Reject)} without a reason.");
                    return false;
            }
        }

        entries = validated;
        rejectionReason = null;
        return true;
    }

    /// <summary>
    /// Resolves <paramref name="relativePath"/> against <paramref name="directory"/> and verifies
    /// the result still lives under it (SPEC F90.2's belt-and-braces rule). Every entry path is
    /// already gated by the entry's own manifest pattern / <see cref="MetaPathPattern"/> before this
    /// is ever called — which, by construction (no <c>..</c>, no scheme, no leading <c>/</c>), makes
    /// an escape here unreachable through the public <c>GetIndexAsync</c>/<c>GetEntryAsync</c>
    /// surface. Kept as its own independently testable method (rather than inlined) so this SECOND,
    /// independent layer is pinned directly — mirrors T99's own "test the seam directly" idiom for
    /// <c>SettingValidator</c>.
    /// </summary>
    internal static bool TryResolveWithinDirectory(Uri directory, string relativePath, [NotNullWhen(true)] out Uri? resolved)
    {
        resolved = new Uri(directory, relativePath);
        return resolved.AbsoluteUri.StartsWith(directory.AbsoluteUri, StringComparison.Ordinal);
    }

    /// <summary>One raw entry's fate: kept, silently dropped (forward-compat), or fatal to the whole index.</summary>
    enum EntryValidationOutcome
    {
        Valid,
        Skip,
        Reject,
    }

    static EntryValidationOutcome TryValidateEntry(
        CatalogIndexEntryJson raw, Uri directory,
        out CatalogEntrySummary? summary, out string? reason)
    {
        summary = null;
        reason = null;

        // Kind is resolved FIRST (F103.1): an entry naming a kind this app doesn't recognise might
        // be shaped in a way no persona/theme/font rule below can meaningfully validate (a future
        // icon/avatar) — it is skipped outright, before slug/audience/manifest are even looked at,
        // and never counts toward rejecting the rest of the index.
        if (!TryResolveKind(raw.Kind, out var kind))
            return EntryValidationOutcome.Skip;

        if (raw.Slug is not { } slug || !SlugPattern().IsMatch(slug))
        {
            reason = $"invalid slug '{raw.Slug}'";
            return EntryValidationOutcome.Reject;
        }

        CatalogAudience audience;
        switch (raw.Audience)
        {
            case "everyone":
                audience = CatalogAudience.Everyone;
                break;
            case "mature":
                audience = CatalogAudience.Mature;
                break;
            default:
                reason = $"entry '{slug}' has an invalid audience '{raw.Audience}'";
                return EntryValidationOutcome.Reject;
        }

        // `raw.Manifest` is the F103.2 field name; `raw.Card` is the legacy persona-only wire name
        // the live genwave-catalog origin still emits today (T178 migrates it) — accepting either
        // is what keeps today's real catalog (and every existing persona-catalog spec) working
        // unchanged while `manifest` becomes the one name every future kind targets from day one.
        if (!TryValidateFileRef(raw.Manifest ?? raw.Card, ManifestPathPattern(kind), slug, directory, out var manifest, out var manifestReason))
        {
            reason = $"entry '{slug}' manifest {manifestReason}";
            return EntryValidationOutcome.Reject;
        }

        if (!TryValidateFileRef(raw.Meta, MetaPathPattern(), slug, directory, out var meta, out var metaReason))
        {
            reason = $"entry '{slug}' meta {metaReason}";
            return EntryValidationOutcome.Reject;
        }

        // F104.1: only a font entry carries assets[] at all — persona/theme entries always resolve
        // to the empty list (CatalogEntrySummary.Assets's own "absent means empty" remarks). A font
        // entry whose assets[] is missing, empty, or contains anything malformed is skipped OUTRIGHT
        // (never rejects the whole index) — see TryValidateAssets's own remarks for why this is a
        // whole-entry skip rather than a field-level degrade like Preview.
        IReadOnlyList<CatalogAssetRef> assets;
        if (kind == CatalogEntryKind.Font)
        {
            if (!TryValidateAssets(raw.Assets, slug, directory, out var fontAssets))
                return EntryValidationOutcome.Skip;

            assets = fontAssets;
        }
        else
        {
            assets = [];
        }

        summary = new CatalogEntrySummary(slug, kind, audience, raw.BestFor ?? [], manifest, meta, TryParsePreview(raw.Preview), assets);
        return EntryValidationOutcome.Valid;
    }

    /// <summary>
    /// Admits the optional <c>preview</c> object (SPEC F103.4, T185's contract) with the SAME
    /// tolerant posture <see cref="BestFor"/> already has on this class's own raw entries — but,
    /// unlike <c>BestFor</c>, a shape this deliberately permissive needs TWO separate defences
    /// (review findings, T185):
    ///
    /// <para>
    /// (1) <paramref name="raw"/> is read as a raw <see cref="JsonElement"/>, not the typed
    /// <see cref="CatalogPreviewJson"/> record directly — a wrong-typed <c>preview</c> (a number,
    /// string, or array, or a wrong-typed leaf inside an otherwise-shaped object) must never throw
    /// out of the single top-level <c>Deserialize</c> call in <see cref="TryValidate"/> that parses
    /// the WHOLE index — that would reject every entry, persona and theme alike, over one decorative
    /// field. Any shape that can't convert into <see cref="CatalogPreviewJson"/> is caught here and
    /// degrades ONLY this entry's preview to <see langword="null"/>.
    /// </para>
    ///
    /// <para>
    /// (2) Each swatch value that DOES deserialize as a string is still checked against
    /// <see cref="SwatchHexPattern"/> (<see cref="ThemeManifestParser.TokenValueText"/>) before it is
    /// trusted — index.json is remote, untrusted content, same as the manifest/meta paths above, and
    /// this field reaches the wire (and an inline CSS <c>style</c> attribute in the Admin UI)
    /// verbatim; a non-hex string (e.g. <c>'red;background-image:url(...)'</c>) never reaches a
    /// caller.
    /// </para>
    ///
    /// A field that is simply absent (every pre-T185 index, and every persona entry), or present but
    /// missing a mode, a swatch key, or a valid hex value, resolves to <see langword="null"/> rather
    /// than rejecting the whole index over a decorative shelf field — unlike an unrecognised
    /// <c>audience</c>, malformed preview data is cosmetic (the catalog schema's own remarks: "a
    /// stale swatch is a shelf cosmetic issue, not a broadcast one"), so an older or lightly-broken
    /// index must still serve every other entry, and this one entry still shows its name with no
    /// chips.
    /// </summary>
    static CatalogThemePreview? TryParsePreview(JsonElement? raw)
    {
        if (raw is not { ValueKind: JsonValueKind.Object } element)
            return null;

        CatalogPreviewJson? preview;
        try
        {
            preview = element.Deserialize<CatalogPreviewJson>(JsonOptions);
        }
        catch (JsonException)
        {
            // A shape Deserialize can't convert (e.g. `light`/`dark` present but not an object, or
            // a leaf like `bg` typed as a number) — cosmetic, not fatal; see this method's own
            // remarks.
            return null;
        }

        if (preview is not { Light: { } light, Dark: { } dark })
            return null;

        if (TryParseSwatchSet(light) is not { } lightSwatches || TryParseSwatchSet(dark) is not { } darkSwatches)
            return null;

        return new CatalogThemePreview(lightSwatches, darkSwatches);
    }

    static CatalogThemeSwatchSet? TryParseSwatchSet(CatalogSwatchSetJson raw)
    {
        if (raw is not
            {
                Bg: { } bg, Surface: { } surface, Ink: { } ink, Accent: { } accent, Accent2: { } accent2,
            })
            return null;

        if (!SwatchHexPattern().IsMatch(bg) || !SwatchHexPattern().IsMatch(surface) || !SwatchHexPattern().IsMatch(ink)
            || !SwatchHexPattern().IsMatch(accent) || !SwatchHexPattern().IsMatch(accent2))
            return null;

        return new CatalogThemeSwatchSet(bg, surface, ink, accent, accent2);
    }

    /// <summary>
    /// Validates a font entry's whole <c>assets[]</c> array (SPEC F104.1, T193) — REJECT-VS-DEGRADE
    /// POSTURE: unlike <see cref="TryParsePreview"/>'s field-level degrade (a bad <c>preview</c>
    /// nulls just that one decorative field, keeping the rest of the entry), a malformed or absent
    /// assets list here fails the WHOLE entry, which <see cref="TryValidateEntry"/> then SKIPS
    /// (never rejects the whole index — that stays reserved for slug/audience/manifest/meta shape
    /// failures). The difference: a pack IS its files — an entry admitted with an empty or
    /// partly-broken assets list would be a shelf card advertising a font nothing can actually
    /// serve, a strictly worse outcome than the entry simply not existing yet. So this is
    /// ALL-OR-NOTHING: every declared asset must individually validate (<see cref="TryValidateAssetRef"/>)
    /// and at least one must be present, or the caller treats the entire font entry as absent.
    ///
    /// <para>
    /// <paramref name="raw"/> is a raw <see cref="JsonElement"/>, not the typed
    /// <see cref="CatalogAssetJson"/> array directly (S2 review finding — the exact T185
    /// <c>preview</c> trap, reintroduced here: an <c>assets</c> shaped as an object instead of an
    /// array, or containing anything malformed, used to throw straight out of the top-level
    /// <c>Deserialize</c> call in <see cref="TryValidate"/> and reject the WHOLE index over one
    /// kind's own field). ONLY an array shape is even considered here — anything else (an object, a
    /// string, a number) fails this whole-entry check immediately, same as an empty array; each
    /// element's own shape is then re-validated defensively, element by element, inside
    /// <see cref="TryValidateAssetRef"/>.
    /// </para>
    /// </summary>
    static bool TryValidateAssets(
        JsonElement? raw, string slug, Uri directory,
        [NotNullWhen(true)] out IReadOnlyList<CatalogAssetRef>? assets)
    {
        if (raw is not { ValueKind: JsonValueKind.Array } array)
        {
            assets = null;
            return false;
        }

        var validated = new List<CatalogAssetRef>(array.GetArrayLength());
        foreach (var element in array.EnumerateArray())
        {
            if (!TryValidateAssetRef(element, slug, directory, out var assetRef))
            {
                assets = null;
                return false;
            }

            validated.Add(assetRef);
        }

        if (validated.Count == 0)
        {
            assets = null;
            return false;
        }

        assets = validated;
        return true;
    }

    /// <summary>
    /// One <c>assets[]</c> element's shape check (SPEC F104.1) — the same SSRF-shaped belt-and-braces
    /// rules <see cref="TryValidateFileRef"/> applies to a manifest/meta pointer (path shape, slug
    /// ownership, directory containment), plus a positive <see cref="CatalogAssetJson.Bytes"/> (the
    /// fetch transport's declared size cap, T194 — zero or negative names nothing a caller could
    /// ever stream). S4 review finding: rather than re-implementing that whole belt-and-braces
    /// check a second time, this calls <see cref="TryValidateFileRef"/> ITSELF — the one place that
    /// security-critical traversal/SSRF logic lives — passing <see cref="AssetPathPattern"/> in place
    /// of a manifest/meta pattern and discarding its WARN-worthy reason string (unlike
    /// <see cref="TryValidateFileRef"/>'s callers, which reject the whole index and so need one, a
    /// bad asset here degrades/skips its OWN entry silently — the same no-reason shape
    /// <see cref="TryValidateEntry"/>'s unknown-kind <c>Skip</c> outcome already carries).
    ///
    /// <para>
    /// <paramref name="element"/> is a raw <see cref="JsonElement"/>, not the typed
    /// <see cref="CatalogAssetJson"/> directly (S2 review finding, mirrors <see cref="TryParsePreview"/>'s
    /// own defence): a shape <c>Deserialize&lt;CatalogAssetJson&gt;</c> can't convert (a non-object
    /// element, a <c>bytes</c> leaf typed as a string, or a <c>bytes</c> value overflowing even
    /// <see cref="long"/>) is caught here and fails only THIS asset — <see cref="TryValidateAssets"/>'s
    /// own all-or-nothing posture is what turns that into a whole-entry skip, never a whole-index
    /// rejection.
    /// </para>
    /// </summary>
    static bool TryValidateAssetRef(
        JsonElement element, string slug, Uri directory, [NotNullWhen(true)] out CatalogAssetRef? assetRef)
    {
        CatalogAssetJson? raw;
        try
        {
            raw = element.Deserialize<CatalogAssetJson>(JsonOptions);
        }
        catch (JsonException)
        {
            // A shape Deserialize can't convert (e.g. an element that isn't an object at all, or a
            // `bytes` leaf typed as a string or overflowing long) — this one asset is simply
            // invalid; see this method's own remarks.
            assetRef = null;
            return false;
        }

        if (raw is not { Bytes: { } bytes } || bytes <= 0)
        {
            assetRef = null;
            return false;
        }

        if (!TryValidateFileRef(
                new CatalogFileRefJson { Path = raw.Path, Sha256 = raw.Sha256 },
                AssetPathPattern(), slug, directory, out var fileRef, out _))
        {
            assetRef = null;
            return false;
        }

        assetRef = new CatalogAssetRef(fileRef.Path, fileRef.Sha256, bytes);
        return true;
    }

    /// <summary>A missing <c>kind</c> defaults to persona (back-compat, F103.1/AC2); any value other than <c>"persona"</c>/<c>"theme"</c>/<c>"font"</c> is unrecognised.</summary>
    static bool TryResolveKind(string? raw, out CatalogEntryKind kind)
    {
        switch (raw)
        {
            case null:
            case "persona":
                kind = CatalogEntryKind.Persona;
                return true;
            case "theme":
                kind = CatalogEntryKind.Theme;
                return true;
            case "font":
                kind = CatalogEntryKind.Font;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    static Regex ManifestPathPattern(CatalogEntryKind kind) => kind switch
    {
        CatalogEntryKind.Persona => PersonaManifestPathPattern(),
        CatalogEntryKind.Theme => ThemeManifestPathPattern(),
        CatalogEntryKind.Font => FontManifestPathPattern(),
        _ => throw new UnreachableException($"Unhandled {nameof(CatalogEntryKind)} value: {kind}."),
    };

    static bool TryValidateFileRef(
        CatalogFileRefJson? raw, Regex pathPattern, string slug, Uri directory,
        [NotNullWhen(true)] out CatalogFileRef? fileRef,
        [NotNullWhen(false)] out string? reason)
    {
        if (raw is not { Path: { } path, Sha256: { } sha256 })
        {
            fileRef = null;
            reason = "is missing a path/sha256";
            return false;
        }

        // The ONE shape check that rules out an absolute URL, a scheme, a leading slash, and any
        // ".." traversal all at once — the pattern cannot match anything but a plain relative
        // entries/<slug>/<name>.(persona|theme|meta).json path (SPEC F90.2). The offending raw path
        // is named verbatim in the reason the caller logs as the required WARN.
        if (!pathPattern.IsMatch(path))
        {
            fileRef = null;
            reason = $"path '{path}' is not a valid relative entries/ path";
            return false;
        }

        // The path's own <slug> segment must equal the entry's declared `slug` field (review
        // finding) — otherwise a manifest/meta could sit under a DIFFERENT entry's directory (still
        // regex-valid, still resolving under the SAME index directory, so the belt-and-braces check
        // below would never catch it) while being advertised under this one's slug/audience/bestFor.
        // The pattern already guarantees exactly two '/'-delimited segments after "entries/", so
        // splitting by index is safe.
        var pathSlug = path.Split('/')[1];
        if (!string.Equals(pathSlug, slug, StringComparison.Ordinal))
        {
            fileRef = null;
            reason = $"path '{path}' does not belong to slug '{slug}'";
            return false;
        }

        if (!Sha256Pattern().IsMatch(sha256))
        {
            fileRef = null;
            reason = $"path '{path}' has a malformed sha256";
            return false;
        }

        if (!TryResolveWithinDirectory(directory, path, out _))
        {
            fileRef = null;
            reason = $"path '{path}' resolves outside the index directory";
            return false;
        }

        fileRef = new CatalogFileRef(path, sha256);
        reason = null;
        return true;
    }

    /// <summary>Ephemeral JSON projection of the untrusted index.json payload — validated field by field above, then discarded.</summary>
    sealed record CatalogIndexJson
    {
        public IReadOnlyList<CatalogIndexEntryJson>? Entries { get; init; }
    }

    /// <summary>Ephemeral JSON projection of one raw index.json entry, all-nullable — nothing here is trusted until <see cref="TryValidateEntry"/> says so.</summary>
    sealed record CatalogIndexEntryJson
    {
        public string? Slug { get; init; }

        /// <summary><c>"persona"</c> | <c>"theme"</c> | <c>"font"</c> (SPEC F103.1, F104.1); absent means persona (back-compat).</summary>
        public string? Kind { get; init; }

        public string? Audience { get; init; }
        public IReadOnlyList<string>? BestFor { get; init; }

        /// <summary>The F103.2 field name every kind targets going forward.</summary>
        public CatalogFileRefJson? Manifest { get; init; }

        /// <summary>Legacy persona-only wire name — see <see cref="TryValidateEntry"/>'s own remarks on why this is still read.</summary>
        public CatalogFileRefJson? Card { get; init; }

        public CatalogFileRefJson? Meta { get; init; }

        /// <summary>
        /// The optional F103.4 shelf-preview payload (T185) — a raw <see cref="JsonElement"/>, not
        /// the typed <see cref="CatalogPreviewJson"/> directly (review finding): a wrong-typed
        /// <c>preview</c> in an untrusted index.json (a number, string, array, or a wrong-typed
        /// leaf) must never fail the top-level <c>Deserialize</c> call this record is itself a
        /// member of — that would reject the WHOLE index over one decorative field. See
        /// <see cref="TryParsePreview"/>.
        /// </summary>
        public JsonElement? Preview { get; init; }

        /// <summary>
        /// The F104.1 asset list — only ever present (and only ever meaningful) on a
        /// <c>kind:"font"</c> entry; absent on every persona/theme entry, matching
        /// <see cref="CatalogEntrySummary.Assets"/>'s own "absent means empty" posture once
        /// validated. A raw <see cref="JsonElement"/>, not the typed <see cref="CatalogAssetJson"/>
        /// array directly (S2 review finding, mirrors <see cref="Preview"/>'s own remarks
        /// immediately above): a wrong-typed <c>assets</c> (an object instead of an array, a
        /// non-object element, a malformed <c>bytes</c> leaf) must never fail the top-level
        /// <c>Deserialize</c> call this record is itself a member of — that would reject the WHOLE
        /// index over one kind's own field. See <see cref="TryValidateAssets"/> for the whole-entry
        /// reject-vs-degrade posture a malformed or empty list still carries once parsed defensively.
        /// </summary>
        public JsonElement? Assets { get; init; }
    }

    /// <summary>Ephemeral JSON projection of a raw index.json <c>manifest</c>/<c>card</c>/<c>meta</c> file pointer.</summary>
    sealed record CatalogFileRefJson
    {
        public string? Path { get; init; }
        public string? Sha256 { get; init; }
    }

    /// <summary>Ephemeral JSON projection of one raw <c>assets[]</c> entry (SPEC F104.1) — adds
    /// <see cref="Bytes"/> on top of <see cref="CatalogFileRefJson"/>'s path/sha256 shape, since a
    /// font asset's declared size is what the fetch transport (T194) size-caps a stream against.
    /// <see cref="Bytes"/> is <see cref="long"/>, not <see cref="int"/> (S2 review finding — a real
    /// byte count is a <see cref="long"/>-shaped quantity house-wide, e.g. <see cref="Stream.Length"/>):
    /// widening it here means a declared size that merely overflows <see cref="int"/> (still a
    /// syntactically ordinary JSON integer) parses as an ordinary, if oversize, value that
    /// <see cref="TryValidateAssetRef"/> can inspect and reject on its own terms, rather than a value
    /// that throws mid-deserialize purely because of the field's own narrower type. Only a value
    /// overflowing <see cref="long"/> itself still throws — caught defensively the same as any other
    /// malformed asset shape (<see cref="TryValidateAssetRef"/>'s own remarks).</summary>
    sealed record CatalogAssetJson
    {
        public string? Path { get; init; }
        public string? Sha256 { get; init; }
        public long? Bytes { get; init; }
    }

    /// <summary>Ephemeral JSON projection of a raw index.json entry's <c>preview</c> object (SPEC F103.4).</summary>
    sealed record CatalogPreviewJson
    {
        public CatalogSwatchSetJson? Light { get; init; }
        public CatalogSwatchSetJson? Dark { get; init; }
    }

    /// <summary>
    /// Ephemeral JSON projection of one raw <c>light</c>/<c>dark</c> swatch set — <see cref="Accent2"/>
    /// carries the wire name <c>accent-2</c> (genwave-catalog's <c>theme-meta.schema.json</c>), the
    /// one place in this projection that key name is spelled.
    /// </summary>
    sealed record CatalogSwatchSetJson
    {
        public string? Bg { get; init; }
        public string? Surface { get; init; }
        public string? Ink { get; init; }
        public string? Accent { get; init; }

        [JsonPropertyName("accent-2")]
        public string? Accent2 { get; init; }
    }
}
