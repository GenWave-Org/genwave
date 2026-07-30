using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;

namespace GenWave.Tts;

/// <summary>
/// The Tts-side half of the <see cref="ICopyBoundsProvider"/> seam (gh-#253): adapts
/// <see cref="IOptionsMonitor{TOptions}"/> over <see cref="LlmOptions"/> so a live
/// <c>PUT /api/settings</c> edit to <c>Llm:MaxCopyChars</c> reaches the patter-duration estimator's
/// cold tier without a process restart. Mirrors <c>OptionsMonitorBoundaryBiasProvider</c>
/// (GenWave.Host) one seam over — same read-fresh-per-call shape, no caching.
/// </summary>
sealed class OptionsMonitorCopyBoundsProvider(IOptionsMonitor<LlmOptions> llmMonitor) : ICopyBoundsProvider
{
    /// <inheritdoc/>
    public int MaxCopyChars => llmMonitor.CurrentValue.MaxCopyChars;
}
