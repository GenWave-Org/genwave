namespace GenWave.Host.Catalog;

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
/// THE SSRF BOUNDARY THIS CLASS ENFORCES (see <see cref="CatalogProxyService"/>'s own remarks for
/// the full ruling): the operator-controlled index URL is trusted, but REMOTE content — the index
/// body itself — never chooses a fetch target. Every entry path is checked against
/// <see cref="CardPathPattern"/>/<see cref="MetaPathPattern"/> (no absolute URL, no <c>..</c>, no
/// leading <c>/</c> — the pattern shape itself rules all three out, it can never match anything but
/// a plain <c>entries/&lt;slug&gt;/&lt;name&gt;.(persona|meta).json</c> relative path), that its own
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
    // still composes normally into CardPathText/MetaPathText below. `internal` (T101 review finding):
    // this IS the catalog's own slug vocabulary — CatalogController.SlugFormat composes its
    // route-parameter check from this same const rather than inventing a second copy of the shape.
    internal const string SlugSegment = "[a-z0-9]+(-[a-z0-9]+)*";

    // entries/<slug>/<name>.(persona|meta).json — the filename segment is the SAME shape as the
    // slug segment (SPEC F90.2/F89.2: schemas/index.schema.json's card/meta path patterns use this
    // one shape for both segments, not the looser "any run of [a-z0-9-]" a prior version allowed
    // here, which would have tolerated a leading/trailing/doubled hyphen the real schema rejects).
    const string CardPathText = @"\Aentries/" + SlugSegment + "/" + SlugSegment + @"\.persona\.json\z";
    const string MetaPathText = @"\Aentries/" + SlugSegment + "/" + SlugSegment + @"\.meta\.json\z";

    [GeneratedRegex(@"\A" + SlugSegment + @"\z")]
    private static partial Regex SlugPattern();

    [GeneratedRegex(@"\A[a-f0-9]{64}\z")]
    private static partial Regex Sha256Pattern();

    // Split per field so a card can't masquerade as a meta (or vice versa) even though both share
    // the same entries/<slug>/<name>.EXT.json shape.
    [GeneratedRegex(CardPathText)]
    private static partial Regex CardPathPattern();

    [GeneratedRegex(MetaPathText)]
    private static partial Regex MetaPathPattern();

    /// <summary>
    /// Parses and strictly validates a raw index.json payload. On success, every returned
    /// <see cref="CatalogEntrySummary"/> has already passed slug/audience/sha256-shape/path
    /// validation AND the belt-and-braces directory-prefix check. On failure, the WHOLE index is
    /// rejected — <paramref name="rejectionReason"/> names the first offending value (a malformed
    /// entry path names that exact path, per F90.2).
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
            if (!TryValidateEntry(raw, directory, out var summary, out rejectionReason))
            {
                entries = null;
                return false;
            }

            validated.Add(summary);
        }

        entries = validated;
        rejectionReason = null;
        return true;
    }

    /// <summary>
    /// Resolves <paramref name="relativePath"/> against <paramref name="directory"/> and verifies
    /// the result still lives under it (SPEC F90.2's belt-and-braces rule). Every entry path is
    /// already gated by <see cref="CardPathPattern"/>/<see cref="MetaPathPattern"/> before this is
    /// ever called — which, by construction (no <c>..</c>, no scheme, no leading <c>/</c>), makes
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

    static bool TryValidateEntry(
        CatalogIndexEntryJson raw, Uri directory,
        [NotNullWhen(true)] out CatalogEntrySummary? summary,
        [NotNullWhen(false)] out string? reason)
    {
        if (raw.Slug is not { } slug || !SlugPattern().IsMatch(slug))
        {
            summary = null;
            reason = $"invalid slug '{raw.Slug}'";
            return false;
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
                summary = null;
                reason = $"entry '{slug}' has an invalid audience '{raw.Audience}'";
                return false;
        }

        if (!TryValidateFileRef(raw.Card, CardPathPattern(), slug, directory, out var card, out var cardReason))
        {
            summary = null;
            reason = $"entry '{slug}' card {cardReason}";
            return false;
        }

        if (!TryValidateFileRef(raw.Meta, MetaPathPattern(), slug, directory, out var meta, out var metaReason))
        {
            summary = null;
            reason = $"entry '{slug}' meta {metaReason}";
            return false;
        }

        summary = new CatalogEntrySummary(slug, audience, raw.BestFor ?? [], card, meta);
        reason = null;
        return true;
    }

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
        // entries/<slug>/<name>.(persona|meta).json path (SPEC F90.2). The offending raw path is
        // named verbatim in the reason the caller logs as the required WARN.
        if (!pathPattern.IsMatch(path))
        {
            fileRef = null;
            reason = $"path '{path}' is not a valid relative entries/ path";
            return false;
        }

        // The path's own <slug> segment must equal the entry's declared `slug` field (review
        // finding) — otherwise a card/meta could sit under a DIFFERENT entry's directory (still
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
        public string? Audience { get; init; }
        public IReadOnlyList<string>? BestFor { get; init; }
        public CatalogFileRefJson? Card { get; init; }
        public CatalogFileRefJson? Meta { get; init; }
    }

    /// <summary>Ephemeral JSON projection of a raw index.json <c>card</c>/<c>meta</c> file pointer.</summary>
    sealed record CatalogFileRefJson
    {
        public string? Path { get; init; }
        public string? Sha256 { get; init; }
    }
}
