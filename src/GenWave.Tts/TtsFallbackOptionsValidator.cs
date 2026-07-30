namespace GenWave.Tts;

using Microsoft.Extensions.Options;

/// <summary>
/// Startup validation for the gh-#147 fallback chain (<c>Tts:Fallback:Profiles</c>) — fails the
/// boot loudly (ValidateOnStart, wired in <see cref="TtsServiceCollectionExtensions"/>) on an
/// unknown engine kind, a missing/relative endpoint, or a non-positive per-hop budget: a
/// misconfigured resiliency chain is a deployment mistake to surface at deploy time, never a hop
/// silently skipped mid-broadcast.
///
/// Deliberately validates ONLY the new list shape. The legacy flat <c>Endpoint</c>/<c>Voice</c>
/// keys keep their historical anything-goes binding (SettingValidator still guards their live-edit
/// path through PUT /api/settings), so an operator upgrading with old keys cannot be broken at
/// boot by a validator that did not exist when they deployed. An empty Profiles list is valid — it
/// means "use the legacy keys, or no fallback at all" (<see cref="TtsFallbackChain"/>).
/// </summary>
public sealed class TtsFallbackOptionsValidator : IValidateOptions<TtsFallbackOptions>
{
    public ValidateOptionsResult Validate(string? name, TtsFallbackOptions options)
    {
        var failures = new List<string>();

        for (var i = 0; i < options.Profiles.Count; i++)
        {
            var profile = options.Profiles[i];
            var prefix = $"Tts:Fallback:Profiles:{i}";

            var engine = profile.Engine.Trim().ToLowerInvariant();
            if (engine is not (DependencyNames.Kokoro or DependencyNames.Piper))
            {
                failures.Add(
                    $"{prefix}:Engine '{profile.Engine}' is not a known engine kind " +
                    $"(known: '{DependencyNames.Kokoro}', '{DependencyNames.Piper}').");
            }

            if (!IsAbsoluteHttpUri(profile.Endpoint))
                failures.Add($"{prefix}:Endpoint '{profile.Endpoint}' must be an absolute http/https URL.");

            if (profile.TimeoutSeconds is { } budget && (!double.IsFinite(budget) || budget <= 0))
                failures.Add($"{prefix}:TimeoutSeconds must be a positive number of seconds when set.");
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }

    static bool IsAbsoluteHttpUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
