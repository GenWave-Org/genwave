namespace GenWave.Core.Abstractions;

using GenWave.Core.Domain;

/// <summary>
/// The default <see cref="IStationImagingSettingsProvider"/> binding: both knobs read back
/// <see langword="false"/> — mirrors <see cref="NoOpStationLocationProvider"/>'s own "shared instance
/// for non-DI construction" idiom one seam over (SPEC F110.1/F110.3). Both-false is the correct
/// fail-closed answer, not merely a placeholder: PLAN T230's own acceptance bar is "defaults-false
/// ⇒ byte-identical sound," and this binding is exactly what makes that true for any caller that
/// never wires the Host's real <c>OptionsMonitorStationImagingProvider</c>.
/// </summary>
public sealed class NoOpStationImagingSettingsProvider : IStationImagingSettingsProvider
{
    /// <summary>Shared instance for non-DI construction (Core types, tests).</summary>
    public static readonly NoOpStationImagingSettingsProvider Instance = new();

    static readonly StationImagingSettings Off = new(ClockAnchoredIdents: false, TimeAnnouncements: false);

    /// <inheritdoc/>
    public StationImagingSettings Current => Off;
}
