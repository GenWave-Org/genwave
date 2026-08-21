namespace GenWave.Tts.Tests.Fakes;

/// <summary>
/// Hands every named-client request to the same fake handler (never disposed by the client) — the
/// shared home for what had drifted into two verbatim per-file copies
/// (<c>Story189_LlmSingleFlightAndWarnDetail</c>, <c>Story350_ContextFactGate</c>; T331 review
/// finding F6). Mirrors <c>GenWave.Host.Tests.Fakes.SingleHandlerHttpClientFactory</c>'s own shape
/// one project over.
/// </summary>
public sealed class SingleHandlerHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
