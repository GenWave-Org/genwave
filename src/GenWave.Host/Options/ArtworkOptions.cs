using System.ComponentModel.DataAnnotations;

namespace GenWave.Host.Options;

/// <summary>
/// Wiring for <see cref="Api.ArtworkService"/>'s disk cache (SPEC F88.3, STORY-222, PLAN T84).
/// Bound from the <c>Artwork</c> config section — env/compose-only (deliberately absent from
/// <see cref="GenWave.Host.Configuration.StationSettingsAllowlist"/>): the cache directory is
/// deployment topology, the same class of setting as <c>Tts:CacheRoot</c>.
/// <para>
/// <see cref="CacheDir"/> defaults to a subdirectory of the <c>tts</c> named volume
/// (<c>compose.yaml</c>'s <c>tts:/tts</c> read-write mount) rather than a brand new named
/// volume: of the api container's writable, persisted mounts, <c>/media</c> is read-only, and
/// <c>authored</c> is semantically owned by the safe-loop authoring pipeline (F27) — <c>/tts</c>
/// is the one general-purpose writable artifact area already reachable without a compose change.
/// <c>TtsSegmentSource</c> itself only ever writes under <c>{CacheRoot}/{stationId}[/blurbs]</c>
/// (a per-station subdirectory), so a sibling <c>artwork/</c> directory never collides with it.
/// </para>
/// </summary>
public sealed class ArtworkOptions
{
    public const string Section = "Artwork";

    [Required]
    public string CacheDir { get; set; } = "/tts/artwork";
}
