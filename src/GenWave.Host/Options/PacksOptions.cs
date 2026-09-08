using System.ComponentModel.DataAnnotations;

namespace GenWave.Host.Options;

/// <summary>
/// Filesystem roots and byte ceilings for jingle packs and voice packs (SPEC F164–F170,
/// STORY-395..405, PLAN T417). Bound from the <c>Packs</c> section — env/compose-only,
/// deliberately absent from <see cref="GenWave.Host.Configuration.StationSettingsAllowlist"/>: these
/// are deployment topology (where the shared volumes are mounted, how big an upload/preview may be),
/// the same class of setting as <see cref="ArtworkOptions"/>/<see cref="RequestsOptions"/>, not an
/// operator-tunable knob with a live PUT surface.
/// <see cref="JingleRoot"/>/<see cref="VoicesRoot"/> default to the mount points
/// <c>compose.yaml</c> already stacks for the jingle-pack and voice-pack volumes (`/authored/jingle-packs`,
/// `/voices` — the "Shared voice files" volume this file's own DEPLOYMENT.md section documents);
/// <see cref="PreviewMaxBytes"/>/<see cref="JingleAssetMaxBytes"/> bound a single preview clip and a
/// single jingle asset respectively, install-time ceilings enforced by the pack-install routes
/// (PLAN T413/T414), not this class itself.
/// </summary>
public sealed class PacksOptions
{
    public const string SectionName = "Packs";

    /// <summary>Root directory for installed jingle-pack assets (SPEC F165). Mirrors the
    /// <c>/authored/jingle-packs</c> subdirectory of the `authored` volume.</summary>
    [Required]
    public string JingleRoot { get; init; } = "/authored/jingle-packs";

    /// <summary>Root directory for installed voice-pack `.pt` files (SPEC F164, F166) — the shared
    /// `voices` named volume Kokoro reads from read-only and rescans on every request.</summary>
    [Required]
    public string VoicesRoot { get; init; } = "/voices";

    /// <summary>Maximum accepted size, in bytes, of a single voice-pack preview clip. Default 153600
    /// (150 KiB). <c>int</c>, not <c>long</c> — the same byte-cap precedent as
    /// <see cref="GenWave.Host.Catalog.CatalogProxyService.MaxIndexBytes"/>; both defaults here sit
    /// comfortably under <see cref="int.MaxValue"/>.</summary>
    [Range(1, int.MaxValue)]
    public int PreviewMaxBytes { get; init; } = 153_600;

    /// <summary>Maximum accepted size, in bytes, of a single jingle-pack asset file. Default 5242880
    /// (5 MiB).</summary>
    [Range(1, int.MaxValue)]
    public int JingleAssetMaxBytes { get; init; } = 5_242_880;
}
