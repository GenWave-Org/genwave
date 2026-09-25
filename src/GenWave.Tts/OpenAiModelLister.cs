namespace GenWave.Tts;

using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;

/// <summary>
/// Typed-HttpClient proxy for an OpenAI-compatible <c>GET {Llm:Endpoint}/v1/models</c> (SPEC
/// F205.7, STORY-479, PLAN T580) — the LLM-side sibling of <see cref="KokoroVoiceLister"/>, same
/// no-boot-frozen-<see cref="HttpClient.BaseAddress"/> discipline (SPEC F36.1–F36.4):
/// <c>Llm:Endpoint</c> is read from <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> and an
/// absolute URI is built per call (<see cref="EndpointUri"/>), so a live PUT to
/// <c>Llm:Endpoint</c> applies to the very next list without an api restart.
///
/// <para>
/// An empty <c>Llm:Endpoint</c> throws rather than returning an empty list — unlike
/// <see cref="OllamaHealthProbe"/>'s "off is not a failure" health-check posture, "list what
/// models this unconfigured backend serves" has no honest empty answer: the caller
/// (<c>GenWave.Host.Configuration.LlmModelChoiceProbe</c>, through <c>ProbedChoiceCache</c>) must
/// see this as a failed attempt, not a backend that legitimately serves zero models.
/// </para>
/// </summary>
public sealed class OpenAiModelLister(HttpClient http, IOptionsMonitor<LlmOptions> optionsMonitor) : ILlmModelLister
{
    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct)
    {
        var endpoint = optionsMonitor.CurrentValue.Endpoint;
        if (string.IsNullOrEmpty(endpoint))
            throw new InvalidOperationException("Llm:Endpoint is not configured.");

        var requestUri = EndpointUri.Combine(endpoint, "/v1/models");
        using var response = await http.GetAsync(requestUri, ct);
        response.EnsureSuccessStatusCode();   // throws HttpRequestException on non-2xx

        var payload = await response.Content.ReadFromJsonAsync<OpenAiModelsResponse>(ct);
        return payload?.ModelIds() ?? [];
    }
}
