namespace GenWave.Ads.Tests.Fakes;

/// <summary>
/// Fake <see cref="HttpMessageHandler"/> backing an <see cref="HttpClient"/> in tests — no spec may
/// reach the network. A configurable responder function produces the <see cref="HttpResponseMessage"/>
/// for every request; every request is captured in arrival order (PLAN T400 review F2 — the real-Tts-
/// meets-real-Ads crossing fact needs a stub completions endpoint, the SAME shape
/// <c>GenWave.Tts.Tests.Fakes.FakeHttpMessageHandler</c> already carries one project over).
/// </summary>
public sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return await respond(request, cancellationToken);
    }
}
