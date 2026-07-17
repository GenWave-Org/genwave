namespace GenWave.MediaLibrary.Tests.Fakes;

/// <summary>
/// Fake <see cref="HttpMessageHandler"/> backing an <see cref="HttpClient"/> in tests (SPEC F48.7 —
/// no test may reach the network). A configurable responder function produces the
/// <see cref="HttpResponseMessage"/> for every request; every request is captured in arrival order
/// so specs can assert header/URL shape (e.g. the descriptive User-Agent).
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
