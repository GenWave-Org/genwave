namespace GenWave.Host.Options;

using Microsoft.Extensions.Options;
using GenWave.Orchestration;

/// <summary>
/// Startup-time validator for <see cref="PersonaRankerOptions"/> (SPEC F82.2-F82.4; STORY-213, T63
/// review carry-over) — the invariants <see cref="PersonaRanker"/>'s own score/softmax math assumes
/// but never itself enforces (the same "nested option class, no recursive DataAnnotations, this
/// validator is the real floor" story <see cref="StationOptionsValidator"/>'s own remarks document).
/// Registered as a singleton and triggered by <c>ValidateOnStart()</c>
/// (<see cref="PersonaRankerOptionsServiceCollectionExtensions.AddGenWavePersonaRanking"/>).
/// </summary>
public sealed class PersonaRankerOptionsValidator : IValidateOptions<PersonaRankerOptions>
{
    public ValidateOptionsResult Validate(string? name, PersonaRankerOptions options)
    {
        if (options.Temperature <= 0)
            return ValidateOptionsResult.Fail(
                "PersonaRanker:Temperature must be greater than 0 (a softmax temperature of 0 or " +
                "below is undefined).");

        if (options.TopK < 1)
            return ValidateOptionsResult.Fail(
                "PersonaRanker:TopK must be at least 1 (the scored pool cannot be empty).");

        if (options.ExplorationRate is < 0.0 or > 1.0)
            return ValidateOptionsResult.Fail(
                "PersonaRanker:ExplorationRate must be within [0, 1].");

        if (options.BiasGain < 0)
            return ValidateOptionsResult.Fail(
                "PersonaRanker:BiasGain must be non-negative.");

        if (options.EnergyPull < 0)
            return ValidateOptionsResult.Fail(
                "PersonaRanker:EnergyPull must be non-negative.");

        return ValidateOptionsResult.Success;
    }
}
