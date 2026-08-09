namespace GenWave.Core.Abstractions;

using GenWave.Core.Domain;

/// <summary>
/// SPEC F107.2 — the thin accessor seam between <c>GenWave.Context</c> (which references only
/// <c>GenWave.Core</c>/<c>GenWave.Abstractions</c> and cannot see the Host's
/// <c>IOptionsMonitor&lt;T&gt;</c> directly) and the Host's live per-provider configuration. Mirrors
/// <see cref="ICadenceProvider"/>/<see cref="IRenderBudgetProvider"/> one seam over: each registered
/// <see cref="IContextProvider"/>'s settings live at <c>Context:{Key}:*</c> (<see cref="IContextProvider.Key"/>'s
/// own doc), advertised <c>Live</c> so an operator edit reaches the very next cadence-slot tick with
/// no process restart.
///
/// <para>
/// Implementations MUST re-evaluate <see cref="For"/> fresh on every call — never cache the result
/// in a field (the same discipline <see cref="ICadenceProvider.Current"/> and its siblings follow).
/// The Host's <c>IOptionsMonitor</c>-backed implementation lands at PLAN T226; until then, a
/// disabled/no-op stand-in keeps <c>GenWave.Context</c> — and every test built against it — compiling
/// and inert.
/// </para>
/// </summary>
public interface IContextSettingsProvider
{
    /// <summary>The live settings for the provider registered under <paramref name="key"/>
    /// (<see cref="IContextProvider.Key"/>), evaluated fresh on every call.</summary>
    ContextProviderSettings For(string key);
}
