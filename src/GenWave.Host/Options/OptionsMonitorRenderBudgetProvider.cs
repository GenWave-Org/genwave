using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Tts;

namespace GenWave.Host.Options;

/// <summary>
/// The Host-side half of the <see cref="IRenderBudgetProvider"/> seam (SPEC F44.2, closes gitea-#197):
/// wraps <see cref="IOptionsMonitor{TOptions}"/> so the Orchestrator reads the SAME live value
/// <c>PUT /api/settings</c> writes (mirrors <see cref="OptionsMonitorCadenceProvider"/>).
///
/// Replaces <c>Program.cs</c>'s previous one-time <c>TimeSpan.FromSeconds(ttsOpts.RenderBudgetSeconds)</c>
/// snapshot, taken once at composition-root time and handed to the Orchestrator as a frozen
/// <see cref="TimeSpan"/> for the life of the process — the exact boot-frozen-consumer bug this
/// provider retires. Nothing is cached here — <see cref="IOptionsMonitor{T}.CurrentValue"/> already
/// is the cache.
/// </summary>
sealed class OptionsMonitorRenderBudgetProvider(IOptionsMonitor<TtsOptions> ttsMonitor)
    : IRenderBudgetProvider
{
    public TimeSpan Current => TimeSpan.FromSeconds(ttsMonitor.CurrentValue.RenderBudgetSeconds);
}
