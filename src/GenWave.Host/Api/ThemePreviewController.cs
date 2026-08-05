using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Host.Theming;

namespace GenWave.Host.Api;

/// <summary>
/// <c>POST /api/themes/preview</c> — the theme catalog's detail live-preview (SPEC F103.5, STORY-274,
/// PLAN T186): composes a posted theme manifest into CSS SCOPED under
/// <see cref="ContainerSelector"/>, never <c>:root</c>, so a browser can render an honest "what
/// you'll get" mock before importing anything.
///
/// <para>
/// <b>Delivery choice: POST-a-manifest, not GET-a-catalog-slug (PLAN T186's own "state your choice"
/// call).</b> The Admin UI already holds the manifest text in hand by the time a theme's detail
/// opens — <c>GET /api/catalog/entries/{slug}</c> (<see cref="CatalogController.Entry"/>) already
/// fetched and sha256-verified it (SPEC F90.3) for the SAME review-before-you-act surface
/// <see cref="ThemesImportController.Import"/> itself posts back. This route reuses those exact
/// bytes rather than re-fetching the catalog a second time: no <see cref="Catalog.CatalogProxyService"/>
/// dependency here at all, no <c>catalogSlug</c> to validate, nothing stored. It mirrors
/// <see cref="TtsPreviewController"/>'s own shape — a transient POST-in, transform-out computation,
/// never persisted — more than it mirrors <see cref="CatalogController"/>'s GET-by-slug reads, because
/// composing is exactly that: a pure transform over content the caller already possesses, not a fetch.
/// Composition itself still lives in exactly ONE place regardless of the delivery shape:
/// <see cref="ThemeCssComposer"/>, the SAME composer <c>GET /spectator/theme.css</c>/<c>GET /api/theme.css</c>
/// serve from — this route asks it for the scoped overload, never a forked TypeScript re-implementation.
/// </para>
///
/// <para>
/// <b>Reuses the F79/F103.6 shell for the parts that are genuinely the same control.</b> Same size cap
/// (<see cref="BoundedImportBodyReader"/>, both the running-total read and the
/// <see cref="RequestSizeLimitAttribute"/> belt-and-braces copy) and the same
/// deserialization-IS-validation posture as <see cref="ThemesImportController.Import"/>: a
/// <see cref="ThemeManifestException"/> from <see cref="ThemeManifestParser.Parse"/> maps to 400, never
/// an unhandled 500 — both routes' 413/400 bodies come from the shared <see cref="ImportProblems"/>
/// factories, not a copy each (review finding). Deliberately NOT reused: the schema-major gate
/// (<see cref="ThemesImportController"/>'s own <c>ExtractSchemaVersion</c>/<c>CurrentSchemaVersion</c>)
/// and the shipped-slug/route-slug machinery — nothing here is ever stored, so there is no forward
/// compatibility contract to protect and no slug to normalize; a manifest that merely PARSES is enough
/// to preview honestly, exactly what <see cref="ThemeCssComposer"/> needs.
/// </para>
///
/// <para>
/// <b><see cref="ContainerSelector"/> is server-authored, never client input.</b> The Admin UI's own
/// preview markup wraps its mock in an element carrying this exact class (a documented, mirrored
/// constant — the same "one literal, two files" idiom <c>ThemeCatalog.CookieName</c> already uses
/// against <c>admin-ui/lib/theme.ts</c>). Accepting a caller-supplied selector instead would turn this
/// route into a second CSS-injection surface for zero benefit — there is exactly one preview container
/// in the whole Admin UI, so a fixed constant is the correct shape, not a missing parameter.
/// </para>
/// </summary>
[ApiController]
[Route("api/themes")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Settings)]
public sealed class ThemePreviewController : ControllerBase
{
    const string CssContentType = "text/css; charset=utf-8";

    /// <summary>
    /// The Admin UI's theme detail preview container's own class name (mirrored in
    /// <c>admin-ui/app/(authed)/persona-catalog/theme-preview.ts</c>) — see this controller's own
    /// remarks ("<see cref="ContainerSelector"/> is server-authored") for why this is a fixed constant
    /// rather than a request parameter.
    /// </summary>
    public const string ContainerSelector = ".theme-live-preview";

    /// <summary>
    /// POST /api/themes/preview — see this controller's own remarks for the full delivery-shape
    /// reasoning. Body is the raw theme manifest JSON text (the SAME bytes the caller already fetched
    /// and is about to review); response is the scoped preview CSS as <c>text/css</c>.
    /// </summary>
    [HttpPost("preview")]
    [Consumes("application/json")]
    [RequestSizeLimit(BoundedImportBodyReader.MaxImportBytes)]
    public async Task<IActionResult> Preview(CancellationToken ct)
    {
        var (json, oversized) = await BoundedImportBodyReader.ReadBoundedBodyAsync(
            Request, BoundedImportBodyReader.MaxImportBytes, ct);
        if (oversized)
            return StatusCode(StatusCodes.Status413PayloadTooLarge, ImportProblems.Oversized());

        ThemeManifest manifest;
        try
        {
            manifest = ThemeManifestParser.Parse(new ThemeManifestSource("theme-preview", json));
        }
        catch (ThemeManifestException ex)
        {
            return BadRequest(ImportProblems.MalformedManifest(ex.Message));
        }

        var css = ThemeCssComposer.ComposeScoped(manifest, ContainerSelector);
        return Content(css, CssContentType);
    }
}
