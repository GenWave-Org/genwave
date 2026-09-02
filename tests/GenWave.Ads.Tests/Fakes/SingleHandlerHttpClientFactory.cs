namespace GenWave.Ads.Tests.Fakes;

/// <summary>
/// Hands every named-client request to the same fake handler (never disposed by the client) — mirrors
/// <c>GenWave.Tts.Tests.Fakes.SingleHandlerHttpClientFactory</c>'s own shape one project over (PLAN
/// T400 review F2).
/// </summary>
public sealed class SingleHandlerHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
