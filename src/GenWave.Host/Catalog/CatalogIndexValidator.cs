namespace GenWave.Host.Catalog;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// Pure, I/O-free validation of a fetched index.json payload against SPEC F90.2's strict shape
/// rules — no HTTP, no caching, no logging. <see cref="CatalogProxyService"/> is the only caller,
/// and owns turning a rejection into the one WARN log line F90.2 asks for; this class only ever
/// answers "is this shape trustworthy", never "should I serve/cache it" (single responsibility,
/// and independently testable without a fake HTTP handler in play).
///
/// <para>
/// THE `kind` SEAM (SPEC F103.1): each entry declares a <c>kind</c> (<c>"persona"</c> |
/// <c>"theme"</c>); a missing field defaults to <see cref="CatalogEntryKind.Persona"/> (back-compat
/// for every entry authored before the field existed). A <c>kind</c> naming neither case is
/// forward-compat, not fatal — that ONE entry is silently dropped and the rest of the index still
/// loads (<see cref="TryValidateEntry"/>'s own early return) — deliberately unlike an unrecognised
/// <c>audience</c> below, which still rejects the WHOLE index (audience is content-safety; kind is
/// forward-compat). The per-kind manifest file pattern (<see cref="PersonaManifestPathPattern"/> /
/// <see cref="ThemeManifestPathPattern"/>) is picked only once an entry's kind is known.
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

    // entries/<slug>/<name>.persona.json / entries/<slug>/<name>.theme.json — the per-kind manifest
    // shape (SPEC F103.2): the filename segment is the SAME shape as the slug segment (SPEC
    // F90.2/F89.2: schemas/index.schema.json's card/meta path patterns use this one shape for both
    // segments, not the looser "any run of [a-z0-9-]" a prior version allowed here, which would
    // have tolerated a leading/trailing/doubled hyphen the real schema rejects).
    const string PersonaManifestPathText = @"\Aentries/" + SlugSegment + "/" + SlugSegment + @"\.persona\.json\z";
    const string ThemeManifestPathText = @"\Aentries/" + SlugSegment + "/" + SlugSegment + @"\.theme\.json\z";
    const string MetaPathText = @"\Aentries/" + SlugSegment + "/" + SlugSegment + @"\.meta\.json\z";

    [GeneratedRegex(@"\A" + SlugSegment + @"\z")]
    private static partial Regex SlugPattern();

    [GeneratedRegex(@"\A[a-f0-9]{64}\z")]
    private static partial Regex Sha256Pattern();

    // Split per kind (and per field) so a manifest can't masquerade as a meta (or vice versa), and
    // a persona's manifest can't masquerade as a theme's (or vice versa), even though all three
    // share the same entries/<slug>/<name>.EXT.json shape.
    [GeneratedRegex(PersonaManifestPathText)]
    private static partial Regex PersonaManifestPathPattern();

    [GeneratedRegex(ThemeManifestPathText)]
    private static partial Regex ThemeManifestPathPattern();

    [GeneratedRegex(MetaPathText)]
    private static partial Regex MetaPathPattern();

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
        // be shaped in a way no persona/theme rule below can meaningfully validate (a future
        // font/icon/avatar) — it is skipped outright, before slug/audience/manifest are even
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

        summary = new CatalogEntrySummary(slug, kind, audience, raw.BestFor ?? [], manifest, meta);
        return EntryValidationOutcome.Valid;
    }

    /// <summary>A missing <c>kind</c> defaults to persona (back-compat, F103.1/AC2); any value other than <c>"persona"</c>/<c>"theme"</c> is unrecognised.</summary>
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
            default:
                kind = default;
                return false;
        }
    }

    static Regex ManifestPathPattern(CatalogEntryKind kind) => kind switch
    {
        CatalogEntryKind.Persona => PersonaManifestPathPattern(),
        CatalogEntryKind.Theme => ThemeManifestPathPattern(),
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

        /// <summary><c>"persona"</c> | <c>"theme"</c> (SPEC F103.1); absent means persona (back-compat).</summary>
        public string? Kind { get; init; }

        public string? Audience { get; init; }
        public IReadOnlyList<string>? BestFor { get; init; }

        /// <summary>The F103.2 field name every kind targets going forward.</summary>
        public CatalogFileRefJson? Manifest { get; init; }

        /// <summary>Legacy persona-only wire name — see <see cref="TryValidateEntry"/>'s own remarks on why this is still read.</summary>
        public CatalogFileRefJson? Card { get; init; }

        public CatalogFileRefJson? Meta { get; init; }
    }

    /// <summary>Ephemeral JSON projection of a raw index.json <c>manifest</c>/<c>card</c>/<c>meta</c> file pointer.</summary>
    sealed record CatalogFileRefJson
    {
        public string? Path { get; init; }
        public string? Sha256 { get; init; }
    }
}
