namespace GenWave.Host.Options;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// The v1 station-default envelope knobs (SPEC F81.3) within the Station config section — a single
/// 24/7 envelope, no schedule grid. Bound to <c>Station:Envelope</c>; live-editable via the F19
/// settings overlay (joins the allowlist in <see cref="Configuration.StationSettingsAllowlist"/>).
/// Defaults mirror <see cref="GenWave.Abstractions.Playout.SegmentEnvelope.StationDefault"/> exactly
/// (empty genres = every genre; the full [0,1] energy range = no energy constraint) so a fresh
/// install behaves identically to today's envelope-less playout until an operator narrows either
/// knob.
/// </summary>
public sealed class StationEnvelopeOptions
{
    /// <summary>
    /// Raw JSON array of genre names, e.g. <c>["Rock","Jazz"]</c> — the same opaque-string-kind
    /// idiom <c>Tts:Corrections</c> uses (see that options class's own remarks): the station-settings
    /// overlay only expands a stored JSON array into indexed <c>IConfiguration</c> keys for arrays of
    /// scalars it already knows how to bind as a typed list; a raw string here keeps this on the
    /// shape every other free-text leaf already uses. Null, blank, or <c>"[]"</c> means no genre
    /// constraint (SPEC F81.1's "empty Genres = all genres"). Parsing this into
    /// <see cref="GenWave.Abstractions.Playout.SegmentEnvelope.Genres"/> is the eventual live-provider
    /// consumer's job (a later task), not this class's.
    /// </summary>
    public string? Genres { get; init; }

    /// <summary>Lower bound of the admitted energy percentile band (SPEC F80.1, F81.1). Documentation-only
    /// [Range] — <see cref="StationOptionsValidator"/> is the real boot floor, mirroring every other
    /// nested Station:* knob (nested classes are not reached by root <c>ValidateDataAnnotations()</c>).</summary>
    [Range(0.0, 1.0)]
    public double EnergyMin { get; init; } = 0.0;

    /// <summary>Upper bound of the admitted energy percentile band (SPEC F80.1, F81.1). See <see cref="EnergyMin"/>'s
    /// remarks on boot-floor enforcement.</summary>
    [Range(0.0, 1.0)]
    public double EnergyMax { get; init; } = 1.0;
}
