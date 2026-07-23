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

    /// <summary>Active DJ persona pointer (SPEC F35.2, F36.2). Bound to <c>Station:Persona</c>.</summary>
    public StationPersonaOptions Persona { get; set; } = new();

    /// <summary>Rotation anti-repeat/artist-separation knobs (SPEC F41.6). Bound to <c>Station:Rotation</c>.</summary>
    public StationRotationOptions Rotation { get; set; } = new();

    /// <summary>Boundary-aware selection bias knobs (SPEC F74.3, STORY-198). Bound to <c>Station:BoundaryBias</c>.</summary>
    public StationBoundaryBiasOptions BoundaryBias { get; set; } = new();

    /// <summary>Station-default 24/7 segment envelope knobs (SPEC F81.3, STORY-212). Bound to <c>Station:Envelope</c>.</summary>
    public StationEnvelopeOptions Envelope { get; set; } = new();
}
