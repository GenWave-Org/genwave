namespace GenWave.Ads;

/// <summary>
/// SPEC F163.1 (STORY-388, PLAN T396) — projects only <c>Station:Ads:AntiRepeatWindow</c> (Live,
/// default 5, validated 0-50; the allowlist row + range enforcement land at PLAN T397) so
/// <see cref="LibraryAdSpotSource"/>'s in-memory anti-repeat ring reads it LIVE, via
/// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>, with no api restart — ahead
/// of the allowlist row landing (an operator can already set the env/compose value; T397 only adds
/// the live-PUT surface). Bound independently of <see cref="AdsOptions"/> — a DIFFERENT config
/// namespace (<c>Station:Ads:*</c>, not <c>Ads:*</c>) and a DIFFERENT validation posture (no
/// <c>[Range]</c>/<c>ValidateOnStart</c> here: Live settings are range-checked at the settings
/// allowlist/validator, never by DataAnnotations on a bound options class — the same split
/// <c>GardenerOptions</c>'s own remarks document for its one Live exception).
/// </summary>
public sealed class AdSpotAntiRepeatOptions
{
    public const string SectionName = "Station:Ads";

    /// <summary>Count of most-recently-vended ad spot ids the in-memory ring excludes from the next
    /// pick (SPEC F158.5). Default 5 (SPEC F163.1's own explicit default).</summary>
    public int AntiRepeatWindow { get; set; } = 5;
}
