namespace GenWave.Tts.Tests.Fakes;

/// <summary>
/// Fake <see cref="HttpMessageHandler"/> backing an <see cref="HttpClient"/> in tests — no probe
/// spec may reach the network. A configurable responder function produces the
/// <see cref="HttpResponseMessage"/> for every request; every request is captured in arrival
/// order so specs can assert URL shape (e.g. which lightweight path a probe hits) and prove a
/// probe made zero calls at all (mirrors the house precedent,
/// <c>GenWave.MediaLibrary.Tests.Fakes.FakeHttpMessageHandler</c>).
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
