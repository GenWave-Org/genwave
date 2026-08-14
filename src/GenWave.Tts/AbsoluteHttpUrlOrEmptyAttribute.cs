namespace GenWave.Tts;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Validates an OPTIONAL URL leaf whose empty value is a legal, documented "unset" state (T148
/// review finding F5) — plain <see cref="UrlAttribute"/> returns false for
/// <see cref="UrlAttribute.IsValid(object?)"/> given <c>""</c>, so it boot-crashes exactly the
/// empty value <see cref="TtsOptions.PiperPrimaryEndpoint"/>'s own doc comment (and the primary-
/// selection code that reads it, <see cref="TtsServiceCollectionExtensions"/>) treats as "Kokoro
/// is primary, unchanged" — every topology except the piper-only opt-in.
///
/// Mirrors <c>GenWave.Host.Configuration.SettingValidator</c>'s own shape for the equivalent
/// live-edit key, <c>Tts:Fallback:Endpoint</c> (<c>v =&gt; string.IsNullOrEmpty(v) ||
/// IsAbsoluteHttpUri(v)</c>): null/empty passes, and any non-empty value must be an absolute
/// http/https URL — the same "no shape to police when absent, strict when present" rule, just
/// enforced here at DataAnnotations boot-validation time
/// (<see cref="TtsServiceCollectionExtensions.AddGenWaveTts"/>'s <c>ValidateDataAnnotations()</c>)
/// rather than at a live PUT — <see cref="TtsOptions.PiperPrimaryEndpoint"/>'s own remarks explain
/// why this key is deploy-time-only and never reaches that live-edit path at all.
/// </summary>
public sealed class AbsoluteHttpUrlOrEmptyAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) =>
        value switch
        {
            null => true,
            string s when string.IsNullOrEmpty(s) => true,
            string s => Uri.TryCreate(s, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps),
            _ => false,
        };
}
