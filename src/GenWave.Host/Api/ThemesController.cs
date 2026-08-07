using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Host.Theming;

namespace GenWave.Host.Api;

/// <summary>
/// <c>GET /api/themes</c> — every resolvable station theme (shipped ∪ imported ∪ saved), full
/// manifest (SPEC F104.11, STORY-286, PLAN T206): the v2 editor's base-theme picker needs a COMPLETE
/// manifest per candidate (palette tokens + the theme's own current fonts, to seed the picker's
/// default role selections), not just the slug/label pair <c>Station:Theme</c>'s settings choices
/// already carry (<see cref="Configuration.StationSettingsAllowlist.ThemeChoices"/>). That existing
/// projection exists for a DIFFERENT job — which theme is CURRENTLY active — and
/// <c>persona-catalog/page.tsx</c>'s own remarks already weighed and declined widening it (or adding a
/// sibling route) for a narrower provenance-only need; this is the first caller that genuinely needs
/// the manifest bytes themselves, so it earns its own thin GET rather than a third shape bolted onto
/// that settings row.
/// </summary>
/// <remarks>
/// <b>A read-only sibling to <see cref="ThemesImportController"/>/<see cref="ThemesSaveAsOwnController"/>,
/// deliberately its own class.</b> This action never touches
/// <see cref="GenWave.Core.Abstractions.IThemeStore"/> at all, only the already-loaded
/// <see cref="ThemeCatalog"/> singleton (the SAME snapshot <c>GET /api/theme.css</c> resolves
/// against) — true before and after <see cref="ThemesSaveAsOwnController"/> joined
/// <see cref="ThemesImportController"/> as <c>station.theme</c>'s second write path (PLAN T207): both
/// writers share the exact same font-law/ceiling/shipped-slug gate (see
/// <see cref="ThemesSaveAsOwnController"/>'s own remarks), so a row this catalog ever serves still
/// satisfies it regardless of which route wrote it. Naming this controller after the RESOURCE, not the
/// verb — mirroring <see cref="FontPackController"/>'s own GET-list + POST-install split under one
/// resource-named class — would put this read on either writer instead, but each writer's own name
/// is deliberately narrow to its own verb; bolting an unrelated read onto either would blur that
/// promise for zero benefit.
///
/// <para>
/// <b>Returns <see cref="ThemeManifest"/> verbatim — no parallel DTO.</b> The manifest format IS the
/// interchange format (<see cref="ThemeManifestSerializer"/>'s own remarks), already trusted across
/// the wire in both directions (<see cref="ThemesImportController.Import"/> accepts it as a POST
/// body, <see cref="ThemePreviewController.Preview"/> too). ASP.NET's default MVC JSON options
/// already camelCase every property the same way <see cref="ThemeManifestSerializer.Options"/> does,
/// so this controller's plain <c>Ok(...)</c> produces the SAME wire shape a hand-rolled DTO mirroring
/// <see cref="ThemeFonts"/>/<see cref="ThemeFontFace"/>/<see cref="ThemeFontAsset"/>/<see cref="ThemeModes"/>
/// field-for-field would, at the cost of four types that would only ever drift from their own
/// originals. Nothing here is more sensitive than what every sibling theme route already discloses
/// to an authenticated operator — same class-level <see cref="AdminSurfaceAttribute"/>+
/// <see cref="AuthorizationPolicies.Settings"/> pairing as the rest of this prefix.
/// </para>
///
/// <para>
/// <b>Never gated by the Community Catalog kill switch</b> (SPEC F104.8's "station-local inventory
/// outlives the catalog" posture, <see cref="FontPackController.List"/>'s own precedent). This action
/// depends on nothing but <see cref="ThemeCatalog"/> — never
/// <see cref="Catalog.CommunityCatalogAccessor"/>/<see cref="Catalog.CatalogProxyService"/> — so there
/// is no catalog-reachability axis for it to even vary on. Previously documented but not Fact-pinned
/// (T206 review finding F1); now pinned by name, alongside <see cref="FontPackController.Assignable"/>'s
/// own identical posture, in <c>Story286_EditorComposesTheRemix.cs</c>'s own
/// <c>ScenarioTheCatalogKillSwitchDoesNotGateTheEditorReads</c>.
/// </para>
/// </remarks>
[ApiController]
[Route("api/themes")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Settings)]
public sealed class ThemesController(ThemeCatalog themeCatalog) : ControllerBase
{
    /// <summary>
    /// GET /api/themes — every theme <see cref="ThemeCatalog.All"/> currently resolves (shipped ∪
    /// owner-imported ∪ owner-saved), in load order — the exact set SPEC F104.11's "any resolvable
    /// theme — shipped, imported, or saved" names as the editor's base-theme picker's own candidate
    /// list.
    /// </summary>
    [HttpGet]
    public IActionResult Index() => Ok(themeCatalog.All);
}
