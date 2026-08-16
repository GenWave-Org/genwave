using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using GenWave.Host.Catalog;

namespace GenWave.Host.Api;

/// <summary>
/// The shared "install a Dean-curated pack from the Community Catalog" shell (SPEC F104.5/F128.3) —
/// slug gates, the kill-switch bare-404, entry-kind resolution, and the per-asset fetch loop every such
/// route needs, plus their common <see cref="ProblemDetails"/> factories. Extracted (T293 review
/// finding S6, the "shared control, one home" idiom <see cref="ImportProblems"/>/
/// <see cref="BoundedImportBodyReader"/> already established for the portable-JSON theme routes, PLAN
/// T184 review F4) once <see cref="AvatarPackController"/> became a SECOND near-verbatim copy of
/// <see cref="FontPackController"/>'s own shape — the third-copy precedent this codebase already
/// follows elsewhere (<c>GuardedRouteInspector</c>'s own extraction, PLAN T209 review finding N3) is
/// pulled forward here to the second copy instead, since the two controllers' own remarks already
/// documented the duplication as deliberate mirroring, not drift.
///
/// <para>
/// Deliberately a STATIC helper set, never a DI-registered service — mirrors
/// <see cref="ImportProblems"/>/<see cref="BoundedImportBodyReader"/>'s own shape: nothing here carries
/// state across calls, so injecting it would only cost every consumer a constructor parameter for no
/// behaviour a singleton would add. <c>tools/check-seam-index.sh</c> only tracks live DI registrations
/// (SEAMS.md's own "generated from the composition root" contract) — this type is correctly invisible
/// to it, the same way its two static siblings already are.
/// </para>
///
/// <para>
/// Differences between a font pack and an avatar pack that stay OUTSIDE this shell, deliberately: each
/// controller keeps its OWN <c>MaxPackBytes</c> (a magnitude + SPEC citation this shell only ever
/// receives as parameters, never owns), its own manifest cross-check/dedupe rule (a font pack dedupes
/// by FILE, an avatar pack by ITEM NAME — different vocabularies, not a shared shape), its own
/// re-install/upsert/rebuild-after-write tail, and — naturally — its own business-specific refusals
/// (<see cref="FontPackController"/>'s 23505 collision mapping has no avatar-side counterpart at all).
/// </para>
/// </summary>
internal static partial class CatalogInstallShell
{
    /// <summary>Cheap reject before the regex, shared by every route's slug AND its own kill-switch
    /// gate (mirrors <c>CatalogController.MaxSlugLength</c>'s own reasoning) — a real catalog entry
    /// slug is a short, human-authored identifier, never anywhere near this long.</summary>
    public const int MaxSlugLength = 64;

    // Composed from CatalogIndexValidator.SlugSegment — mirrors CatalogController.SlugFormat's own
    // \A/\z-anchored composition (see that member's remarks for why, not ^/$). A [GeneratedRegex]
    // partial method works identically on a shared static home as it did on each controller's own
    // partial class — the source generator only needs a partial declaration to attach to, not any
    // particular kind of container.
    [GeneratedRegex(@"\A" + CatalogIndexValidator.SlugSegment + @"\z")]
    public static partial Regex SlugFormat();

    /// <summary>Bare, zero-byte 404 (F87.2/F61 posture) — mirrors <c>CatalogController.DisabledSurfaceResult</c>'s
    /// own remarks. Takes <paramref name="response"/> directly rather than a <see cref="ControllerBase"/>
    /// — the one member here that needs anything from the calling action besides its own
    /// arguments.</summary>
    public static IActionResult DisabledSurfaceResult(HttpResponse response)
    {
        response.StatusCode = StatusCodes.Status404NotFound;
        return new EmptyResult();
    }

    public static ProblemDetails BadSlugProblem(string slug) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Invalid slug.",
        Detail = $"\"{slug}\" is not a valid catalog entry slug (lowercase letters, digits, and single hyphens only).",
    };

    public static ProblemDetails SlugTooLongProblem(int length) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Invalid slug.",
        Detail = $"slug must be at most {MaxSlugLength} characters (got {length}).",
    };

    public static ProblemDetails CatalogUnavailableProblem() => new()
    {
        Status = StatusCodes.Status503ServiceUnavailable,
        Title  = "Persona catalog unavailable.",
        Detail = "The catalog is currently unreachable. Try again shortly.",
    };

    /// <summary>
    /// The shared 502 body every withheld-asset/entry case returns — <paramref name="kind"/> (e.g.
    /// <c>"font"</c>, <c>"avatar"</c>) only ever affects the TITLE's own "{Kind} pack unavailable."; the
    /// detail text is identical across every kind (mirrors <c>FontPackController</c>/
    /// <c>AvatarPackController</c>'s own byte-identical <c>WithheldProblem</c> bodies before this
    /// extraction). Deliberately no slug/hash/upstream detail here (F15.7) — that detail is already in
    /// the WARN <see cref="CatalogProxyService"/> logs server-side.
    /// </summary>
    public static ProblemDetails WithheldProblem(string kind, string reason) => new()
    {
        Status = StatusCodes.Status502BadGateway,
        Title  = $"{Capitalize(kind)} pack unavailable.",
        Detail = $"This pack {reason} and was withheld. Try again shortly.",
    };

    public static ProblemDetails UnknownPackProblem(string kind, string slug) => new()
    {
        Status = StatusCodes.Status404NotFound,
        Title  = "Not found.",
        Detail = $"No installable {kind} pack with slug \"{slug}\" exists.",
    };

    // Distinct from UnknownPackProblem above: that one names a CATALOG entry an install route couldn't
    // resolve; this one names an INSTALLED pack an uninstall route couldn't find.
    public static ProblemDetails UnknownInstalledPackProblem(string kind, string slug) => new()
    {
        Status = StatusCodes.Status404NotFound,
        Title  = "Not found.",
        Detail = $"No installed {kind} pack with slug \"{slug}\" exists.",
    };

    public static ProblemDetails MalformedManifestProblem(string kind, string slug) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = $"Malformed {kind} pack manifest.",
        Detail = $"\"{slug}\"'s {kind} manifest could not be parsed.",
    };

    /// <summary>
    /// <paramref name="file"/> is a manifest-declared filename off an UNTRUSTED, remote origin — passed
    /// through <see cref="LogSafeText.Sanitize"/> (review finding S2) rather than interpolated raw, the
    /// same "never echo a remote string unbounded into a body" discipline every other Problem factory
    /// on this type already gets for free by carrying no remote free-text field at all.
    /// </summary>
    public static ProblemDetails UndeclaredManifestAssetProblem(string kind, string slug, string file) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = $"Malformed {kind} pack manifest.",
        Detail = $"\"{slug}\"'s manifest references \"{LogSafeText.Sanitize(file)}\", which is not one of its declared catalog assets.",
    };

    public static ProblemDetails PackTooLargeProblem(string kind, string slug, long totalBytes, long maxPackBytes, string specRef) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = $"{Capitalize(kind)} pack exceeds the size ceiling.",
        Detail = $"\"{slug}\" totals {totalBytes} bytes, over the {maxPackBytes}-byte pack ceiling ({specRef}).",
    };

    /// <summary>
    /// A slug-shaped hint's shape gate (SPEC F118.3/F128.1) — the SAME rule
    /// <c>CatalogController.ValidateSuggestedPersonaShape</c> already applies to a show entry's own
    /// <c>suggestedPersona</c> meta field on the EPHEMERAL shelf projection (review finding S2:
    /// <see cref="AvatarPackController"/>'s own DURABLE write path was letting an avatar item's
    /// <c>suggestedPersona</c> flow unshaped into <c>station.avatar_pack_item</c>). Reuses THIS type's
    /// own <see cref="MaxSlugLength"/>/<see cref="SlugFormat"/> — the identical bound/pattern a real
    /// catalog slug is held to everywhere on this shell — rather than a second, independently-drifting
    /// copy of the shape. Degrades to <see langword="null"/>, never rejects the whole install: a
    /// suggestion is an OFFER (SPEC F128.5), not a value this route depends on to do its own job.
    /// </summary>
    public static string? ValidateSuggestedPersonaShape(string? suggestedPersona) =>
        suggestedPersona is { Length: > 0 and <= MaxSlugLength } candidate && SlugFormat().IsMatch(candidate)
            ? candidate
            : null;

    static string Capitalize(string kind) => char.ToUpperInvariant(kind[0]) + kind[1..];

    /// <summary>
    /// Resolves the catalog entry and confirms its kind matches <paramref name="expectedKind"/>. A
    /// non-null <see cref="IActionResult"/> error is always paired with a null
    /// <see cref="CatalogEntryContent"/>, and vice versa — the C#-without-unions tuple idiom every
    /// helper here follows, narrowed at each call site via an explicit <c>is not { } x</c> check rather
    /// than the null-forgiving operator.
    /// </summary>
    public static async Task<(IActionResult? Error, CatalogEntryContent? Content)> ResolveEntryAsync(
        CatalogProxyService catalogProxyService, string kind, CatalogEntryKind expectedKind, string slug, CancellationToken ct)
    {
        var result = await catalogProxyService.GetEntryAsync(slug, ct);
        switch (result)
        {
            case CatalogEntryFetchResult.Ok ok when ok.Content.Kind == expectedKind:
                return (null, ok.Content);
            case CatalogEntryFetchResult.Ok or CatalogEntryFetchResult.NotFound:
                // A real entry that just isn't this kind of pack gets the SAME "unknown pack" refusal
                // as a slug naming nothing at all — no route here has any business revealing that a
                // different-kind entry exists under this slug.
                return (new NotFoundObjectResult(UnknownPackProblem(kind, slug)), null);
            case CatalogEntryFetchResult.Unreachable:
                return (Status503(CatalogUnavailableProblem()), null);
            case CatalogEntryFetchResult.HashMismatch:
                return (Status502(WithheldProblem(kind, "failed its integrity check")), null);
            case CatalogEntryFetchResult.Oversize:
                return (Status502(WithheldProblem(kind, "exceeded its size limit")), null);
            default:
                // CatalogEntryFetchResult's constructor is private (closed hierarchy) — this arm can
                // never actually run; mirrors CatalogController's own discard-arm remarks.
                throw new UnreachableException($"Unhandled {nameof(CatalogEntryFetchResult)} case.");
        }
    }

    /// <summary>
    /// Fetches and hash-verifies EVERY asset the resolved entry declares (not just a manifest's own
    /// item/file subset), summing each fetched asset's own byte length against
    /// <paramref name="maxPackBytes"/> INSIDE the loop, refusing the INSTANT it is crossed — the early
    /// cutoff discipline <see cref="FontPackController.FetchAllAssetsAsync"/>'s own N1 review finding
    /// established, never only after every declared asset is already buffered.
    /// <paramref name="assetByteCeiling"/> is the defense-in-depth re-check on each individual fetched
    /// asset's own size (<paramref name="kind"/>'s real per-asset transport cap —
    /// <see cref="CatalogProxyService"/> already enforces it during the fetch itself; this is a second,
    /// belt-and-suspenders look at the same invariant, mirrors each former per-controller copy's own
    /// "verify, don't re-trust" remarks). Every returned asset carries its own <c>Sha256</c> — the
    /// index's own pinned hash for that asset — even though only a font pack's own write path still
    /// reads it back out (an avatar pack's own stored hash is instead the LATER re-encode's own hash,
    /// see <see cref="AvatarPackItemInput.Sha256"/>'s own remarks); returning it unconditionally keeps
    /// this one fetch shape usable by both callers rather than a font-only return type.
    /// </summary>
    public static async Task<(IActionResult? Error, Dictionary<string, CatalogFetchedAsset>? Assets)> FetchAllAssetsAsync(
        CatalogProxyService catalogProxyService, string kind, string slug, CatalogEntryContent content,
        long assetByteCeiling, long maxPackBytes, string maxPackBytesSpecRef, CancellationToken ct)
    {
        var fetched = new Dictionary<string, CatalogFetchedAsset>(StringComparer.Ordinal);
        long totalBytes = 0;

        foreach (var assetRef in content.Assets)
        {
            var file = Path.GetFileName(assetRef.Path);
            var result = await catalogProxyService.GetAssetAsync(slug, file, ct);
            switch (result)
            {
                case CatalogAssetFetchResult.Ok ok:
                    if (ok.Bytes.LongLength > assetByteCeiling)
                        return (Status502(WithheldProblem(kind, "exceeded its size limit")), null);

                    totalBytes += ok.Bytes.LongLength;

                    if (totalBytes > maxPackBytes)
                        return (new BadRequestObjectResult(PackTooLargeProblem(kind, slug, totalBytes, maxPackBytes, maxPackBytesSpecRef)), null);

                    fetched[file] = new CatalogFetchedAsset(ok.Bytes, assetRef.Sha256);
                    break;
                case CatalogAssetFetchResult.HashMismatch:
                    return (Status502(WithheldProblem(kind, "failed its integrity check")), null);
                case CatalogAssetFetchResult.Oversize:
                    return (Status502(WithheldProblem(kind, "exceeded its size limit")), null);
                case CatalogAssetFetchResult.Unreachable:
                    return (Status503(CatalogUnavailableProblem()), null);
                case CatalogAssetFetchResult.NotFound:
                    // The index changed out from under this request between GetEntryAsync and this
                    // call (a rare TOCTOU race, never a client input error) — the same withheld
                    // posture as a hash mismatch: this asset could not be cleanly fetched.
                    return (Status502(WithheldProblem(kind, "could not be fetched")), null);
                default:
                    throw new UnreachableException($"Unhandled {nameof(CatalogAssetFetchResult)} case.");
            }
        }

        return (null, fetched);
    }

    static ObjectResult Status502(ProblemDetails problem) => new(problem) { StatusCode = StatusCodes.Status502BadGateway };
    static ObjectResult Status503(ProblemDetails problem) => new(problem) { StatusCode = StatusCodes.Status503ServiceUnavailable };

    /// <summary>One already hash-verified asset's bytes plus the index-pinned <see cref="Sha256"/> a
    /// font pack's own write path stores verbatim — an avatar pack's own write path reads only
    /// <see cref="Bytes"/> (see <see cref="FetchAllAssetsAsync"/>'s own remarks for why).</summary>
    public sealed record CatalogFetchedAsset(byte[] Bytes, string Sha256);
}
