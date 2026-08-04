namespace GenWave.Host.Api;

/// <summary>
/// Public shape for <c>GET /spectator/api/themes</c> (SPEC F102.10a, STORY-266, PLAN T166): the
/// resolved active theme plus the full catalog the switcher's <c>&lt;select&gt;</c> offers.
/// Deliberately carries nothing beyond <see cref="Active"/>/<see cref="Options"/> — no manifest
/// fonts or tokens (those are <c>theme.css</c>'s job) — so the disclosure contract (SPEC F62.9,
/// STORY-183) stays exactly this shape.
/// </summary>
/// <param name="Active">
/// The slug <see cref="Theming.ThemeCatalog.Resolve"/> resolved for THIS request — the same
/// cookie → <c>Station:Theme</c> → shipped-default cascade <c>theme.css</c> uses, so the
/// pre-selected option always agrees with whichever sheet actually styled the page.
/// </param>
/// <param name="Options">Every theme the catalog carries, in load order.</param>
public sealed record SpectatorThemesResponse(string Active, IReadOnlyList<SpectatorThemeOption> Options);
