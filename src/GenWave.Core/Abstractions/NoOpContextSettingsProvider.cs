namespace GenWave.Core.Abstractions;

using GenWave.Core.Domain;

/// <summary>
/// The default <see cref="IContextSettingsProvider"/> binding: every provider reads back
/// disabled/zero-cadence/no-explicit-persona settings — mirrors
/// <see cref="StationDefaultEnvelopeProvider"/>'s "shared instance for non-DI construction" idiom one
/// seam over (SPEC F107.2). Exists so a caller constructed without a live options stack (a unit test,
/// or a host that has not yet wired the T226 <c>IOptionsMonitor</c>-backed implementation) never has
/// to null-check.
///
/// <para>
/// A <c>GenWave.Orchestration.SpeechDeferralKind.Context</c> deferral is never enqueued in the first
/// place without a real, enabled provider behind it (SPEC F107.6 — the pipeline itself gates fetch/delivery on
/// <see cref="ContextProviderSettings.Enabled"/>), so this binding's own <c>Enabled: false</c> answer
/// is never itself the reason a context segment fails to air. The one thing a live caller (the
/// Orchestrator's drain arm, F107.7) actually reads off this binding is
/// <see cref="ContextProviderSettings.PersonaId"/> — <see langword="null"/> here degrades to the
/// on-air DJ, the same fallback an explicitly-configured zero would.
/// </para>
/// </summary>
public sealed class NoOpContextSettingsProvider : IContextSettingsProvider
{
    /// <summary>Shared instance for non-DI construction (Core types, tests).</summary>
    public static readonly NoOpContextSettingsProvider Instance = new();

    static readonly ContextProviderSettings Disabled = new(
        Enabled: false, SegmentCadenceMinutes: 0, PatterCadenceMinutes: 0, PersonaId: null);

    /// <inheritdoc/>
    public ContextProviderSettings For(string key) => Disabled;
}
