namespace GenWave.Core.Domain;

/// <summary>
/// The ONE fail-closed parse seam between <c>StationOptions.Audience</c>'s raw configuration string
/// and the <see cref="AudiencePosture"/> enum every pool-predicate consumer switches on (SPEC F95.1,
/// F95.4, PLAN T114). Mirrors <c>GenWave.Tts.DegradationController.ParsePin</c>'s exact shape — trim +
/// <see cref="string.ToLowerInvariant"/>, one case for the non-default value, everything else falls
/// through to the safe default — with one deliberate difference: ParsePin returns a nullable "no pin"
/// result because a degradation pin is optional; this parser is TOTAL. Audience posture is a
/// fail-CLOSED gate, not an optional override, so there is no "unset" state to fall back further
/// from — unrecognized text, empty/whitespace, or a <see langword="null"/> from a misconfigured
/// environment all collapse to <see cref="AudiencePosture.Everyone"/>, the same safe default
/// <c>StationOptions.Audience</c>'s own C# property initializer seeds a fresh station with.
/// </summary>
public static class AudiencePostureParser
{
    public static AudiencePosture Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "mature" => AudiencePosture.Mature,
        // "everyone", empty, whitespace-only, null, or anything unrecognized (SettingValidator should
        // already have rejected this at the settings-API boundary — stay defensive rather than throw
        // from a read path the picker calls on every selection).
        _ => AudiencePosture.Everyone,
    };
}
