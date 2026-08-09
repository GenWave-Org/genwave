namespace GenWave.Context.Tests.Fakes;

/// <summary>
/// Fake <see cref="HttpMessageHandler"/> backing an <see cref="HttpClient"/> in tests — no Story299
/// fact may reach the network. Mirrors <c>GenWave.MediaLibrary.Tests.Fakes.FakeHttpMessageHandler</c>/
/// <c>GenWave.Tts.Tests.Fakes.FakeHttpMessageHandler</c> exactly, one project over. A configurable
/// responder function produces the <see cref="HttpResponseMessage"/> for every request; every request
/// is captured in arrival order so a fact can assert zero calls were made at all (F108.1's
/// fail-closed path) or inspect the URL a real call would have carried.
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
