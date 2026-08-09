namespace GenWave.Context;

/// <summary>
/// Opt-in capability (F4 fix, T226 review) — the <see cref="ISelfGatingContextProvider"/> precedent
/// shape applied to a second, independent concern: a provider that needs <see cref="ContextPipeline"/>
/// to enforce a MINIMUM <see cref="Core.Domain.ContextProviderSettings.SegmentCadenceMinutes"/>
/// regardless of what an operator configured — e.g. <see cref="Weather.WeatherContextProvider"/>'s
/// SPEC F108.2 "twice an hour, at most" floor. A provider that does not implement this interface is
/// floored only at <see cref="ContextPipeline"/>'s own existing one-minute floor-divide clamp (see
/// that class's own remarks) — today's unchanged behavior for every provider that declares no
/// cadence floor of its own.
///
/// <para>
/// Re-homes the floor OUT of <c>GenWave.Host.Options.ConfigurationContextSettingsProvider</c> (which
/// used to special-case <c>key == "weather"</c> by string — the exact kind of provider-specific
/// knowledge that class's own remarks say a generic, "any future kind" settings shim must never
/// carry) and INTO the one provider that actually owns the number, consulted by
/// <see cref="ContextPipeline"/> itself — the same place <see cref="ISelfGatingContextProvider"/> is
/// already consulted, in <see cref="ContextPipeline.TickAsync"/>, wherever
/// <see cref="Core.Domain.ContextProviderSettings.SegmentCadenceMinutes"/> is actually consumed. This
/// is the STRUCTURAL backstop, not the operator-facing rule: <c>GenWave.Host.Configuration.SettingValidator</c>'s
/// write-time 30–1440 range (SPEC F108.2) stays what an operator sees rejected at
/// <c>PUT /api/settings</c> time; this interface is what still holds for a value that reaches
/// <see cref="ContextPipeline"/> some OTHER way (an appsettings.json/env override, which never passes
/// through that validator at all).
/// </para>
/// </summary>
interface ICadenceFlooredContextProvider
{
    /// <summary>The minimum SegmentCadenceMinutes <see cref="ContextPipeline"/> enforces for this
    /// provider — evaluated fresh on every read (cheap, synchronous, no I/O), mirroring
    /// <see cref="ISelfGatingContextProvider.IsAvailable"/>'s own discipline.</summary>
    int MinimumSegmentCadenceMinutes { get; }
}
