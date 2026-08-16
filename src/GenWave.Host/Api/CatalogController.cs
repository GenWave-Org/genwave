using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Host.Catalog;
using GenWave.Host.Icons;
using GenWave.Host.Options;

namespace GenWave.Host.Api;

/// <summary>
/// The Persona Catalog browse surface (SPEC F90.2, STORY-234, PLAN T101): <c>GET /api/catalog/index</c>
/// and <c>GET /api/catalog/entries/{slug}</c>, the two read-only routes the Admin UI's shelf browse
/// (a later task) will consume. Every fetch/verify/cache/single-flight concern lives in
/// <see cref="CatalogProxyService"/> (SPEC F90.2-F90.4, PLAN T100) — this controller's ENTIRE job is
/// translating that service's closed-hierarchy results into HTTP, never re-deriving any of its own
/// rules.
///
/// <para>
/// POLICY PARITY (SPEC F90.2's own "same auth policy as the existing persona import endpoint" rule):
/// <see cref="AdminSurfaceAttribute"/> + <see cref="AuthorizationPolicies.Settings"/> — the EXACT
/// shape <c>PersonaController</c>'s <c>Import</c> action carries, since importing a catalog card
/// ends at that same action (SPEC F90.5). No rate limiting (T101 review): every other admin-plane
/// controller in this codebase carries none — rate limiting here is reserved for public/
/// unauthenticated surfaces (spectator requests, login) where an anonymous caller can hammer a route
/// for free; this one already costs a valid admin session.
/// </para>
///
/// <para>
/// DISABLED (SPEC F90.1) IS A BARE 404, NOT A PROBLEM BODY: <see cref="CommunityCatalogAccessor.IsEnabled"/>
/// false means the catalog surface itself does not exist right now — the same "reveals nothing, not
/// even that a feature flag exists" posture <see cref="AdminSurfaceAttribute"/>'s own surface-gate
/// idiom already uses for <c>Admin:Enabled</c>/<c>Station:SpectatorMode</c> (see
/// <see cref="SurfaceGateMiddleware"/>). Checked here, in-action, rather than via a THIRD
/// <c>SurfaceGateMiddleware</c> attribute+static-config-boolean pair: those two existing gates read a
/// boolean decided BEFORE routing needs to know anything about the request; this one is decided by
/// the exact same live read (<see cref="CommunityCatalogAccessor.IsEnabled"/>) the controller needs
/// anyway to know WHAT to fetch, so a parallel middleware-level copy of that same read would be
/// duplication, not reuse.
/// </para>
///
/// <para>
/// UNREACHABLE IS A GRACEFUL 200, NEVER A NON-2XX STATUS (design choice, T101): once the catalog is
/// enabled, both routes stay 200 even with a cold cache or a rejected/unreachable origin —
/// <see cref="CatalogIndexResponse.Unreachable"/>/<see cref="CatalogEntryResponse.Unreachable"/> carry
/// the signal instead. This mirrors the two existing house idioms for a degraded UI-facing read
/// (<c>SpectatorTrackNowPlaying.Listeners</c> going null when Icecast stats are unreachable;
/// <c>StatusController</c>'s <c>llm.lastOutcome</c> going null with no LLM attempt yet — both "state
/// embedded in a 200", never an HTTP error status for "nothing to show yet") AND ARCHITECTURE.md's
/// own Persona Catalog section, verbatim: "Offline/unreachable = a graceful empty-state, never an
/// error page." A 503 was the other candidate (matches <c>VoicesController</c>'s 502-on-unreachable
/// posture) but that shape fits a SINGLE external dependency a caller either gets or doesn't
/// (<c>ITtsVoiceLister</c> underpins exactly one dropdown); the catalog index is a LIST the Admin UI
/// renders as a page, and a page that 5xxs on a cold cache is precisely the "error page"
/// ARCHITECTURE.md rules out. <see cref="CatalogEntryFetchResult.Unreachable"/> reuses the SAME shape
/// at the entry route for the one narrow race it represents (the index went unreachable between page
/// load and a detail click) — deliberately distinct from <see cref="CatalogEntryFetchResult.NotFound"/>
/// (a real, durable "no such slug"), which still 404s.
/// </para>
///
/// <para>
/// HASH MISMATCH / OVERSIZE ARE 502, NOT THE GRACEFUL SHAPE (SPEC F90.3): both are the ONE case
/// where the origin answered but served something this station refuses to relay — a genuine
/// upstream-integrity failure, the textbook meaning of Bad Gateway, and different in kind from
/// "nothing to show" (an empty/cold shelf is not an error; a tampered or oversize file is). The
/// response body never echoes the slug/hash values <see cref="CatalogProxyService"/> already WARNs
/// server-side (mirrors <c>VoicesController</c>'s own "no internal detail in a 502 body" posture,
/// F15.7) — an operator reads the WARN in the logs, not the browser.
/// </para>
/// </summary>
[ApiController]
[Route("api/catalog")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Settings)]
public sealed partial class CatalogController(
    CatalogProxyService catalogProxyService, CommunityCatalogAccessor catalogAccessor) : ControllerBase
{
    /// <summary>
    /// GET /api/catalog/index — the shelf listing (SPEC F90.2, F90.4). 404 (bare) when the catalog
    /// is disabled (F90.1); otherwise always 200 — see this controller's own remarks for why
    /// <see cref="CatalogIndexFetchResult.Unreachable"/> is embedded rather than a non-2xx status.
    /// </summary>
    [HttpGet("index")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (!catalogAccessor.IsEnabled)
            return DisabledSurfaceResult();

        var result = await catalogProxyService.GetIndexAsync(ct);

        return result switch
        {
            CatalogIndexFetchResult.Ok ok => Ok(new CatalogIndexResponse(
                ok.Entries.Select(ToShelfEntryDto).ToArray(), ok.FetchedAt, Unreachable: false)),
            CatalogIndexFetchResult.Unreachable => Ok(new CatalogIndexResponse(null, null, Unreachable: true)),
            // CatalogIndexFetchResult's constructor is private (closed hierarchy) — this arm can
            // never actually run; kept because Roslyn's pattern-exhaustiveness checker doesn't treat
            // a private-constructor closed hierarchy as provably exhaustive (mirrors
            // CatalogProxyService's own discard arms).
            _ => throw new UnreachableException($"Unhandled {nameof(CatalogIndexFetchResult)} case."),
        };
    }

    /// <summary>
    /// GET /api/catalog/entries/{slug} — one entry's hash-verified card + meta content (SPEC F90.2,
    /// F90.3). 404 (bare) when the catalog is disabled (F90.1); 400 for an over-length or malformed
    /// slug (see <see cref="MaxSlugLength"/>/<see cref="SlugFormat"/>); 404 (with a body) for a
    /// well-formed but unknown slug; 502 when the fetched content fails its F90.3 integrity check
    /// (hash mismatch or oversize — WARN already logged by <see cref="CatalogProxyService"/>);
    /// otherwise 200, or the graceful <see cref="CatalogEntryResponse.Unreachable"/> shape — see this
    /// controller's own remarks.
    /// </summary>
    [HttpGet("entries/{slug}")]
    public async Task<IActionResult> Entry(string slug, CancellationToken ct)
    {
        if (!catalogAccessor.IsEnabled)
            return DisabledSurfaceResult();

        if (slug.Length > MaxSlugLength)
            return BadRequest(SlugTooLongProblem(slug.Length));

        if (!SlugFormat().IsMatch(slug))
            return BadRequest(BadSlugProblem(slug));

        var result = await catalogProxyService.GetEntryAsync(slug, ct);

        return result switch
        {
            CatalogEntryFetchResult.Ok ok => Ok(ToEntryResponse(ok)),
            CatalogEntryFetchResult.NotFound => NotFound(UnknownEntryProblem(slug)),
            CatalogEntryFetchResult.Unreachable => Ok(CatalogEntryResponse.UnreachableCatalog()),
            CatalogEntryFetchResult.HashMismatch =>
                StatusCode(StatusCodes.Status502BadGateway, WithheldProblem("failed its integrity check")),
            CatalogEntryFetchResult.Oversize =>
                StatusCode(StatusCodes.Status502BadGateway, WithheldProblem("exceeded its size limit")),
            // CatalogEntryFetchResult's constructor is private (closed hierarchy) — see Index's own
            // discard arm for why this still needs one.
            _ => throw new UnreachableException($"Unhandled {nameof(CatalogEntryFetchResult)} case."),
        };
    }

    /// <summary>
    /// GET /api/catalog/entries/{slug}/assets/{file} — one binary asset's hash-verified bytes (SPEC
    /// F104.1, F104.4, T194): a font pack's woff2 face today, its OFL licence text too since the
    /// route is asset-generic, never kind-specific. Same disabled/slug-shape guards as
    /// <see cref="Entry"/>; <paramref name="file"/> gets only a cheap length bound (T101's own
    /// <see cref="MaxSlugLength"/> precedent) — no shape regex, because
    /// <see cref="CatalogProxyService.GetAssetAsync"/> only ever matches it for EQUALITY against the
    /// resolved entry's own already-validated asset filenames (mirrors <c>FontEndpoints</c>' "compared
    /// for equality, never concatenated into a path" posture) — anything not naming a real asset is
    /// simply <see cref="CatalogAssetFetchResult.NotFound"/>, the same shape an unknown slug gets.
    ///
    /// <para>
    /// NO-STORE (SPEC F104.4 — "transient... nothing persists, nothing is served station-wide"):
    /// <see cref="AssetFileResult"/> sets <c>Cache-Control: no-store</c> EXPLICITLY, alongside
    /// <c>X-Content-Type-Options: nosniff</c> (mirrors <c>FontEndpoints</c>' own stamp) — NOT left to
    /// <see cref="NoCacheApiMiddleware"/> alone (F5 review finding): that middleware only writes its
    /// headers <c>if (!Response.HasStarted)</c>, a best-effort guard silently dropped once a large
    /// streamed asset's body has already begun flushing by the time the middleware runs on the way
    /// back out — the exact shape a font pack's woff2 face IS. This is deliberately the OPPOSITE
    /// posture from <c>FontEndpoints</c>' installed/vendored faces (immutable, one-year cache): those
    /// are permanent, filename-versioned station assets; this is a transient, never-installed preview
    /// of someone else's content, so a fresh admin request always re-verifies through this station's
    /// own hash check rather than trusting a stale local copy.
    /// </para>
    ///
    /// <para>
    /// STATUS CODES: 404 (bare) disabled catalog; 400 malformed/over-length slug or file; 404 (body)
    /// unknown slug or unknown asset for that slug; 502 hash mismatch/oversize — the SAME upstream
    /// integrity posture <see cref="Entry"/> already uses (mirrors that action's own WithheldProblem);
    /// 503 when the catalog itself is currently unreachable — UNLIKE <see cref="Entry"/>'s graceful
    /// embedded-flag 200, a binary response has no JSON envelope to carry that signal in, so this is
    /// the one route on this controller where "nothing to show yet" is a real non-2xx status rather
    /// than state embedded in a 200 (a deliberate, stated deviation from this controller's own
    /// class-level UNREACHABLE remarks, forced by the response's raw-bytes shape).
    /// </para>
    /// </summary>
    [HttpGet("entries/{slug}/assets/{file}")]
    public async Task<IActionResult> Asset(string slug, string file, CancellationToken ct)
    {
        if (!catalogAccessor.IsEnabled)
            return DisabledSurfaceResult();

        if (slug.Length > MaxSlugLength)
            return BadRequest(SlugTooLongProblem(slug.Length));

        if (!SlugFormat().IsMatch(slug))
            return BadRequest(BadSlugProblem(slug));

        if (file.Length > MaxAssetFileLength)
            return BadRequest(AssetFileTooLongProblem(file.Length));

        var result = await catalogProxyService.GetAssetAsync(slug, file, ct);

        return result switch
        {
            CatalogAssetFetchResult.Ok ok => AssetFileResult(ok.Bytes, file),
            CatalogAssetFetchResult.NotFound => NotFound(UnknownAssetProblem(slug, file)),
            CatalogAssetFetchResult.Unreachable =>
                StatusCode(StatusCodes.Status503ServiceUnavailable, CatalogUnavailableProblem()),
            CatalogAssetFetchResult.HashMismatch =>
                StatusCode(StatusCodes.Status502BadGateway, WithheldProblem("failed its integrity check")),
            CatalogAssetFetchResult.Oversize =>
                StatusCode(StatusCodes.Status502BadGateway, WithheldProblem("exceeded its size limit")),
            // CatalogAssetFetchResult's constructor is private (closed hierarchy) — see Index's own
            // discard arm for why this still needs one.
            _ => throw new UnreachableException($"Unhandled {nameof(CatalogAssetFetchResult)} case."),
        };
    }

    FileContentResult AssetFileResult(byte[] bytes, string file)
    {
        // Explicit, not left to NoCacheApiMiddleware alone (F5 review finding) — see this action's
        // own NO-STORE remarks.
        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(bytes, AssetContentType(file));
    }

    /// <summary><c>font/woff2</c> matches <c>FontEndpoints</c>' own vendored-face content type; <c>image/png</c> (SPEC F128.1, review finding — an avatar pack item/persona sidecar face is a real PNG, not opaque bytes) matches the .woff2 arm's own precedent rather than falling back to the generic binary type; the pack's OFL.txt (never a specimen itself, but served by the SAME asset-generic route) gets a plain-text type; anything else this pattern doesn't recognise falls back to a generic binary type rather than guessing.</summary>
    static string AssetContentType(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".woff2" => "font/woff2",
        ".png" => "image/png",
        ".txt" => "text/plain; charset=utf-8",
        _ => "application/octet-stream",
    };

    static CatalogShelfEntryDto ToShelfEntryDto(CatalogEntrySummary summary) =>
        new(summary.Slug, ToWireKind(summary.Kind), ToWireAudience(summary.Audience), summary.BestFor,
            ToPreviewDto(summary.Preview), ToFontByteTotal(summary), summary.Family);

    // SPEC F104.3's shelf-card byte total — zero fetch, straight off the index's own asset refs.
    // summary.Family (STORY-281 AC1 reconciliation, T194 review finding) needs no equivalent helper
    // here — CatalogIndexValidator already kind-gates it to font-only, null otherwise, so it passes
    // straight through above; see CatalogShelfEntryDto.FontFamily's own remarks.
    static long? ToFontByteTotal(CatalogEntrySummary summary) =>
        summary.Kind == CatalogEntryKind.Font ? summary.Assets.Sum(a => a.Bytes) : null;

    // Null exactly when CatalogEntrySummary.Preview is (every persona entry, and a theme entry
    // whose index carries none, T185) — see that property's own remarks.
    static CatalogShelfPreviewDto? ToPreviewDto(CatalogThemePreview? preview) =>
        preview is null ? null : new CatalogShelfPreviewDto(ToSwatchSetDto(preview.Light), ToSwatchSetDto(preview.Dark));

    static CatalogShelfSwatchSetDto ToSwatchSetDto(CatalogThemeSwatchSet swatches) =>
        new(swatches.Bg, swatches.Surface, swatches.Ink, swatches.Accent, swatches.Accent2);

    // Lowercase, matching genwave-catalog's own schema vocabulary verbatim — see
    // CatalogShelfEntryDto's own remarks on why this is never the enum's default PascalCase
    // serialization. F104.1/S1 review finding: this switch REJECTED CatalogEntryKind.Font (the
    // default arm threw UnreachableException), 500ing BOTH routes the instant a font entry ever
    // reached either projection below — CatalogIndexValidator had already learned to ADMIT the
    // kind (T193), but this switch, the one place a summary's Kind becomes a wire string, had not.
    // Every CatalogEntryKind member must have an arm here — there is no forward-compat "skip"
    // available at this layer (unlike CatalogIndexValidator.TryResolveKind's own unrecognised-kind
    // posture): by the time a CatalogEntrySummary/CatalogEntryContent exists, its Kind has ALREADY
    // been validated as a member of this enum, so a missing arm is a genuine coding bug, not
    // forward-compat data — UnreachableException stays the right shape for every OTHER member, it
    // was only ever wrong for a kind this app now legitimately ships.
    static string ToWireKind(CatalogEntryKind kind) => kind switch
    {
        CatalogEntryKind.Persona => "persona",
        CatalogEntryKind.Theme => "theme",
        CatalogEntryKind.Font => "font",
        CatalogEntryKind.Show => "show",
        CatalogEntryKind.Avatar => "avatar",
        CatalogEntryKind.Icon => "icon",
        _ => throw new UnreachableException($"Unhandled {nameof(CatalogEntryKind)} value: {kind}."),
    };

    static string ToWireAudience(CatalogAudience audience) => audience switch
    {
        CatalogAudience.Everyone => "everyone",
        CatalogAudience.Mature => "mature",
        _ => throw new UnreachableException($"Unhandled {nameof(CatalogAudience)} value: {audience}."),
    };

    /// <summary>
    /// T102's DTO extension (see <see cref="CatalogEntryResponse"/>'s own remarks): projects
    /// <see cref="CatalogEntryContent"/> — already hash-verified, already carrying
    /// audience/bestFor straight off the index — into the full wire shape the shelf's detail panel
    /// reads, parsing <see cref="CatalogEntryContent.MetaJson"/> ONLY for the three display fields
    /// (author/description/samplePatter) meta.json alone carries.
    /// </summary>
    static CatalogEntryResponse ToEntryResponse(CatalogEntryFetchResult.Ok ok)
    {
        var meta = ParseMetaFields(ok.Content.MetaJson);
        var isFont = ok.Content.Kind == CatalogEntryKind.Font;
        var isShow = ok.Content.Kind == CatalogEntryKind.Show;
        var isAvatar = ok.Content.Kind == CatalogEntryKind.Avatar;
        var isPersona = ok.Content.Kind == CatalogEntryKind.Persona;
        var isIcon = ok.Content.Kind == CatalogEntryKind.Icon;
        var fontManifest = isFont ? CatalogFontManifestSerializer.Deserialize(ok.Content.ManifestJson) : null;
        var avatarManifest = isAvatar ? CatalogAvatarPackManifestSerializer.Deserialize(ok.Content.ManifestJson) : null;
        var iconDefinition = isIcon ? ResolveIconCount(ok.Content.ManifestJson) : null;
        return new CatalogEntryResponse(
            ok.Content.ManifestJson,
            ok.Content.MetaJson,
            ok.FetchedAt,
            Unreachable: false,
            ToWireKind(ok.Content.Kind),
            ToWireAudience(ok.Content.Audience),
            ok.Content.BestFor,
            meta.Author,
            meta.Description,
            meta.SamplePatter ?? [],
            FontFamily: fontManifest?.Family,
            FontByteTotal: isFont ? ok.Content.Assets.Sum(a => a.Bytes) : null,
            FontSpecimenFile: ResolveSpecimenFile(fontManifest, ok.Content.Assets),
            FontLicense: fontManifest?.License,
            FontVersion: fontManifest?.Version,
            FontSubset: fontManifest?.Subset,
            SuggestedPersona: isShow ? ValidateSuggestedPersonaShape(meta.SuggestedPersona) : null,
            AvatarItems: avatarManifest?.Items.Select(item => ToAvatarItemDto(item, ok.Content.Assets)).ToArray(),
            PersonaAvatarFile: isPersona ? ResolvePersonaAvatarFile(ok.Content.Assets) : null,
            PackName: avatarManifest?.PackName,
            IconCount: iconDefinition);
    }

    /// <summary>
    /// An icon pack entry's own declared icon count (SPEC F130.1, PLAN T304 rider 4) — re-validates
    /// <paramref name="manifestJson"/> (the already-fetched, hash-verified <c>.icon.json</c> text)
    /// through the SAME whitelist gate <see cref="IconPackController.Install"/> runs at install time,
    /// at zero extra network cost. A pre-install manifest has never been through that gate before —
    /// unlike <see cref="IconPackController.ToSummaryDto"/>'s own cheap key-count for an ALREADY
    /// installed (and so already-canonical) pack's listing row, this is the one honest count a
    /// not-yet-installed entry can offer, so the full <see cref="IconPackDefinitionParser.Validate"/>
    /// walk is worth paying here, once per detail click. Degrades to <see langword="null"/> — never a
    /// 500 — on a manifest that fails the whitelist (mirrors <see cref="ResolveSpecimenFile"/>'s own
    /// "degrade, never throw" posture for a font pack's own parse failure); the admin-ui's own safe
    /// renderer (PLAN T304) still draws whatever it defensively can from the raw <see cref="CatalogEntryResponse.Card"/>
    /// text regardless of whether this count resolved.
    /// </summary>
    static int? ResolveIconCount(string manifestJson) =>
        IconPackDefinitionParser.Validate(Encoding.UTF8.GetBytes(manifestJson)) is IconPackValidationResult.Valid valid
            ? valid.Definition.Icons.Count
            : null;

    /// <summary>
    /// One avatar pack item, projected onto the wire (SPEC F128.1, F128.4, PLAN T292) —
    /// <see cref="CatalogAvatarPackItem.SuggestedPersona"/> gets the SAME shape check
    /// <see cref="ValidateSuggestedPersonaShape"/> already applies to a show entry's own suggestion
    /// (a real catalog slug, ≤64 chars): the field arrives off the SAME untrusted, remote manifest
    /// content every other field on this DTO does, so a malformed value degrades to
    /// <see langword="null"/> rather than reaching the wire (and, eventually, a second catalog fetch)
    /// unchecked. <see cref="ResolveDeclaredAssetFile"/> applies the SAME "never trust a
    /// manifest-only filename" rule to <see cref="CatalogAvatarPackItem.File"/> (review finding,
    /// PLAN T292 — mirrors <see cref="ResolveSpecimenFile"/>'s own cross-reference for a font pack's
    /// upright face): a hostile or simply out-of-sync <c>.avatar.json</c> manifest can name a file
    /// the index's own <c>assets[]</c> never declared, and this projection must not repeat that
    /// unverified name back onto the wire (and, eventually, into an unresolvable
    /// <c>GET /api/catalog/entries/{slug}/assets/{file}</c> call).
    /// </summary>
    static CatalogAvatarItemDto ToAvatarItemDto(CatalogAvatarPackItem item, IReadOnlyList<CatalogAssetRef> assets) =>
        new(item.Name, ResolveDeclaredAssetFile(item.File, assets), ValidateSuggestedPersonaShape(item.SuggestedPersona));

    /// <summary>
    /// Resolves <paramref name="file"/> to itself when — and only when — <paramref name="assets"/>
    /// (this entry's own hash-verified, index-declared asset list) actually carries a file by that
    /// bare name; <see langword="null"/> otherwise (review finding, PLAN T292 — see
    /// <see cref="ToAvatarItemDto"/>'s own remarks). The SAME cross-reference
    /// <see cref="ResolveSpecimenFile"/> already applies to a font pack's own upright face.
    /// </summary>
    static string? ResolveDeclaredAssetFile(string file, IReadOnlyList<CatalogAssetRef> assets) =>
        assets.Any(a => Path.GetFileName(a.Path) == file) ? file : null;

    /// <summary>
    /// Resolves a PERSONA entry's own optional sidecar face to its bare filename (SPEC F128.2, PLAN
    /// T292) — <paramref name="assets"/> is already index-validated (<c>CatalogIndexValidator.TryValidatePersonaAvatarAsset</c>)
    /// to hold AT MOST one element for this kind, so a single lookup (never a cross-reference against
    /// a manifest, unlike <see cref="ResolveSpecimenFile"/>'s own font-kind job — a persona's sidecar
    /// asset names itself, <c>&lt;slug&gt;.avatar.png</c>, with no separate manifest declaration to
    /// match against) is the whole job. <see langword="null"/> when this persona declares no face.
    /// </summary>
    static string? ResolvePersonaAvatarFile(IReadOnlyList<CatalogAssetRef> assets) =>
        assets.Count == 1 ? Path.GetFileName(assets[0].Path) : null;

    /// <summary>
    /// A show entry's OPTIONAL <c>suggestedPersona</c> meta field (SPEC F118.3, PLAN T254) — read
    /// straight off the already hash-verified <c>meta.json</c> content <see cref="ParseMetaFields"/>
    /// already parsed for author/description/samplePatter, degraded to <see langword="null"/> (never
    /// a 400/500) when absent, over-length, or outside the catalog's own slug shape — mirrors
    /// <c>CatalogIndexValidator.TryParseFamily</c>'s own "decorative, never-fails" posture for an
    /// optional index-adjacent field. Shape-checked here (unlike <c>Author</c>/<c>Description</c>,
    /// free text with no further use): genwave-catalog's own <c>show-meta.schema.json</c> pins this
    /// value to its slug vocabulary because it is "read back out at import time and offered as a
    /// candidate slug for a second catalog fetch" (that schema's own remarks) — untrusted input the
    /// same way any other slug-shaped field is, not free text like <c>description</c>. Reuses THIS
    /// controller's own <see cref="SlugFormat"/>/<see cref="MaxSlugLength"/> — the identical rule a
    /// real catalog slug is already held to everywhere else on this controller — rather than a second,
    /// independently-drifting copy of the shape.
    /// </summary>
    static string? ValidateSuggestedPersonaShape(string? suggestedPersona) =>
        suggestedPersona is { Length: > 0 and <= MaxSlugLength } candidate && SlugFormat().IsMatch(candidate)
            ? candidate
            : null;

    /// <summary>
    /// Resolves the bare filename of the pack's UPRIGHT face — the one real face SPEC F104.4's
    /// specimen preview renders — by cross-referencing <paramref name="manifest"/>'s own
    /// <c>files[]</c> <c>role:"upright"</c> entry against <paramref name="assets"/>, this entry's own
    /// hash-verified asset list (T193/T194): the manifest's filename is never trusted alone, only
    /// once it also names something the index itself declared, so the result is guaranteed to be
    /// something <see cref="Asset"/> can actually serve. Degrades to <see langword="null"/> — never
    /// throws — when there is no manifest (parse failure, or a non-font entry), no upright file
    /// declared, or no matching asset.
    /// </summary>
    static string? ResolveSpecimenFile(CatalogFontManifest? manifest, IReadOnlyList<CatalogAssetRef> assets)
    {
        var uprightFile = manifest?.Files.FirstOrDefault(f => f.Role == "upright")?.File;
        return uprightFile is not null && assets.Any(a => Path.GetFileName(a.Path) == uprightFile)
            ? uprightFile
            : null;
    }

    static readonly JsonSerializerOptions MetaJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Parses <see cref="CatalogEntryContent.MetaJson"/> for its shelf display fields. This content
    /// already passed genwave-catalog's own CI schema validation before publish (F89.2) and this
    /// station's own sha256 check before it ever reached here (F90.3) — a parse failure is not an
    /// expected outcome, but degrading to an empty <see cref="CatalogEntryMetaJson"/> (every field
    /// null/absent) rather than a 500 matches this whole controller's own "never an error page for
    /// a shape issue" posture (see this controller's UNREACHABLE remarks).
    /// </summary>
    static CatalogEntryMetaJson ParseMetaFields(string metaJson)
    {
        try
        {
            return JsonSerializer.Deserialize<CatalogEntryMetaJson>(metaJson, MetaJsonOptions) ?? new CatalogEntryMetaJson();
        }
        catch (JsonException)
        {
            return new CatalogEntryMetaJson();
        }
    }

    /// <summary>
    /// Length bound BEFORE the regex (T101 review — parity with <c>PersonaController.Import</c>'s
    /// own <c>MaxCatalogSlugLength</c> guard on its <c>catalogSlug</c> parameter): cheap reject, and
    /// keeps a pathological input away from the regex engine at all. A real catalog entry slug is a
    /// short, human-authored identifier (<see cref="CatalogIndexValidator.SlugSegment"/>'s own
    /// vocabulary), never anywhere near this long.
    /// </summary>
    const int MaxSlugLength = 64;

    /// <summary>
    /// Length bound on the <c>{file}</c> route segment (T194, mirrors <see cref="MaxSlugLength"/>'s
    /// own cheap-reject-before-any-real-work reasoning) — no regex needed alongside it, unlike
    /// <see cref="SlugFormat"/>: <see cref="Asset"/>'s own remarks explain why an equality-only match
    /// against the resolved entry's real asset filenames already closes the shape question. A real
    /// asset filename (<c>CatalogIndexValidator</c>'s own <c>entries/&lt;slug&gt;/&lt;filename&gt;</c>
    /// vocabulary) is a short, human-authored name, never anywhere near this long.
    /// </summary>
    const int MaxAssetFileLength = 128;

    // Composed from CatalogIndexValidator.SlugSegment — the catalog's OWN slug vocabulary (that
    // class parses the identical shape out of an untrusted index.json) — anchored \A/\z, not ^/$ (a
    // SECOND file this exact PersonaController.SlugFormat regression class could recur in: .NET's
    // regex `$` matches immediately before a trailing '\n', not just true end-of-input, so `.../\z`
    // is what actually rejects e.g. "valid-dj\n" over the wire, not `.../$`). [GeneratedRegex]
    // attribute arguments must be compile-time constants, so this recomposes the const text rather
    // than calling CatalogIndexValidator.SlugPattern() directly — the RULE lives once
    // (CatalogIndexValidator.SlugSegment); this is its one other consumer, not a second copy of it.
    [GeneratedRegex(@"\A" + CatalogIndexValidator.SlugSegment + @"\z")]
    private static partial Regex SlugFormat();

    /// <summary>
    /// The F87.2/F61 surface-off idiom (see this controller's own class remarks): a truly bare,
    /// zero-byte 404 with no <c>Content-Type</c> — <see cref="ControllerBase.NotFound()"/> looks bare
    /// at the call site, but <c>[ApiController]</c>'s automatic client-error-to-ProblemDetails
    /// conversion turns it into a JSON body regardless (confirmed empirically: <c>StatusCode(404)</c>
    /// does too — the conversion triggers on the STATUS CODE, not the result type). Setting
    /// <see cref="HttpResponse.StatusCode"/> directly and returning <see cref="EmptyResult"/> bypasses
    /// that filter entirely (empirically verified: 0 bytes, no <c>Content-Type</c> header).
    /// </summary>
    IActionResult DisabledSurfaceResult()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        return new EmptyResult();
    }

    static ProblemDetails BadSlugProblem(string slug) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Invalid slug.",
        Detail = $"\"{slug}\" is not a valid catalog entry slug (lowercase letters, digits, and single hyphens only).",
    };

    static ProblemDetails SlugTooLongProblem(int length) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Invalid slug.",
        Detail = $"slug must be at most {MaxSlugLength} characters (got {length}).",
    };

    static ProblemDetails UnknownEntryProblem(string slug) => new()
    {
        Status = StatusCodes.Status404NotFound,
        Title  = "Not found.",
        Detail = $"No catalog entry with slug \"{slug}\" exists.",
    };

    static ProblemDetails AssetFileTooLongProblem(int length) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Invalid asset file.",
        Detail = $"file must be at most {MaxAssetFileLength} characters (got {length}).",
    };

    static ProblemDetails UnknownAssetProblem(string slug, string file) => new()
    {
        Status = StatusCodes.Status404NotFound,
        Title  = "Not found.",
        Detail = $"No asset \"{file}\" on catalog entry \"{slug}\".",
    };

    static ProblemDetails CatalogUnavailableProblem() => new()
    {
        Status = StatusCodes.Status503ServiceUnavailable,
        Title  = "Persona catalog unavailable.",
        Detail = "The catalog is currently unreachable. Try again shortly.",
    };

    // Deliberately no slug/hash/upstream detail here (F15.7 — mirrors VoicesController's own
    // BadGateway posture): that detail is already in the WARN CatalogProxyService logs server-side.
    static ProblemDetails WithheldProblem(string reason) => new()
    {
        Status = StatusCodes.Status502BadGateway,
        Title  = "Persona catalog entry unavailable.",
        Detail = $"This entry {reason} and was withheld. Try again shortly.",
    };
}
