namespace GenWave.Host.Options;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Flat options class for the "Station" configuration section. Validated at startup;
/// a missing or invalid station config prevents the host from starting.
/// </summary>
public sealed class StationOptions
{
    public const string Section = "Station";

    [Required]
    public string Id { get; set; } = string.Empty;

    [Required]
    public string Name { get; set; } = string.Empty;

    [Required, MinLength(1)]
    public string Voice { get; set; } = string.Empty;

    /// <summary>
    /// Enables the public spectator surface (SPEC F62, F61's operating-mode table). Not required —
    /// defaults false (today's behavior unchanged: no public read-only surface). Read live, per
    /// request, via <c>IOptionsMonitor&lt;StationOptions&gt;</c> by
    /// <see cref="GenWave.Host.Api.SurfaceGateMiddleware"/>: when false, every endpoint marked
    /// <see cref="GenWave.Host.Api.SpectatorSurfaceAttribute"/> returns a bare 404 — the surface
    /// does not exist for a deployment that has not opted in.
    /// </summary>
    public bool SpectatorMode { get; set; }

    /// <summary>
    /// The public Icecast stream URL surfaced to spectators (SPEC F62.8). Not required — defaults
    /// to empty, which the spectator "about" panel treats as "no player": an absolute http/https
    /// URL or a root-relative path (e.g. <c>/stream</c>) is legal once the operator sets it.
    /// </summary>
    public string PublicStreamUrl { get; set; } = string.Empty;

    /// <summary>
    /// The base URL feeder annotations resolve per-track artwork/station-icon URLs against (SPEC
    /// F88.4–F88.5, STORY-223, PLAN T85). Not required — defaults to empty, which is the F88.5
    /// contract in full: no push (music or TTS) ever carries a <c>url=</c> annotation, and
    /// <c>genwave.liq</c>'s ICY metadata forwards nothing for that key. Once non-empty, every
    /// music push carries <c>{PublicBaseUrl}/spectator/api/artwork/{token}</c> and every TTS push
    /// carries the reserved station-icon path (see <see cref="GenWave.Host.Engine.ArtworkUrlResolver"/>)
    /// — same URL-safety guard as <see cref="PublicStreamUrl"/> (<c>SettingValidator.IsSafePublicStreamUrl</c>),
    /// since both are operator-supplied URLs an eventual client fetches.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>The set of library ids this station is permitted to draw from. Must be non-empty.</summary>
    public StationScopeOptions Scope { get; set; } = new();

    /// <summary>
    /// The set of library ids used for safe-rotation fallback. Must be non-empty and contain
    /// only positive ids. The deployment default is library 1, provided via
    /// <c>appsettings.json</c> so that IConfiguration binding starts from an empty list and
    /// overrides replace rather than append. Bound to <c>Station:SafeScope:LibraryIds</c>.
    /// </summary>
    public StationScopeOptions SafeScope { get; set; } = new();

    /// <summary>Controls how often voice segments are woven into the broadcast.</summary>
    public StationCadenceOptions Cadence { get; set; } = new();

    /// <summary>
    /// Safe-loop authoring config (SPEC F27) — generation-time inputs, not live-editable
    /// (F27.10). Bound to <c>Station:Safe</c>.
    /// </summary>
    public StationSafeOptions Safe { get; set; } = new();

    /// <summary>Rotation anti-repeat/artist-separation knobs (SPEC F41.6). Bound to <c>Station:Rotation</c>.</summary>
    public StationRotationOptions Rotation { get; set; } = new();

    /// <summary>Boundary-aware selection bias knobs (SPEC F74.3, STORY-198). Bound to <c>Station:BoundaryBias</c>.</summary>
    public StationBoundaryBiasOptions BoundaryBias { get; set; } = new();

    /// <summary>Station-default 24/7 segment envelope knobs (SPEC F81.3, STORY-212). Bound to <c>Station:Envelope</c>.</summary>
    public StationEnvelopeOptions Envelope { get; set; } = new();

    /// <summary>The three live-editable listener-request knobs (SPEC F87.2, F87.6, STORY-224). Bound to <c>Station:Requests</c>.</summary>
    public StationRequestsOptions Requests { get; set; } = new();

    /// <summary>The Library Gardener's one live-editable knob (SPEC F150.2, F155.1, STORY-380,
    /// STORY-369, PLAN T357/T366). Bound to <c>Station:Thumbs</c>.</summary>
    public StationThumbsOptions Thumbs { get; set; } = new();

    /// <summary>
    /// Audience posture (SPEC F95.1, STORY-250, PLAN T111): <c>"everyone"</c> (default,
    /// fail-closed) or <c>"mature"</c> (case-insensitive — mirrors <c>Llm:DegradationPin</c>'s
    /// own guard, <see cref="GenWave.Host.Configuration.SettingValidator"/>). <c>everyone</c> is
    /// the safe default a fresh station boots into: nothing stamped
    /// <c>explicit</c> may enter a candidate pool until an operator deliberately opts in to
    /// <c>mature</c>. No consumer reads this yet — T114 wires the shared pool predicate
    /// (rotation, request matcher, boundary bias) this drives (SPEC F95.4).
    /// </summary>
    public string Audience { get; set; } = "everyone";

    /// <summary>
    /// The station's IANA timezone id (gh-#117), e.g. <c>America/Edmonton</c> — drives every
    /// "station-local now" the LLM prompt path sees (the clock line, <c>SegmentRequest.LocalNow</c>)
    /// via <see cref="GenWave.Core.Abstractions.IStationClockProvider"/>. Not required — defaults to
    /// empty, which is the honest "use the container's own clock" state (pre-gh-#117 behavior,
    /// byte-identical). Read live, per call, through
    /// <see cref="OptionsMonitorStationClockProvider"/>; an unresolvable value that arrives via the
    /// environment (the settings API rejects one — <c>SettingValidator</c>) also falls back to the
    /// container's clock rather than faulting the patter path.
    /// </summary>
    public string Timezone { get; set; } = string.Empty;

    /// <summary>
    /// The station's chosen theme slug (SPEC F102.5, F102.14, STORY-265, PLAN T163/T164) — the
    /// second-from-the-floor rung of theme resolution (visitor cookie → this → shipped default).
    /// Not required — defaults to empty, the honest "no station value set anywhere" case
    /// (<c>Station:Theme</c> is deliberately unseeded in <c>appsettings.json</c>; see
    /// <c>StationSettingsAllowlist</c>'s own remarks), which resolution treats identically to an
    /// unresolvable slug: fall through to <see cref="GenWave.Host.Theming.ThemeCatalog.ShippedDefaultSlug"/>.
    /// Read live, per request, through <c>IOptionsMonitor&lt;StationOptions&gt;</c> by both theme
    /// endpoints, so a <c>PUT /api/settings</c> here reaches the very next request with no api
    /// restart — the DB overlay provider is registered after env/appsettings (see
    /// <c>StationSettingsHostingExtensions</c>), so this single value already reflects whichever of
    /// "saved settings row" or "env default" currently wins; resolution itself never has to choose
    /// between the two.
    /// </summary>
    public string Theme { get; set; } = string.Empty;

    /// <summary>
    /// The station's chosen icon-pack slug (SPEC F130.4, STORY-337, PLAN T303) — activation for the
    /// admin chrome's third swappable layer. Not required — defaults to empty, the honest "house
    /// icons" state (<c>Station:IconPack</c> is deliberately unseeded in <c>appsettings.json</c>, the
    /// same <see cref="Theme"/> precedent immediately above): the F130.3 renderer resolves an empty
    /// value, and any value naming no currently-installed pack (F130.5's fail-open uninstall — a
    /// dangling slug left behind by a <c>DELETE</c> that never touches this setting), identically to
    /// house icons, never an error. Read live, per request, through
    /// <c>IOptionsMonitor&lt;StationOptions&gt;</c> by <c>IconPackController.Active</c>, so a
    /// <c>PUT /api/settings</c> here reaches the very next request with no api restart.
    /// </summary>
    public string IconPack { get; set; } = string.Empty;

    /// <summary>The station's broadcast location (SPEC F108.1, F108.3, PLAN T226) — read live
    /// through <see cref="OptionsMonitorStationLocationProvider"/> by
    /// <c>GenWave.Context.Weather.WeatherContextProvider</c>. Bound to <c>Station:Location</c>.
    /// </summary>
    public StationLocationOptions Location { get; set; } = new();

    /// <summary>Clock-anchored imaging knobs (SPEC F110.1/F110.3, gh-#381, PLAN T226) — see
    /// <see cref="StationImagingOptions"/>'s own remarks for why this task adds the binding with no
    /// consumer yet. Bound to <c>Station:Imaging</c>.</summary>
    public StationImagingOptions Imaging { get; set; } = new();

    /// <summary>Show-domain knobs (SPEC F116.3, STORY-308, PLAN T249) — see
    /// <see cref="StationShowsOptions"/>'s own remarks. Bound to <c>Station:Shows</c>.</summary>
    public StationShowsOptions Shows { get; set; } = new();
}
