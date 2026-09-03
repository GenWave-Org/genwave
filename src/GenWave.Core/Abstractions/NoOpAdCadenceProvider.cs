namespace GenWave.Core.Abstractions;

/// <summary>
/// The default <see cref="IAdCadenceProvider"/> binding: always answers 0 (disabled) — mirrors
/// <see cref="NoOpStationImagingSettingsProvider"/>'s own "shared instance for non-DI construction"
/// idiom one seam over (SPEC F158.3). Zero is the correct fail-closed answer, not merely a
/// placeholder: a composition that never wires the Host's real
/// <c>OptionsMonitorAdCadenceProvider</c> must never trigger an ad break nobody configured.
/// </summary>
public sealed class NoOpAdCadenceProvider : IAdCadenceProvider
{
    /// <summary>Shared instance for non-DI construction (Core types, tests).</summary>
    public static readonly NoOpAdCadenceProvider Instance = new();

    /// <inheritdoc/>
    public int Current => 0;
}
