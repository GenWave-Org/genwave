namespace GenWave.Tts;

using GenWave.Core.Abstractions;
using Microsoft.Extensions.Options;

/// <summary>
/// The render budget of the engine speaking right now (SPEC F219.6): every render-chain reader
/// (BreakRenderer via the planner, crosstalk, safe content, preview) asks this seam per call, so a
/// saved <c>Tts:Engine</c> or a live <c>Tts:RenderBudgetSeconds</c> edit (which moves the seeded
/// kokoro/piper entries) applies to the next render. When the stored engine is not registered it
/// falls back to <c>Tts:RenderBudgetSeconds</c> instead of throwing. Nothing is cached here.
/// </summary>
public sealed class ActiveEngineRenderBudgetProvider(IActiveVoiceEngine active, IOptionsMonitor<TtsOptions> options)
    : IRenderBudgetProvider
{
    public TimeSpan Current =>
        TimeSpan.FromSeconds(active.Current?.RenderBudgetSeconds ?? options.CurrentValue.RenderBudgetSeconds);
}
