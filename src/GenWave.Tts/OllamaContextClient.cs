namespace GenWave.Tts;

using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// The two Ollama calls <see cref="LlmContextWindow"/> needs (SPEC F218.21): <c>GET /api/ps</c> for
/// the resident model's <c>context_length</c>, and Ollama's documented empty-prompt
/// <c>POST /api/generate</c> to load a model that is not resident. Stateless; a client is created per call
/// (never at construction, so resolving it for a status poll opens nothing), no BaseAddress — the
/// endpoint is passed per call, same discipline as <see cref="OllamaHealthProbe"/>.
/// </summary>
public sealed class OllamaContextClient(IHttpClientFactory httpClientFactory)
{
    /// <summary>The resident model's context length, or null when the page is absent or does not list the model.</summary>
    public async Task<int?> ResidentContextAsync(string endpoint, string model, CancellationToken ct)
    {
        var http = httpClientFactory.CreateClient();
        using var response = await http.GetAsync(EndpointUri.Combine(endpoint, "/api/ps"), ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var list = await response.Content.ReadFromJsonAsync<ProcessList>(ct);
        var wanted = WithTag(model);
        var entry = list?.Models?.FirstOrDefault(m => WithTag(m.Name) == wanted || WithTag(m.Model) == wanted);
        return entry is { ContextLength: > 0 } ? entry.ContextLength : null;
    }

    /// <summary>Ollama lists every model with its tag; a configured name without one means <c>:latest</c>.</summary>
    static string? WithTag(string? model) =>
        model is null || model.Substring(model.LastIndexOf('/') + 1).Contains(':') ? model : model + ":latest";

    /// <summary>Loads <paramref name="model"/> into memory without generating anything. Throws on non-2xx.</summary>
    public async Task LoadAsync(string endpoint, string model, CancellationToken ct)
    {
        var http = httpClientFactory.CreateClient();
        var body = JsonSerializer.Serialize(new { model, prompt = "", stream = false });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(EndpointUri.Combine(endpoint, "/api/generate"), content, ct);
        response.EnsureSuccessStatusCode();
    }

    sealed record ProcessList([property: JsonPropertyName("models")] List<ProcessModel>? Models);

    sealed record ProcessModel(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("context_length")] int ContextLength);
}
