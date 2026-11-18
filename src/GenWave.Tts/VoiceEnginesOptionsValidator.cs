namespace GenWave.Tts;

using Microsoft.Extensions.Options;

/// <summary>Boot validation for <c>Tts:Engines</c> (SPEC F219.1): unknown profile, blank endpoint or out-of-range budget fails with the key named.</summary>
public sealed class VoiceEnginesOptionsValidator : IValidateOptions<VoiceEnginesOptions>
{
    /// <summary>Floor of every render budget: an engine's own, and a <c>Tts:EngineRenderBudgets</c> entry.</summary>
    public const int MinRenderBudgetSeconds = 1;

    /// <summary>Same ceiling as the <c>Tts:RenderBudgetSeconds</c> setting.</summary>
    public const int MaxRenderBudgetSeconds = 600;

    public ValidateOptionsResult Validate(string? name, VoiceEnginesOptions options)
    {
        var failures = new List<string>();
        foreach (var (engineName, entry) in options.Engines)
        {
            var prefix = $"Tts:Engines:{engineName}";
            if (VoiceEngineProfiles.Find(entry.Profile) is null)
            {
                failures.Add(
                    $"{prefix}:Profile '{entry.Profile}' is not a known profile " +
                    $"(known: {string.Join(", ", VoiceEngineProfiles.All.Select(p => $"'{p.Name}'"))}).");
            }

            if (string.IsNullOrWhiteSpace(entry.Endpoint))
                failures.Add($"{prefix}:Endpoint must not be blank.");

            if (entry.RenderBudgetSeconds is < MinRenderBudgetSeconds or > MaxRenderBudgetSeconds)
                failures.Add($"{prefix}:RenderBudgetSeconds must be between {MinRenderBudgetSeconds} and {MaxRenderBudgetSeconds} seconds.");
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
