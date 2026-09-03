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
/// and owns turning a whole-index rejection into the one WARN log line F90.2 asks for, PLUS
/// (round-1 review findings 1/3, PLAN T292) turning each returned <see cref="CatalogValidationNotice"/>
/// into its own WARN line; this class only ever answers "is this shape trustworthy, and what
/// non-fatal issues did I notice along the way", never "should I serve/cache it, or log about it"
/// (single responsibility, and independently testable without a fake HTTP handler in play).
///
/// <para>
/// THE `kind` SEAM (SPEC F103.1, widened to <c>"font"</c> by F104.1, <c>"show"</c> by F118.1): each
/// entry declares a <c>kind</c> (<c>"persona"</c> | <c>"theme"</c> | <c>"font"</c> | <c>"show"</c>); a
/// missing field defaults to <see cref="CatalogEntryKind.Persona"/> (back-compat for every entry
/// authored before the field existed). A <c>kind</c> naming none of these is forward-compat, not fatal
/// — that ONE entry is silently dropped and the rest of the index still loads
/// (<see cref="TryValidateEntry"/>'s own early return) — deliberately unlike an unrecognised
/// <c>audience</c> below, which still rejects the WHOLE index (audience is content-safety; kind is
/// forward-compat). The per-kind manifest file pattern (<see cref="PersonaManifestPathPattern"/> /
/// <see cref="ThemeManifestPathPattern"/> / <see cref="FontManifestPathPattern"/> /
/// <see cref="ShowManifestPathPattern"/>) is picked only once an entry's kind is known. A font entry
/// ALSO carries <c>assets[]</c> (SPEC F104.1) — validated once the manifest/meta refs pass, with its
/// own reject-vs-degrade posture: see <see cref="TryValidateAssets"/>'s own remarks. A show entry
/// carries no assets/family/preview at all — the same minimal <c>{manifest, meta}</c> shape a persona
/// entry has, just under the <c>.show.json</c> manifest pattern.
/// </para>
///
/// <para>
/// WIDENED TO <c>"ad-pack"</c> (SPEC F162.2, STORY-393, PLAN T405): the SIMPLEST kind yet — the same
/// minimal <c>{manifest, meta}</c> shape a show/icon entry has, no binary <c>assets[]</c> arm at all
/// (an ad-pack's whole body, <c>briefs[]</c>, is manifest DATA, never a file the index declares
/// separately). Everything this class checks about an ad-pack entry is the SAME belt-and-braces path/
/// slug/directory shape every other kind already gets — <see cref="AdPackManifestPathPattern"/>,
/// picked once <see cref="TryResolveKind"/> resolves <c>"ad-pack"</c>. The manifest's own DEEPER
/// shape (brief count/field-length caps, since installed briefs become durable
/// <c>station.ad_brief</c> rows) is <see cref="CatalogAdPackManifestSerializer"/>'s job, consulted
/// only once this class's own shape gate already passed — the SAME "index validator proves shape,
/// manifest serializer proves content" split <see cref="CatalogFontManifestSerializer"/> already has
/// one kind over.
/// </para>
///
/// <para>
/// WIDENED TO <c>"avatar"</c> AND <c>"icon"</c> (SPEC F128.1, F130.6, PLAN T292): an avatar pack is
/// the SECOND assets-carrying kind — one PNG per pack item riding <c>assets[]</c>, the exact same
/// all-or-nothing "a pack IS its files" posture <see cref="CatalogEntryKind.Font"/> already has (see
/// <see cref="TryValidateAssets"/>). An icon pack carries no binary assets at all — the same minimal
/// <c>{manifest, meta}</c> shape a show entry has. SEPARATELY (SPEC F128.2), a PERSONA entry may now
/// ALSO carry <c>assets[]</c> — at most ONE <c>&lt;slug&gt;.avatar.png</c>, the "this DJ ships with
/// this face" sidecar — a GENUINELY OPTIONAL field with its OWN three-rung ladder (round-1 review
/// findings 1/3, STORY-331 AC3/AC6), deliberately unlike a pack's own all-or-nothing assets: 0 or 1
/// well-formed asset validates normally (absent/empty both mean "no face"); a SINGLE malformed asset
/// (bad path/sha256/bytes/filename, or a non-array <c>assets</c> value) DEGRADES to no face with a
/// WARN — the entry still lists (SPEC F128.9's own "absent ⇒ a neutral placeholder" posture, mirrored
/// here rather than losing the whole DJ over one broken sidecar); TWO OR MORE declared assets — a
/// genuine cardinality violation, never incidental JSON noise — WITHHOLDS just that one entry with a
/// WARN naming the one-face rule, while the REST OF THE SHELF still lists (STORY-331 AC6's own words:
/// "that ENTRY is rejected", never the whole index — see <see cref="TryValidatePersonaAvatarAsset"/>'s
/// own remarks for the exact ladder).
/// </para>
///
/// <para>
/// THE SSRF BOUNDARY THIS CLASS ENFORCES (see <see cref="CatalogProxyService"/>'s own remarks for
/// the full ruling): the operator-controlled index URL is trusted, but REMOTE content — the index
/// body itself — never chooses a fetch target. Every entry path is checked against the entry's own
/// manifest pattern / <see cref="MetaPathPattern"/> (no absolute URL, no <c>..</c>, no leading
/// <c>/</c> — the pattern shape itself rules all three out, it can never match anything but a plain
/// <c>entries/[&lt;kind-plural&gt;/]&lt;slug&gt;/&lt;name&gt;.(persona|theme|meta).json</c> relative
/// path — both shelf layouts, genwave-catalog#33), that its own <c>&lt;slug&gt;</c> segment (always
/// the second-to-last) equals the entry's own declared slug, and then resolved ONLY against
/// the index URL's own directory, with a second, independent "still starts with that directory"
/// check on the resolved absolute URI (<see cref="TryResolveWithinDirectory"/>) — belt and braces,
/// SPEC F90.2. A single bad MANIFEST/META path rejects the WHOLE index, never just that one entry —
/// the one exception is narrower still, and lives entirely in the WIDENED TO paragraph above: a
/// persona entry's own two-or-more-avatar-assets shape withholds only that entry.
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

    // The OPTIONAL per-kind folder segment (genwave-catalog#33): the shelf repo is migrating from a
    // flat entries/<slug>/ tree to per-kind entries/<kind-plural>/<slug>/ folders, and this app must
    // admit BOTH layouts through the transition (the old one is what the live origin serves today;
    // the new one is what it serves after the move). When the folder IS present it must name the
    // entry's OWN kind — a persona manifest under entries/shows/ is a lie about what the file is,
    // not an alternative layout, and fails the persona pattern outright. The folder set is CLOSED
    // (the SEVEN kinds this app recognises, widened from four by F128.1/F130.6/T292 and from six by
    // F162.2/T405), mirroring TryResolveKind: an unrecognised-kind entry is already skipped before
    // any path pattern is consulted, so an unrecognised kind FOLDER can only ever appear on a
    // known-kind entry — where it is exactly the mismatch case above.
    const string PersonaFolderText = "(?:personas/)?";
    const string ThemeFolderText = "(?:themes/)?";
    const string FontFolderText = "(?:fonts/)?";
    const string ShowFolderText = "(?:shows/)?";
    const string AvatarFolderText = "(?:avatars/)?";
    const string IconFolderText = "(?:icons/)?";
    const string AdPackFolderText = "(?:ad-packs/)?";
    const string AnyKindFolderText = "(?:(?:personas|themes|fonts|shows|avatars|icons|ad-packs)/)?";

    // entries/[<kind-plural>/]<slug>/<name>.persona.json (and .theme/.font/.show/.avatar/.icon/
    // .ad-pack) — the per-kind manifest shape (SPEC F103.2, F104.1, F118.1, F128.1, F130.6, F162.2):
    // the filename segment is the SAME shape as the slug segment (SPEC F90.2/F89.2:
    // schemas/index.schema.json's card/meta path patterns use this one shape for both segments, not
    // the looser "any run of [a-z0-9-]" a prior version allowed here, which would have tolerated a
    // leading/trailing/doubled hyphen the real schema rejects). The meta pattern alone takes the
    // ANY-kind folder alternation — a meta filename carries no kind of its own — with the
    // manifest-directory equality check in TryValidateEntry pinning it to the one folder its entry
    // actually lives in.
    const string PersonaManifestPathText = @"\Aentries/" + PersonaFolderText + SlugSegment + "/" + SlugSegment + @"\.persona\.json\z";
    const string ThemeManifestPathText = @"\Aentries/" + ThemeFolderText + SlugSegment + "/" + SlugSegment + @"\.theme\.json\z";
    const string FontManifestPathText = @"\Aentries/" + FontFolderText + SlugSegment + "/" + SlugSegment + @"\.font\.json\z";
    const string ShowManifestPathText = @"\Aentries/" + ShowFolderText + SlugSegment + "/" + SlugSegment + @"\.show\.json\z";
    const string AvatarManifestPathText = @"\Aentries/" + AvatarFolderText + SlugSegment + "/" + SlugSegment + @"\.avatar\.json\z";
    const string IconManifestPathText = @"\Aentries/" + IconFolderText + SlugSegment + "/" + SlugSegment + @"\.icon\.json\z";
    const string AdPackManifestPathText = @"\Aentries/" + AdPackFolderText + SlugSegment + "/" + SlugSegment + @"\.ad-pack\.json\z";
    const string MetaPathText = @"\Aentries/" + AnyKindFolderText + SlugSegment + "/" + SlugSegment + @"\.meta\.json\z";

    // entries/<slug>/<filename> — a pack's binary asset (SPEC F104.1, F128.1): a font pack's 1-2
    // latin-subsetted woff2 faces and its OFL licence text, or an avatar pack's PNG items, sitting
    // alongside (never inside) its own manifest. UNLIKE the manifest/meta filename segment above
    // (which must equal the slug itself), an asset's filename is the pack's OWN file name (e.g.
    // "space-grotesk-variable-latin.woff2", "OFL.txt", "warm-grin.png") — so this pattern constrains
    // character set and extension only, not the slug shape. It still gives the SAME SSRF-shaped
    // guarantee (no absolute URL, no scheme, no leading slash, no ".." traversal): no '/' appears anywhere in the
    // character class, so a value can never introduce a second path segment to traverse with, and
    // the leading-character class rules out a value starting with '.'. Extension sets are SPLIT PER
    // KIND (review finding, PLAN T292 — a single shared set let a font pack declare a .png item and
    // an avatar pack declare a .woff2 one, neither of which either real pack kind ever ships): a
    // font pack's own woff2 faces + OFL licence text (F104.1), an avatar pack's own PNG items only
    // (F128.1) — never a format the other kind doesn't actually ship.
    const string FontAssetFileNameText = @"[A-Za-z0-9][A-Za-z0-9._-]*\.(?:woff2|txt)";
    const string AvatarAssetFileNameText = @"[A-Za-z0-9][A-Za-z0-9._-]*\.png";
    const string FontAssetPathText = @"\Aentries/" + FontFolderText + SlugSegment + "/" + FontAssetFileNameText + @"\z";
    const string AvatarAssetPathText = @"\Aentries/" + AvatarFolderText + SlugSegment + "/" + AvatarAssetFileNameText + @"\z";

    // entries/[personas/]<slug>/<slug>.avatar.png — a PERSONA entry's OWN optional avatar sidecar
    // (SPEC F128.2), UNLIKE a pack's own free-named asset above: the filename segment MUST equal the
    // directory's own slug segment — enforced by the pattern ITSELF via a named backreference
    // (review finding, PLAN T292: a prior version used two independent, unrelated SlugSegment
    // occurrences here, which admitted ANY slug-shaped filename sitting in the entry's own directory,
    // e.g. entries/valid-dj/some-other-dj.avatar.png — not just valid-dj's own face — despite the
    // comment already claiming the binding this pattern alone did not perform). Combined with
    // TryValidateFileRef's own directory-segment-equals-declared-slug check, this transitively pins
    // the filename to the ENTRY's declared slug too, so this DJ's face can never be confused for
    // another entry's. See TryValidatePersonaAvatarAsset's own remarks for the at-most-one
    // cardinality rule this pattern alone does not express.
    const string PersonaAvatarAssetPathText = @"\Aentries/" + PersonaFolderText + @"(?<avatarSlug>" + SlugSegment + @")/\k<avatarSlug>\.avatar\.png\z";

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

    // The SAME CSS-injection-safe family shape ThemeManifestParser enforces on a theme manifest's
    // own font family (T194 review finding — the blocker: an optional shelf-card `family` string
    // was reaching CatalogShelfEntryDto.FontFamily with only a `Length > 0` check, admitting a
    // payload like 'X;}</style><script>alert(1)</script>' verbatim) — composed from its
    // `internal const` (mirrors SwatchHexPattern immediately above), not a second copy. See
    // ThemeManifestParser.FontFamilyText's own remarks.
    [GeneratedRegex(ThemeManifestParser.FontFamilyText)]
    private static partial Regex FamilyPattern();

    // Split per kind (and per field) so a manifest can't masquerade as a meta (or vice versa), and
    // a persona's manifest can't masquerade as a theme's (or vice versa), even though all three
    // share the same entries/<slug>/<name>.EXT.json shape.
    [GeneratedRegex(PersonaManifestPathText)]
    private static partial Regex PersonaManifestPathPattern();

    [GeneratedRegex(ThemeManifestPathText)]
    private static partial Regex ThemeManifestPathPattern();

    [GeneratedRegex(FontManifestPathText)]
    private static partial Regex FontManifestPathPattern();

    [GeneratedRegex(ShowManifestPathText)]
    private static partial Regex ShowManifestPathPattern();

    [GeneratedRegex(AvatarManifestPathText)]
    private static partial Regex AvatarManifestPathPattern();

    [GeneratedRegex(IconManifestPathText)]
    private static partial Regex IconManifestPathPattern();

    [GeneratedRegex(AdPackManifestPathText)]
    private static partial Regex AdPackManifestPathPattern();

    [GeneratedRegex(MetaPathText)]
    private static partial Regex MetaPathPattern();

    [GeneratedRegex(FontAssetPathText)]
    private static partial Regex FontAssetPathPattern();

    [GeneratedRegex(AvatarAssetPathText)]
    private static partial Regex AvatarAssetPathPattern();

    [GeneratedRegex(PersonaAvatarAssetPathText)]
    private static partial Regex PersonaAvatarAssetPathPattern();

    /// <summary>
    /// Back-compat overload for every caller that has no use for the round-1 (PLAN T292)
    /// <see cref="CatalogValidationNotice"/> channel — discards it. <see cref="CatalogProxyService"/>
    /// is the one caller that DOES need it, to log F90.3's own "withholds-that-entry" WARN shape for
    /// each one (see the 5-out overload's own remarks); every other caller in this codebase's test
    /// suite (driving this seam directly) never had a reason to see them before this widening and
    /// still doesn't.
    /// </summary>
    public static bool TryValidate(
        byte[] json, Uri directory,
        [NotNullWhen(true)] out IReadOnlyList<CatalogEntrySummary>? entries,
        [NotNullWhen(false)] out string? rejectionReason) =>
        TryValidate(json, directory, out entries, out _, out rejectionReason);

    /// <summary>
    /// Parses and strictly validates a raw index.json payload. On success, every returned
    /// <see cref="CatalogEntrySummary"/> has already passed kind/slug/audience/sha256-shape/path
    /// validation AND the belt-and-braces directory-prefix check. TWO different reasons an entry can
    /// be missing from the result, neither one a rejection reason: an entry naming an unrecognised
    /// <c>kind</c> is simply absent (F103.1, forward-compat — no <paramref name="notices"/> entry, this
    /// is the ordinary, expected case); a persona entry that failed its own one-face rule (SPEC
    /// F128.2, STORY-331 AC6, round-1 review finding 1) is WITHHELD and absent too, but DOES add a
    /// <paramref name="notices"/> entry naming the slug and reason — this class stays log-free by
    /// design (its own class remarks), so <paramref name="notices"/> is how that WARN-worthy fact
    /// ever reaches a caller. A malformed persona sidecar that only DEGRADES (round-1 review finding
    /// 3) also adds a <paramref name="notices"/> entry, even though its entry is NOT missing from the
    /// result — see <see cref="TryValidatePersonaAvatarAsset"/>'s own remarks for the full ladder. On
    /// failure, the WHOLE index is rejected — <paramref name="rejectionReason"/> names the first
    /// offending value (a malformed entry path names that exact path, per F90.2), and
    /// <paramref name="notices"/> is discarded (empty).
    /// </summary>
    public static bool TryValidate(
        byte[] json, Uri directory,
        [NotNullWhen(true)] out IReadOnlyList<CatalogEntrySummary>? entries,
        out IReadOnlyList<CatalogValidationNotice> notices,
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
            notices = [];
            rejectionReason = $"malformed JSON ({ex.Message})";
            return false;
        }

        if (document?.Entries is not { } rawEntries)
        {
            entries = null;
            notices = [];
            rejectionReason = "missing an 'entries' array";
            return false;
        }

        var validated = new List<CatalogEntrySummary>(rawEntries.Count);
        var collectedNotices = new List<CatalogValidationNotice>();
        foreach (var raw in rawEntries)
        {
            switch (TryValidateEntry(raw, directory, out var summary, out var entryNotice, out var entryRejectionReason))
            {
                case EntryValidationOutcome.Valid:
                    validated.Add(summary
                        ?? throw new UnreachableException($"{nameof(EntryValidationOutcome.Valid)} without a summary."));
                    if (entryNotice is not null)
                        collectedNotices.Add(entryNotice);
                    break;

                case EntryValidationOutcome.Skip:
                    // F103.1/AC5: an entry naming a kind this app does not recognise is dropped —
                    // forward-compat for a future kind — the rest of the index still loads. No
                    // notice: this is the ordinary, expected case, never a WARN-worthy business-rule
                    // violation (unlike EntryValidationOutcome.Withheld below).
                    break;

                case EntryValidationOutcome.Withheld:
                    // STORY-331 AC6 (round-1 review finding 1): this ENTRY alone is excluded — the
                    // rest of the index still loads. The reason travels via entryNotice, never
                    // rejectionReason, which stays reserved for a whole-index-fatal shape failure.
                    collectedNotices.Add(entryNotice
                        ?? throw new UnreachableException($"{nameof(EntryValidationOutcome.Withheld)} without a notice."));
                    break;

                case EntryValidationOutcome.Reject:
                default:
                    entries = null;
                    notices = [];
                    rejectionReason = entryRejectionReason
                        ?? throw new UnreachableException($"{nameof(EntryValidationOutcome.Reject)} without a reason.");
                    return false;
            }
        }

        entries = validated;
        notices = collectedNotices;
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

    /// <summary>One raw entry's fate: kept (optionally with a non-fatal <see cref="CatalogValidationNotice"/>
    /// riding alongside it, e.g. a degraded sidecar face), silently dropped (forward-compat, an
    /// unrecognised kind), withheld with a <see cref="CatalogValidationNotice"/> WARN (a real
    /// per-entry business-rule violation — STORY-331 AC6 — the rest of the index still loads), or
    /// fatal to the whole index.</summary>
    enum EntryValidationOutcome
    {
        Valid,
        Skip,
        Withheld,
        Reject,
    }

    static EntryValidationOutcome TryValidateEntry(
        CatalogIndexEntryJson raw, Uri directory,
        out CatalogEntrySummary? summary, out CatalogValidationNotice? notice, out string? reason)
    {
        summary = null;
        notice = null;
        reason = null;

        // Kind is resolved FIRST (F103.1): an entry naming a kind this app doesn't recognise might
        // be shaped in a way no persona/theme/font/show/avatar/icon rule below can meaningfully
        // validate (a future kind) — it is skipped outright, before slug/audience/manifest are even
        // looked at, and never counts toward rejecting the rest of the index.
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

        // ONE-DIRECTORY INVARIANT (genwave-catalog#33): an entry's manifest and meta (and assets,
        // below) all sit in the SAME directory. Under the flat layout the patterns alone pinned this
        // (both could only ever match entries/<slug>/); with the optional kind folder they no longer
        // do — MetaPathPattern accepts ANY kind folder, so a persona's meta could otherwise sit under
        // entries/shows/<slug>/ while its manifest sits under entries/personas/<slug>/. Beyond the
        // shape lie, this is what keeps an entry's bare FILENAMES unique whenever its full paths are
        // (the asset dedup below and CatalogProxyService's filename-keyed asset lookup both lean on
        // that), so it is enforced as a hard reject, same as the slug-ownership check inside
        // TryValidateFileRef.
        var entryDirectory = DirectoryOf(manifest.Path);
        if (!string.Equals(entryDirectory, DirectoryOf(meta.Path), StringComparison.Ordinal))
        {
            reason = $"entry '{slug}' meta path '{meta.Path}' does not sit in its manifest's own directory";
            return EntryValidationOutcome.Reject;
        }

        // F104.1/F128.1/F128.2: font AND avatar entries carry assets[] with the SAME all-or-nothing
        // "a pack IS its files" posture (TryValidateAssets); a persona entry's own assets[] is
        // GENUINELY OPTIONAL (at most one sidecar face, TryValidatePersonaAvatarAsset); theme/show/
        // icon entries always resolve to the empty list (CatalogEntrySummary.Assets's own "absent
        // means empty" remarks) — an icon manifest carries its whole vector document inline, F130.1,
        // never a binary asset.
        IReadOnlyList<CatalogAssetRef> assets;
        switch (kind)
        {
            case CatalogEntryKind.Font:
            case CatalogEntryKind.Avatar:
                // A font/avatar entry whose assets[] is missing, empty, or contains anything
                // malformed is skipped OUTRIGHT (never rejects the whole index) — see
                // TryValidateAssets's own remarks for why this is a whole-entry skip rather than a
                // field-level degrade like Preview.
                if (!TryValidateAssets(raw.Assets, slug, entryDirectory, directory, kind, out var packAssets))
                    return EntryValidationOutcome.Skip;

                assets = packAssets;
                break;

            case CatalogEntryKind.Persona:
                // The three-rung ladder (round-1 review findings 1/3) — see
                // TryValidatePersonaAvatarAsset's own remarks for why each rung lands here.
                switch (TryValidatePersonaAvatarAsset(raw.Assets, slug, entryDirectory, directory, out var personaAssets, out var personaNotice))
                {
                    case PersonaAvatarOutcome.TooMany:
                        // STORY-331 AC6: this ENTRY is withheld — never the whole index.
                        notice = personaNotice
                            ?? throw new UnreachableException($"{nameof(PersonaAvatarOutcome.TooMany)} without a notice.");
                        return EntryValidationOutcome.Withheld;

                    case PersonaAvatarOutcome.Degraded:
                        // A malformed sidecar degrades to no face; the entry itself still lists.
                        notice = personaNotice
                            ?? throw new UnreachableException($"{nameof(PersonaAvatarOutcome.Degraded)} without a notice.");
                        assets = [];
                        break;

                    case PersonaAvatarOutcome.Ok:
                        assets = personaAssets;
                        break;

                    default:
                        throw new UnreachableException($"Unhandled {nameof(PersonaAvatarOutcome)} value.");
                }

                break;

            default:
                assets = [];
                break;
        }

        // STORY-281 AC1 reconciliation (T194 review finding): only meaningful on a font entry,
        // mirrors Assets' own kind-gating — see TryParseFamily's own remarks for the decorative,
        // never-fails posture this field alone gets.
        var family = kind == CatalogEntryKind.Font ? TryParseFamily(raw.Family) : null;

        summary = new CatalogEntrySummary(slug, kind, audience, raw.BestFor ?? [], manifest, meta, TryParsePreview(raw.Preview), assets, family);
        return EntryValidationOutcome.Valid;
    }

    // T194 review finding: ThemeManifestParser itself caps its own font family only by presence
    // (`{ Length: > 0 }`), never by an upper bound — safe there because a theme manifest is
    // first-party, embedded content, not remote input. This field is the opposite: it arrives off a
    // remote, untrusted index.json, so a shape-valid-but-absurd blob (a many-KB run of
    // letters/digits/spaces/hyphens, which FamilyPattern alone would still admit) still needs a
    // bound. 64 is an honest, generous cap — no real font family name in this format's own
    // vocabulary ("Space Grotesk", "Fraunces", "Source Sans 3") comes anywhere close — chosen for
    // parity with this codebase's other short-identifier caps (e.g. CatalogController.MaxSlugLength)
    // rather than derived from any real font's measured length.
    const int MaxFamilyLength = 64;

    /// <summary>
    /// Admits the OPTIONAL shelf-card <c>family</c> string (STORY-281 AC1 reconciliation, T194
    /// review finding — "the shelf card shows FAMILY, but family lives in the manifest which browse
    /// never fetches"): the SAME seam <see cref="BestFor"/>/<see cref="TryParsePreview"/> already
    /// fixed this shape of problem with — a field the INDEX itself carries so a zero-fetch shelf
    /// listing can show it, rather than the shelf paying for a manifest fetch it otherwise never
    /// makes. Decorative, like <see cref="BestFor"/>: absent, wrong-typed, over-length, or
    /// wrong-shaped degrades to <see langword="null"/>, never fails validation of the entry it lives
    /// on or rejects the whole index — a missing/bad family name is purely cosmetic, never a reason a
    /// real, well-formed font entry should vanish from the shelf.
    ///
    /// <para>
    /// SHAPE (T194 review finding — blocker): this value reaches <see cref="Api.CatalogShelfEntryDto.FontFamily"/>
    /// verbatim, off UNTRUSTED index.json content, the exact same wire-injection exposure
    /// <see cref="TryParsePreview"/>'s own swatch check (<see cref="SwatchHexPattern"/>) already
    /// guards — a bare <c>Length &gt; 0</c> check let a CSS-injection payload (e.g.
    /// <c>'X;}&lt;/style&gt;&lt;script&gt;alert(1)&lt;/script&gt;'</c>) straight through. Gated on
    /// <see cref="FamilyPattern"/> — the same shape <see cref="ThemeManifestParser.FontFamilyPattern"/>
    /// enforces on a theme manifest's own font family — so this class holds an untrusted index
    /// entry's family to the exact rule a manifest's real one already is.
    /// </para>
    ///
    /// <para>
    /// LENGTH (T194 review finding): <see cref="MaxFamilyLength"/>'s own remarks explain the bound;
    /// checked here alongside the shape and presence checks so all three fail the same way — degrade
    /// to <see langword="null"/>, never throw or reject the whole index.
    /// </para>
    /// </summary>
    static string? TryParseFamily(JsonElement? raw) =>
        raw is { ValueKind: JsonValueKind.String } element
            && element.GetString() is { Length: > 0 and <= MaxFamilyLength } family
            && FamilyPattern().IsMatch(family)
            ? family
            : null;

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
    /// Validates a font/avatar entry's whole <c>assets[]</c> array (SPEC F104.1, F128.1, T193/T292) —
    /// REJECT-VS-DEGRADE POSTURE: unlike <see cref="TryParsePreview"/>'s field-level degrade (a bad
    /// <c>preview</c> nulls just that one decorative field, keeping the rest of the entry), a
    /// malformed or absent assets list here fails the WHOLE entry, which <see cref="TryValidateEntry"/>
    /// then SKIPS (never rejects the whole index — that stays reserved for slug/audience/manifest/meta
    /// shape failures). The difference: a pack IS its files — an entry admitted with an empty or
    /// partly-broken assets list would be a shelf card advertising a font/avatar pack nothing can
    /// actually serve, a strictly worse outcome than the entry simply not existing yet. So this is
    /// ALL-OR-NOTHING: every declared asset must individually validate (<see cref="TryValidateAssetRef"/>,
    /// against <paramref name="kind"/>'s own asset path pattern) and at least one must be present, or
    /// the caller treats the entire entry as absent. A path declared TWICE (F1 review finding, T194)
    /// is the SAME all-or-nothing failure — a pack declaring the same file twice is malformed by
    /// definition (which of the two would even be the real one?) — never merely de-duplicated into a
    /// shorter list, and never left for <see cref="CatalogProxyService"/>'s own cache-prune bookkeeping
    /// to trip over downstream (that bookkeeping is hardened separately, defense-in-depth, but the
    /// front door is where a malformed pack belongs being turned away).
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
    ///
    /// <para>
    /// <paramref name="kind"/> (PLAN T292 widening) is always <see cref="CatalogEntryKind.Font"/> or
    /// <see cref="CatalogEntryKind.Avatar"/> — the two pack-shaped kinds this method's own all-or-
    /// nothing posture applies to; a persona entry's own, genuinely-optional sidecar asset is
    /// <see cref="TryValidatePersonaAvatarAsset"/>'s separate job.
    /// </para>
    /// </summary>
    static bool TryValidateAssets(
        JsonElement? raw, string slug, string entryDirectory, Uri directory, CatalogEntryKind kind,
        [NotNullWhen(true)] out IReadOnlyList<CatalogAssetRef>? assets)
    {
        if (raw is not { ValueKind: JsonValueKind.Array } array)
        {
            assets = null;
            return false;
        }

        var validated = new List<CatalogAssetRef>(array.GetArrayLength());
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in array.EnumerateArray())
        {
            if (!TryValidateAssetRef(element, slug, entryDirectory, directory, AssetPathPattern(kind), AssetByteCeiling(kind), out var assetRef))
            {
                assets = null;
                return false;
            }

            if (!seenPaths.Add(assetRef.Path))
            {
                // The SAME path declared twice within one entry (F1 review finding) — see this
                // method's own remarks on why that fails the whole entry rather than merely
                // collapsing to one copy.
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

    /// <summary>One persona entry's own optional sidecar-face check outcome (round-1 review findings
    /// 1/3, PLAN T292) — see <see cref="TryValidatePersonaAvatarAsset"/>'s own remarks for the exact
    /// three-rung ladder each value maps to.</summary>
    enum PersonaAvatarOutcome
    {
        Ok,
        Degraded,
        TooMany,
    }

    /// <summary>
    /// Validates a PERSONA entry's OWN optional avatar sidecar asset (SPEC F128.2, PLAN T292) — at
    /// most ONE <c>&lt;slug&gt;.avatar.png</c> in <c>assets[]</c>, the "this DJ ships with this face"
    /// shape. UNLIKE <see cref="TryValidateAssets"/>'s all-or-nothing pack posture (a pack IS its
    /// files, at least one required), a persona's own assets are GENUINELY OPTIONAL, with their own
    /// THREE-RUNG LADDER (round-1 review findings 1/3):
    ///
    /// <para>
    /// (1) ABSENT, or present-but-EMPTY — both mean "no face declared"
    /// (<see cref="PersonaAvatarOutcome.Ok"/>, <paramref name="assets"/> resolves to an empty list),
    /// no different from an older index that has never heard of this field.
    /// </para>
    ///
    /// <para>
    /// (2) EXACTLY ONE declared asset that fails its OWN shape check (bad path/sha256/bytes, wrong
    /// directory, oversize, or a filename other than THIS entry's own <c>&lt;slug&gt;.avatar.png</c>
    /// — <see cref="PersonaAvatarAssetPathPattern"/> pins the filename to the slug via a named
    /// backreference, unlike a pack's own free-named items), or a non-array <c>assets</c> value —
    /// DEGRADES to no face (<see cref="PersonaAvatarOutcome.Degraded"/>): the entry still validates
    /// and still lists, with a <paramref name="notice"/> naming the slug and reason for
    /// <see cref="CatalogProxyService"/> to WARN on. Round-1 review finding 3: a malformed sidecar on
    /// the entry class that matters most (a real, otherwise-fine DJ) must never cost the whole entry
    /// its listing — the same "absent ⇒ a neutral placeholder, never a broken card" posture SPEC
    /// F128.9 already states for a genuinely absent face, extended here to a present-but-broken one.
    /// </para>
    ///
    /// <para>
    /// (3) TWO OR MORE declared assets — a deliberate CARDINALITY VIOLATION, never incidental JSON
    /// noise — WITHHOLDS just this one entry (<see cref="PersonaAvatarOutcome.TooMany"/>): excluded
    /// from the index's returned list, with a <paramref name="notice"/> naming the slug and the
    /// one-face rule for <see cref="CatalogProxyService"/> to WARN on, while the REST OF THE SHELF
    /// still loads. STORY-331 AC6's own words: "Given a persona entry carrying two or more assets ...
    /// Then that ENTRY is rejected with a reason naming the one-face rule" — an ENTRY-scoped outcome
    /// (round-1 review finding 1: a prior version of this method instead rejected the WHOLE INDEX
    /// here — a single community typo on one persona's sidecar could brick every OTHER station's
    /// shelf on a cold process, which no SPEC/STORY text ever asked for; SPEC F90.3's own
    /// "withholds-that-entry" WARN posture — <c>CatalogProxyService.WithheldHashMismatch</c>/
    /// <c>WithheldOversize</c> — is the precedent this now mirrors instead).
    /// </para>
    /// </summary>
    static PersonaAvatarOutcome TryValidatePersonaAvatarAsset(
        JsonElement? raw, string slug, string entryDirectory, Uri directory,
        out IReadOnlyList<CatalogAssetRef> assets, out CatalogValidationNotice? notice)
    {
        assets = [];
        notice = null;

        if (raw is not { } element)
            return PersonaAvatarOutcome.Ok; // Absent — no face declared, the ordinary pre-F128 case.

        if (element.ValueKind != JsonValueKind.Array)
        {
            notice = DegradedNotice(slug, "its avatar assets value is not an array");
            return PersonaAvatarOutcome.Degraded;
        }

        var count = element.GetArrayLength();
        if (count == 0)
            return PersonaAvatarOutcome.Ok; // Declared-but-empty — the same "no face" outcome as absent.

        if (count >= 2)
        {
            notice = new CatalogValidationNotice(slug, CatalogValidationNoticeKind.EntryWithheld,
                $"entry '{slug}' carries {count} avatar assets — a persona entry may carry at most one face ({slug}.avatar.png)");
            return PersonaAvatarOutcome.TooMany;
        }

        if (!TryValidateAssetRef(element.EnumerateArray().Single(), slug, entryDirectory, directory, PersonaAvatarAssetPathPattern(), MaxPngAssetBytes, out var assetRef))
        {
            notice = DegradedNotice(slug, "its one declared avatar sidecar asset is malformed (bad path/sha256/bytes, wrong directory or oversize, or a filename other than <slug>.avatar.png)");
            return PersonaAvatarOutcome.Degraded;
        }

        assets = [assetRef];
        return PersonaAvatarOutcome.Ok;
    }

    /// <summary>Builds the <see cref="CatalogValidationNoticeKind.FieldDegraded"/> shape both
    /// <see cref="TryValidatePersonaAvatarAsset"/> degrade rungs share — one place the message
    /// template lives, rather than two independently-drifting copies.</summary>
    static CatalogValidationNotice DegradedNotice(string slug, string detail) =>
        new(slug, CatalogValidationNoticeKind.FieldDegraded, $"entry '{slug}' {detail} — degraded to no face");

    /// <summary>
    /// One <c>assets[]</c> element's shape check (SPEC F104.1) — the same SSRF-shaped belt-and-braces
    /// rules <see cref="TryValidateFileRef"/> applies to a manifest/meta pointer (path shape, slug
    /// ownership, directory containment), plus a positive <see cref="CatalogAssetJson.Bytes"/> (the
    /// fetch transport's declared size cap, T194 — zero or negative names nothing a caller could
    /// ever stream) THAT ALSO NEVER EXCEEDS <paramref name="maxBytes"/> — the caller's own PER-KIND
    /// ceiling (review finding, PLAN T292: a prior version always compared against
    /// <see cref="CatalogProxyService.MaxAssetBytes"/> — 256 KiB, a number FONTS.md/F104.2 derived for
    /// a font pack's own woff2/txt world — regardless of kind, so a CI-legal, SPEC F128.1-compliant
    /// avatar/persona-sidecar PNG between 256 KiB and 512 KiB would silently fail here and vanish its
    /// whole pack/entry). An asset declaring more bytes than <paramref name="maxBytes"/> is malformed
    /// by definition — admitting it anyway would only defer the rejection to fetch time (withheld as
    /// <see cref="CatalogAssetFetchResult.Oversize"/>) while leaving an unbounded declared value
    /// sitting in <see cref="CatalogEntrySummary.Assets"/> for every zero-fetch shelf projection
    /// (<see cref="Api.CatalogController"/>'s <c>FontByteTotal</c> sum) to trust. Bounding it HERE
    /// keeps that sum structurally bounded by construction, never merely by hoping every summing call
    /// site remembers to guard against a hostile origin's declared size. S4 review finding: rather
    /// than re-implementing the belt-and-braces path/slug/directory check a second time, this calls
    /// <see cref="TryValidateFileRef"/> ITSELF — the one place that security-critical traversal/SSRF
    /// logic lives — passing <paramref name="pathPattern"/> (PLAN T292: the caller's own choice of
    /// <see cref="FontAssetPathPattern"/>/<see cref="AvatarAssetPathPattern"/>/
    /// <see cref="PersonaAvatarAssetPathPattern"/>, never a single shared pattern — a font pack's free-
    /// named item and a persona's slug-named sidecar face are shaped differently) in place of a
    /// manifest/meta pattern and discarding its WARN-worthy reason string (unlike
    /// <see cref="TryValidateFileRef"/>'s callers, which reject the whole index and so need one, a bad
    /// asset here degrades/skips its OWN entry silently — the same no-reason shape
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
        JsonElement element, string slug, string entryDirectory, Uri directory, Regex pathPattern, long maxBytes,
        [NotNullWhen(true)] out CatalogAssetRef? assetRef)
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

        if (raw is not { Bytes: { } bytes } || bytes <= 0 || bytes > maxBytes)
        {
            assetRef = null;
            return false;
        }

        if (!TryValidateFileRef(
                new CatalogFileRefJson { Path = raw.Path, Sha256 = raw.Sha256 },
                pathPattern, slug, directory, out var fileRef, out _))
        {
            assetRef = null;
            return false;
        }

        // The ONE-DIRECTORY INVARIANT again (TryValidateEntry's own remarks) — an asset sits
        // alongside its manifest, never under the OTHER layout's copy of the same slug. Fails only
        // this asset (⇒ whole-entry skip, TryValidateAssets' all-or-nothing posture), matching every
        // other malformed-asset shape here.
        if (!string.Equals(DirectoryOf(fileRef.Path), entryDirectory, StringComparison.Ordinal))
        {
            assetRef = null;
            return false;
        }

        assetRef = new CatalogAssetRef(fileRef.Path, fileRef.Sha256, bytes);
        return true;
    }

    /// <summary>The path up to (excluding) its final <c>/</c> — every caller's path has already
    /// matched a pattern whose shape guarantees at least one <c>/</c>.</summary>
    static string DirectoryOf(string path) => path[..path.LastIndexOf('/')];

    /// <summary>A missing <c>kind</c> defaults to persona (back-compat, F103.1/AC2); any value other than <c>"persona"</c>/<c>"theme"</c>/<c>"font"</c>/<c>"show"</c>/<c>"avatar"</c>/<c>"icon"</c>/<c>"ad-pack"</c> is unrecognised.</summary>
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
            case "show":
                kind = CatalogEntryKind.Show;
                return true;
            case "avatar":
                kind = CatalogEntryKind.Avatar;
                return true;
            case "icon":
                kind = CatalogEntryKind.Icon;
                return true;
            case "ad-pack":
                kind = CatalogEntryKind.AdPack;
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
        CatalogEntryKind.Show => ShowManifestPathPattern(),
        CatalogEntryKind.Avatar => AvatarManifestPathPattern(),
        CatalogEntryKind.Icon => IconManifestPathPattern(),
        CatalogEntryKind.AdPack => AdPackManifestPathPattern(),
        _ => throw new UnreachableException($"Unhandled {nameof(CatalogEntryKind)} value: {kind}."),
    };

    /// <summary>
    /// The pack-shaped kinds' own asset path pattern (PLAN T292) — the two <see cref="CatalogEntryKind"/>
    /// members <see cref="TryValidateAssets"/> is ever called for; a persona's own sidecar face uses
    /// <see cref="PersonaAvatarAssetPathPattern"/> directly instead (its filename is slug-shaped, not
    /// free-named like a pack item).
    /// </summary>
    static Regex AssetPathPattern(CatalogEntryKind kind) => kind switch
    {
        CatalogEntryKind.Font => FontAssetPathPattern(),
        CatalogEntryKind.Avatar => AvatarAssetPathPattern(),
        _ => throw new UnreachableException($"{kind} entries do not carry a pack-shaped assets[]."),
    };

    /// <summary>
    /// Declared-size ceiling for a PNG-carrying asset — an avatar pack item OR a persona entry's own
    /// sidecar face (SPEC F128.1: "≤512 KiB per item"; T291 pinned this exact value app-side; T309's
    /// future catalog-CI gate enforces it upstream too). Review finding, PLAN T292:
    /// <see cref="TryValidateAssetRef"/> used to check EVERY kind's declared asset bytes against
    /// <see cref="CatalogProxyService.MaxAssetBytes"/> (256 KiB) — a number FONTS.md/F104.2 derived
    /// for a font pack's own much smaller woff2/txt world, not F128.1's PNG one. A CI-legal, 300-512
    /// KiB avatar face declared in index.json would silently vanish its whole pack/entry over that
    /// borrowed ceiling; this is F128.1's OWN number, named for what it actually bounds.
    /// </summary>
    internal const int MaxPngAssetBytes = 512 * 1024;

    /// <summary>
    /// The pack-shaped kinds' own asset-declared-size ceiling (review finding, PLAN T292) — SPLIT PER
    /// KIND, unlike the single shared check every kind used to get (the bug this fixes, see
    /// <see cref="TryValidateAssetRef"/>'s own remarks): a font pack's woff2/txt items stay pinned to
    /// <see cref="CatalogProxyService.MaxAssetBytes"/> (256 KiB, unaffected by this widening), while
    /// an avatar pack's items — and, via <see cref="TryValidatePersonaAvatarAsset"/>'s own direct call,
    /// a persona's own sidecar face — get <see cref="MaxPngAssetBytes"/> (512 KiB, SPEC F128.1's own
    /// number).
    /// </summary>
    static long AssetByteCeiling(CatalogEntryKind kind) => kind switch
    {
        CatalogEntryKind.Font => CatalogProxyService.MaxAssetBytes,
        CatalogEntryKind.Avatar => MaxPngAssetBytes,
        _ => throw new UnreachableException($"{kind} entries do not carry a pack-shaped assets[]."),
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
        // The pattern already guarantees the slug is the SECOND-TO-LAST '/'-delimited segment in
        // BOTH shelf layouts (entries/<slug>/<file> and entries/<kind-plural>/<slug>/<file>,
        // genwave-catalog#33), so indexing from the end is safe.
        var segments = path.Split('/');
        var pathSlug = segments[^2];
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

        /// <summary><c>"persona"</c> | <c>"theme"</c> | <c>"font"</c> | <c>"show"</c> (SPEC F103.1, F104.1, F118.1); absent means persona (back-compat).</summary>
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

        /// <summary>
        /// The OPTIONAL F104.3/STORY-281 shelf-card family name — only ever meaningful on a
        /// <c>kind:"font"</c> entry. A raw <see cref="JsonElement"/>, not a typed
        /// <see cref="string"/> directly (mirrors <see cref="Preview"/>'s own remarks immediately
        /// above): a wrong-typed <c>family</c> (a number, array, or object) must never fail the
        /// top-level <c>Deserialize</c> call this record is itself a member of. See
        /// <see cref="TryParseFamily"/> for the decorative, never-fails posture this field alone gets.
        /// </summary>
        public JsonElement? Family { get; init; }
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
