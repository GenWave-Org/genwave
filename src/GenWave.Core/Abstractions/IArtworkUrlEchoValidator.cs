using GenWave.Core.Domain;
using GenWave.Core.Playout;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SPEC F88.5 fail-closed extension (PLAN T125 review F2) — the thin accessor seam between
/// <see cref="PlayoutFeeder"/> (which cannot see the Host's <c>IOptionsMonitor&lt;StationOptions&gt;</c>)
/// and the Host's configured <c>Station:PublicBaseUrl</c>, consulted at the ONE place an
/// engine-initiated advance's output metadata echoes a <c>url</c> field back
/// (<see cref="EngineMetadata.ExtractAnnotations"/>).
///
/// <para>
/// That echo is trustworthy for a track this feeder itself pushed — genwave.liq re-exports exactly
/// the token url the push stamped — but NOT for a play the feeder never pushed (the safe rotation
/// drawing straight from a media file): genwave.liq's <c>settings.encoder.metadata.export</c>
/// allow-list forwards whatever <c>url</c>-shaped tag the FILE ITSELF carries too (a Vorbis
/// <c>URL=</c> comment, an ID3 <c>W...</c>/<c>WXXX</c> frame) — indistinguishable, at the
/// output-metadata layer, from our own stamped annotation. Trusting it unconditionally would let a
/// hostile file tag plant an arbitrary third-party URL the spectator page (PLAN T126) renders as an
/// <c>&lt;img src&gt;</c> to public visitors. <see cref="IsTrusted"/> is the ONE gate that tells the
/// two cases apart, so <see cref="PlayoutFeeder"/> itself never needs to know
/// <c>Station:PublicBaseUrl</c>'s shape or value — only whether a given url matches it.
/// </para>
///
/// <para>
/// Implementations MUST re-evaluate <see cref="IsTrusted"/> fresh on every call — never cache the
/// configured base — mirroring every other live-config seam in this family
/// (<see cref="IRotationSettingsProvider"/>'s own remarks): a live <c>Station:PublicBaseUrl</c> edit
/// must be honored by the very next tick, no api restart.
/// </para>
/// </summary>
public interface IArtworkUrlEchoValidator
{
    /// <summary>
    /// True when <paramref name="url"/> is exactly the shape THIS station's own pushes stamp — starts
    /// with the CURRENTLY configured <c>Station:PublicBaseUrl</c> (trailing-slash-trimmed) followed by
    /// the reserved artwork path prefix (see the Host's <c>ArtworkUrlResolver</c>, this seam's real
    /// implementation, for the exact composition). Always false when <c>PublicBaseUrl</c> is empty
    /// (SPEC F88.5's own "no base configured" rule, extended to the echo side) — an empty base can
    /// never be a legitimate prefix of anything a hostile tag could forge.
    /// </summary>
    bool IsTrusted(string url);
}
