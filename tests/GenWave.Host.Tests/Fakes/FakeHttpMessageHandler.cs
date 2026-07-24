namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// Fake <see cref="HttpMessageHandler"/> backing an <see cref="HttpClient"/> in tests — no spec may
/// reach the network. A configurable responder function produces the
/// <see cref="HttpResponseMessage"/> for every request; every request is captured in arrival order so
/// a spec can assert body/header shape (mirrors the house precedent,
/// <c>GenWave.MediaLibrary.Tests.Fakes.FakeHttpMessageHandler</c>/<c>GenWave.Tts.Tests.Fakes.FakeHttpMessageHandler</c>).
/// </summary>
sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return await respond(request, cancellationToken);
    }
}
