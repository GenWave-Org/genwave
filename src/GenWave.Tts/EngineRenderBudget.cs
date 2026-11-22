namespace GenWave.Tts;

using System.Text.Json;

/// <summary>
/// The one rule for how long an engine's render may take (SPEC F219.6, T750a): the engine's entry in
/// <c>Tts:EngineRenderBudgets</c>, else its own configured <see cref="VoiceEngine.RenderBudgetSeconds"/>, else
/// <c>Tts:RenderBudgetSeconds</c> (when there is no engine at all). Read per call, so a live edit applies to the
/// next render. Used by the render-chain provider and by <c>GET /api/voices/engines</c>.
/// </summary>
public static class EngineRenderBudget
{
    public static int For(VoiceEngine? engine, TtsOptions options)
    {
        if (engine is null)
            return options.RenderBudgetSeconds;

        return Override(options.EngineRenderBudgets, engine.Name) ?? engine.RenderBudgetSeconds;
    }

    /// <summary>
    /// The one parser for the <c>Tts:EngineRenderBudgets</c> format: a JSON object of engine name to whole seconds
    /// within the engine budget bounds. Blank is "no overrides" (empty map). Names compare case-insensitively; null
    /// on any malformation, including two keys that differ only in case.
    /// </summary>
    public static IReadOnlyDictionary<string, int>? TryParse(string? json)
    {
        var budgets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
            return budgets;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Number
                    || !property.Value.TryGetInt32(out var seconds)
                    || seconds is < VoiceEnginesOptionsValidator.MinRenderBudgetSeconds
                        or > VoiceEnginesOptionsValidator.MaxRenderBudgetSeconds
                    || !budgets.TryAdd(property.Name, seconds))
                    return null;
            }

            return budgets;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The settings validator guards the write path; an unreadable value (a stale row) just means "no override".
    static int? Override(string? json, string engineName) =>
        TryParse(json)?.TryGetValue(engineName, out var seconds) == true ? seconds : null;
}
