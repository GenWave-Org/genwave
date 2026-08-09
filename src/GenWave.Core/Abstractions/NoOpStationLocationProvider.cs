namespace GenWave.Core.Abstractions;

using GenWave.Core.Domain;

/// <summary>
/// The default <see cref="IStationLocationProvider"/> binding: every call reads back a blank
/// location — mirrors <see cref="NoOpContextSettingsProvider"/>'s own "shared instance for non-DI
/// construction" idiom one seam over (SPEC F108.1). A blank <see cref="StationLocation.Latitude"/>/
/// <see cref="StationLocation.Longitude"/> is a legal, fail-closed input to any coordinate-consuming
/// provider (never a caller-visible fault), so this binding never has to be swapped in just to keep
/// a provider constructible — it is the correct answer for "no location configured yet", not merely
/// a placeholder for one.
/// </summary>
public sealed class NoOpStationLocationProvider : IStationLocationProvider
{
    /// <summary>Shared instance for non-DI construction (Core types, tests).</summary>
    public static readonly NoOpStationLocationProvider Instance = new();

    static readonly StationLocation Blank = new(string.Empty, string.Empty, string.Empty);

    /// <inheritdoc/>
    public StationLocation Current => Blank;
}
