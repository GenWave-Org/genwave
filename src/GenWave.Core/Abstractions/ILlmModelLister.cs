namespace GenWave.Core.Abstractions;

/// <summary>
/// Lists the model ids the configured LLM backend currently serves (SPEC F205.7, STORY-479, PLAN
/// T580) — the LLM-side sibling of <see cref="ITtsVoiceLister"/>, same shape: faults (including an
/// unconfigured/blank <c>Llm:Endpoint</c>) surface as exceptions, never an empty list, so the caller
/// (a <c>GenWave.Host.Configuration.IChoiceProbe</c>, through <c>ProbedChoiceCache</c>) is the one
/// place that turns "no answer" into a cached failed/stale outcome.
/// </summary>
public interface ILlmModelLister
{
    /// <summary>
    /// Returns the model ids the LLM backend currently has available. Throws when the backend is
    /// unreachable, answers with a non-2xx status, or <c>Llm:Endpoint</c> is blank — an unconfigured
    /// endpoint is a failure to list from, not an empty catalog.
    /// </summary>
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct);
}
