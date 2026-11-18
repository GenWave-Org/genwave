namespace GenWave.Tts;

using Microsoft.Extensions.Options;

/// <summary>Boot validation for <c>Tts:Engines</c> (SPEC F219.1): unknown profile, blank endpoint or non-positive budget fails with the key named.</summary>
public sealed class VoiceEnginesOptionsValidator : IValidateOptions<VoiceEnginesOptions>
{
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

            if (entry.RenderBudgetSeconds <= 0)
                failures.Add($"{prefix}:RenderBudgetSeconds must be a positive number of seconds.");
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
