using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using GenWave.Host.Catalog;
using GenWave.Host.Theming;

namespace GenWave.Host.Api;

/// <summary>
/// The gate pipeline shared by BOTH <c>station.theme</c> write routes (SPEC F103.6/F103.10/F104.9/
/// F104.13; <see cref="ThemesImportController.Import"/>, <see cref="ThemesSaveAsOwnController.SaveAsOwn"/>
/// — PLAN T207 review finding F1). Before this type, the two controllers carried a hand-copied,
/// character-identical block each (route slug format → shipped-slug reservation → bounded body read →
/// schema-major → deserialize-as-validation → curated-font provenance/byte-ceiling), documented as
/// "byte-identical by construction" but proven only by a comment — a gate added to one copy tomorrow
/// would silently diverge the other with zero test failures, exactly the invariant
/// <see cref="ThemeFontProvenanceValidator"/>'s own remarks lean on ("this route and
/// <see cref="ThemesSaveAsOwnController"/> are the ONLY two <c>station.theme</c> write paths, and both
/// enforce this SAME gate"). This type is the ONE place that SET and ORDER now live — both controllers
/// call it and do nothing else before their own distinct write, so "same copy" is a property of the
/// code, not a claim a reviewer has to re-verify by diffing two files.
///
/// <para>
/// <b>TWO PHASES, not one call (PLAN T207 review finding B2).</b> A single <c>RunAsync</c> covering
/// every gate end to end was the original shape here — wrong, because
/// <see cref="ThemesImportController.Import"/>'s OWN <c>catalogSlug</c> format/length check (cheap,
/// pure-string, no I/O) must run BETWEEN the route-slug/shipped-slug phase and the
/// read-body/parse/font-law phase, exactly where it always ran before this type existed. Folding the
/// whole pipeline into one call left that route only two legal places to run its own check: entirely
/// before <see cref="ValidateSlug"/> (wrong — a doomed route slug or a shipped-slug collision must
/// still be refused before ANYTHING about <c>catalogSlug</c> is even looked at, the original
/// precedence) or entirely after <see cref="ReadParseAndValidateAsync"/> (the bug this split fixes —
/// it moved a pure-string check BEHIND a bounded body read, a JSON parse, and — for a manifest naming
/// a missing face — a live <see cref="CatalogProxyService.GetIndexAsync"/> round trip a request that
/// used to be rejected before its body was ever read would now pay for). Splitting the pipeline in two
/// gives <see cref="ThemesImportController.Import"/> the seam it always needed: <see cref="ValidateSlug"/>
/// → its own <c>catalogSlug</c> checks → <see cref="ReadParseAndValidateAsync"/>, restoring the exact
/// original precedence. <see cref="ThemesSaveAsOwnController.SaveAsOwn"/>, which has no gate of its
/// own to insert, still runs both phases back to back via <see cref="RunAsync"/> — a thin wrapper, not
/// a second pipeline.
/// </para>
///
/// <para>
/// <b>Gate order.</b> <see cref="ValidateSlug"/>: route slug format (400) → shipped-slug reservation
/// (409, SPEC F103.8, against the DI'd <see cref="ThemeCatalog"/> singleton's fixed shipped set).
/// <see cref="ReadParseAndValidateAsync"/>: bounded body read (413, <see cref="BoundedImportBodyReader"/>)
/// → schema-major (400, SPEC F103.6 AC6, checked ahead of structural parsing so a newer-major manifest
/// that would ALSO fail today's parser still reports the version-naming message —
/// <see cref="ThemeSchemaVersionGate"/>'s own remarks) → deserialize-as-validation (400,
/// <see cref="ThemeManifestParser.Parse"/> — a syntactically malformed body, or any structural defect
/// the parser's own load-time rules catch, maps here rather than an unhandled 500) → curated-font
/// provenance/byte-ceiling (400, SPEC F103.10/F104.9, <see cref="ThemeFontProvenanceValidator"/>,
/// enriched with a providing-pack suggestion via <see cref="FontPackSuggestionBuilder"/> when the
/// catalog index knows one for a missing face, SPEC F104.10).
/// </para>
///
/// <para>
/// <b>Route-neutral <see cref="ThemeManifestSource"/> name, deliberately (PLAN T207 review carry-in
/// 3; the operator-facing copy fixed at review finding on the copy nit).</b> Every refusal body this
/// type builds is byte-identical between the two routes for the SAME input — including a
/// malformed/empty JSON body, whose message embeds the manifest source's own <c>Name</c>
/// (<see cref="ThemeManifestParser.Parse"/>'s own <c>catch (JsonException)</c>/null-document branches).
/// The two controllers used to hand this a caller-specific label (<c>$"import:{slug}"</c> vs
/// <c>$"save-as-own:{slug}"</c>) BEFORE this extraction — a real, if previously untested, gap in
/// STORY-287 AC3's "byte-identical copy" claim, since those two branches are the only ones that ever
/// read the source name rather than the DOCUMENT's own parsed slug. This type closes it by naming the
/// source <paramref name="slug"/> itself, verbatim — no internal-noun prefix (an earlier
/// <c>"theme-write:"</c> label leaked implementation vocabulary into operator-facing 400 copy, e.g.
/// <c>theme manifest 'theme-write:my-remix' is malformed JSON</c> — dropped) — so every gate this type
/// enforces, malformed JSON included, produces the identical, route-neutral refusal body regardless of
/// which route called it.
/// </para>
/// </summary>
internal static partial class ThemeWriteGate
{
    /// <summary>
    /// Phase one — route <paramref name="slug"/> format (400) and shipped-slug reservation (409). No
    /// I/O, no body read: always the FIRST thing either write route does, so a doomed route slug is
    /// refused as cheaply as possible, before <see cref="ThemesImportController.Import"/>'s own
    /// <c>catalogSlug</c> checks or <see cref="ReadParseAndValidateAsync"/>'s body read ever run.
    /// Returns the refusal, or <see langword="null"/> to continue.
    /// </summary>
    public static IActionResult? ValidateSlug(string slug, ThemeCatalog themeCatalog)
    {
        if (!SlugFormat().IsMatch(slug))
            return new BadRequestObjectResult(ImportProblems.BadThemeSlug(slug));

        if (themeCatalog.IsShippedSlug(slug))
            return new ConflictObjectResult(ImportProblems.ShippedSlugReserved(slug));

        return null;
    }

    /// <summary>
    /// Phase two — bounded body read, schema-major, deserialize-as-validation, curated-font
    /// provenance/byte-ceiling, in that order (see this type's own "Gate order" remarks). Assumes
    /// <see cref="ValidateSlug"/> already passed for <paramref name="slug"/> — this method does not
    /// re-check it. Returns EITHER a non-null <paramref name="Refusal"/> result (the response the
    /// caller must return verbatim, nothing written) OR a non-null, already route-slug-normalized
    /// <paramref name="Manifest"/> (the caller's own upsert is what happens next) — never both, never
    /// neither (the same "C#-without-unions tuple idiom" <see cref="FontPackController"/>'s own helpers
    /// already use, narrowed at each call site via <c>is not { } x</c> rather than the null-forgiving
    /// operator).
    /// </summary>
    public static async Task<(IActionResult? Refusal, ThemeManifest? Manifest)> ReadParseAndValidateAsync(
        HttpRequest request,
        string slug,
        InstalledFontCatalog installedFontCatalog,
        CatalogProxyService catalogProxyService,
        CancellationToken ct)
    {
        var (json, oversized) = await BoundedImportBodyReader.ReadBoundedBodyAsync(
            request, BoundedImportBodyReader.MaxImportBytes, ct);
        if (oversized)
            return (OversizedResult(), null);

        // "Two parses, by design" — mirrors this type's callers' own former remarks: a bare
        // JsonDocument parse reads the optional schemaVersion field BEFORE ThemeManifestParser.Parse
        // ever sees the body, so a version-mismatched manifest is refused naming both versions even
        // when its shape would also fail structural parsing. A syntactically malformed body is
        // deliberately NOT reported here — ThemeManifestParser.Parse below throws the one, well-formed
        // message for that.
        int? schemaVersion = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var (version, unreadable) = ThemeSchemaVersionGate.ExtractSchemaVersion(document.RootElement);
            if (unreadable)
                return (new BadRequestObjectResult(ImportProblems.UnreadableSchemaVersion()), null);

            schemaVersion = version;
        }
        catch (JsonException)
        {
            // Malformed JSON — deferred to ThemeManifestParser.Parse below, which throws the same
            // failure as a ThemeManifestException carrying its own well-formed message.
        }

        if (schemaVersion is { } manifestSchemaVersion && manifestSchemaVersion > ThemeSchemaVersionGate.CurrentSchemaVersion)
            return (new BadRequestObjectResult(ImportProblems.NewerSchema(manifestSchemaVersion)), null);

        ThemeManifest manifest;
        try
        {
            // The source name is slug itself — route-neutral by construction, no internal-noun prefix
            // (see this type's own "Route-neutral ThemeManifestSource name" remarks, the copy-nit fix).
            manifest = ThemeManifestParser.Parse(new ThemeManifestSource(slug, json));
        }
        catch (ThemeManifestException ex)
        {
            return (new BadRequestObjectResult(ImportProblems.MalformedManifest(ex.Message)), null);
        }

        // SPEC F103.10/F104.9 — the SAME gate both callers enforce; installedFacesBySrc is captured
        // ONCE and reused for both this call and the enrichment call below, rather than read a second
        // time on the refusal path only.
        var installedFacesBySrc = installedFontCatalog.InstalledByteSizeBySrc();
        try
        {
            ThemeFontProvenanceValidator.Validate(
                manifest, FontProvenanceCatalog.Default.BySrc, ThemeFontProvenanceValidator.PerThemeByteCeilingBytes,
                installedFacesBySrc);
        }
        catch (ThemeManifestException ex)
        {
            var detail = await FontPackSuggestionBuilder.BuildUnvendoredFontDetailAsync(
                manifest, ex.Message, installedFacesBySrc, catalogProxyService, ct);
            return (new BadRequestObjectResult(ImportProblems.UnvendoredFont(detail)), null);
        }

        return (null, NormalizeSlug(manifest, slug));
    }

    /// <summary>
    /// <see cref="ValidateSlug"/> then <see cref="ReadParseAndValidateAsync"/>, back to back — the
    /// composition <see cref="ThemesSaveAsOwnController.SaveAsOwn"/> uses, which has no gate of its own
    /// to run between the two phases (unlike <see cref="ThemesImportController.Import"/>'s own
    /// <c>catalogSlug</c> checks — see this type's own "TWO PHASES" remarks for why that route calls
    /// the two phases directly instead of this wrapper).
    /// </summary>
    public static async Task<(IActionResult? Refusal, ThemeManifest? Manifest)> RunAsync(
        HttpRequest request,
        string slug,
        ThemeCatalog themeCatalog,
        InstalledFontCatalog installedFontCatalog,
        CatalogProxyService catalogProxyService,
        CancellationToken ct)
    {
        if (ValidateSlug(slug, themeCatalog) is { } refusal)
            return (refusal, null);

        return await ReadParseAndValidateAsync(request, slug, installedFontCatalog, catalogProxyService, ct);
    }

    static ObjectResult OversizedResult() =>
        new(ImportProblems.Oversized()) { StatusCode = StatusCodes.Status413PayloadTooLarge };

    /// <summary>Re-stamps <paramref name="manifest"/>'s own <see cref="ThemeManifest.Slug"/> to the
    /// route <paramref name="routeSlug"/> — the manifest's OWN embedded slug never governs storage
    /// (both callers' own former remarks, "Slug is the upsert key, not the manifest's own opinion");
    /// without this, a client that posted a body whose own <c>"slug"</c> field disagreed with the route
    /// would store under one slug while <see cref="ThemeCatalog"/> re-parses <c>definition</c> and
    /// indexes the result under another.</summary>
    static ThemeManifest NormalizeSlug(ThemeManifest manifest, string routeSlug) =>
        manifest with { Slug = routeSlug };

    // Composed from CatalogIndexValidator.SlugSegment — mirrors CatalogController.SlugFormat's own
    // \A/\z-anchored composition (see that member's remarks for why, not ^/$).
    [GeneratedRegex(@"\A" + CatalogIndexValidator.SlugSegment + @"\z")]
    private static partial Regex SlugFormat();
}
