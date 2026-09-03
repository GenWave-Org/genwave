using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// The default <see cref="IAdSpotVend"/> binding: always answers <see langword="null"/> — mirrors
/// <see cref="NoOpStationImagingSettingsProvider"/>'s own "shared instance for non-DI construction"
/// idiom one seam over (SPEC F158.2). A null answer is always a legal one for
/// <see cref="IAdSpotVend.GetNextSpotAsync"/> (an empty pipeline, F158.3), so a composition that
/// never wires <c>GenWave.Ads</c>' real <c>AdSpotPipeline</c> (every pre-T397 construction site,
/// including every unit test) degrades to "no ad ever airs" rather than failing to compose.
/// </summary>
public sealed class NoOpAdSpotVend : IAdSpotVend
{
    /// <summary>Shared instance for non-DI construction (Core types, tests).</summary>
    public static readonly NoOpAdSpotVend Instance = new();

    /// <inheritdoc/>
    public Task<MediaItem?> GetNextSpotAsync(CancellationToken ct) => Task.FromResult<MediaItem?>(null);
}
