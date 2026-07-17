namespace GenWave.Tts;

/// <summary>
/// Joins a configured base endpoint (<c>Tts:Endpoint</c>/<c>Llm:Endpoint</c> — either may itself
/// carry a subpath, e.g. <c>https://host/openai</c>) with a fixed relative API path, WITHOUT
/// dropping that subpath.
///
/// <c>new Uri(new Uri(baseEndpoint), "/v1/x")</c> treats the second argument as an absolute
/// path — per <see cref="System.Uri"/>'s own combine rule, a leading <c>/</c> replaces the
/// base's entire path rather than appending to it, silently discarding <c>/openai</c> above.
/// This is the T3 review finding this helper exists to fix once, for every absolute-URI-per-call
/// site (SPEC F36.2): <c>KokoroTtsSynthesizer</c>, <c>KokoroVoiceLister</c>, <c>LlmCopyWriter</c>.
/// </summary>
internal static class EndpointUri
{
    public static Uri Combine(string baseEndpoint, string relativePath) =>
        new($"{baseEndpoint.TrimEnd('/')}/{relativePath.TrimStart('/')}");
}
