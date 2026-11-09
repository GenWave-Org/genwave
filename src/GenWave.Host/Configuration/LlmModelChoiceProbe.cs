using GenWave.Core.Abstractions;
using GenWave.Tts;
using Microsoft.Extensions.Options;

namespace GenWave.Host.Configuration;

/// <summary>
/// Live <see cref="IChoiceProbe"/> for <c>Llm:Model</c> (SPEC F205.7, STORY-479, PLAN T580) — wraps
/// <see cref="ILlmModelLister"/> (<see cref="OpenAiModelLister"/> in production), scoped by the live
/// <c>Llm:Endpoint</c> value so a repoint invalidates <see cref="ProbedChoiceCache"/>'s own 60 s
/// entry immediately rather than serving the OLD endpoint's model list (see
/// <see cref="IChoiceProbe.ScopeKey"/>'s own remarks). A blank <c>Llm:Endpoint</c> makes
/// <see cref="ILlmModelLister.ListModelsAsync"/> throw — this probe does not special-case that,
/// letting the cache record it as a failed attempt exactly like any other fault (SPEC F205.7c).
/// <see cref="ILlmModelLister"/> arrives wrapped in <see cref="Lazy{T}"/> (Program.cs) — resolving
/// THIS probe (as <see cref="ChoiceSourceBootCheck"/> does, once, at boot) must never itself build
/// the live lister's typed <c>HttpClient</c>; only an actual <see cref="FetchAsync"/> call does.
/// </summary>
internal sealed class LlmModelChoiceProbe(
    Lazy<ILlmModelLister> modelLister, IOptionsMonitor<LlmOptions> optionsMonitor) : IChoiceProbe
{
    /// <summary>The <see cref="StationSettingsAllowlist"/> <c>Llm:Model</c> entry's own
    /// <see cref="SettingChoiceSource.Probe.Name"/> — also what <see cref="ChoiceSourceBootCheck"/>
    /// checks the allowlist against.</summary>
    public const string ProbeName = "llm";

    /// <summary>F218.28 as amended 2026-11-09 (Dean: recommend, don't default — <c>Llm:Model</c> stays blank, F34.2):
    /// picked by Dean from the PatterBench run in <c>tools/PatterBench/RESULTS.md</c>.</summary>
    public const string RecommendedModel = "mistral-nemo:12b";

    public string Name => ProbeName;

    public string ScopeKey() => optionsMonitor.CurrentValue.Endpoint;

    public async Task<IReadOnlyList<SettingChoice>> FetchAsync(CancellationToken ct)
    {
        var modelIds = await modelLister.Value.ListModelsAsync(ct).ConfigureAwait(false);
        var choices = modelIds.Select(id => new SettingChoice(id, id)).ToList();

        var recommendedAt = choices.FindIndex(c => string.Equals(c.Value, RecommendedModel, StringComparison.Ordinal));
        if (recommendedAt >= 0)
        {
            choices.RemoveAt(recommendedAt);
            choices.Insert(0, new SettingChoice(RecommendedModel, $"{RecommendedModel} (recommended)"));
        }

        return choices;
    }
}
