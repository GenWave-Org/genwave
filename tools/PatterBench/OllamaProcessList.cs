namespace GenWave.PatterBench;

using System.Text.Json;

/// <summary>Reads how much video memory a loaded model holds from Ollama's <c>/api/ps</c>.</summary>
internal static class OllamaProcessList
{
    private const double BytesPerGiB = 1024.0 * 1024 * 1024;

    /// <summary>The model's video memory in GiB, or null when the endpoint has no such page or does not list the model.</summary>
    internal static async Task<double?> VramGiBAsync(HttpClient http, Uri endpoint, string model, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(GenWave.Tts.EndpointUri.Combine(endpoint.ToString(), "/api/ps"), ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!document.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var entry in models.EnumerateArray())
            {
                if (NameOf(entry, "name") == model || NameOf(entry, "model") == model)
                {
                    return entry.TryGetProperty("size_vram", out var size) && size.TryGetInt64(out var bytes)
                        ? bytes / BytesPerGiB
                        : null;
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private static string? NameOf(JsonElement entry, string property) =>
        entry.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
