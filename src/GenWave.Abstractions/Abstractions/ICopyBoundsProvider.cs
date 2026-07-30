namespace GenWave.Core.Abstractions;

/// <summary>
/// gh-#253 — the thin accessor seam between the patter-duration estimator's cold heuristic tier
/// (<c>GenWave.Orchestration</c>, which cannot see <c>GenWave.Tts</c>'s <c>LlmOptions</c>) and the
/// live <c>Llm:MaxCopyChars</c> bound. Mirrors <see cref="IRenderBudgetProvider"/> one seam over: a
/// single value, read fresh on every call rather than cached, so a live <c>PUT /api/settings</c>
/// edit reaches the very next estimate with no restart.
/// </summary>
public interface ICopyBoundsProvider
{
    /// <summary>
    /// The live <c>Llm:MaxCopyChars</c> ceiling (SPEC F34.5) — LLM copy longer than this is
    /// rejected to the template fallback, so it bounds the estimator's worst case. Evaluated fresh
    /// on every call.
    /// </summary>
    int MaxCopyChars { get; }
}
