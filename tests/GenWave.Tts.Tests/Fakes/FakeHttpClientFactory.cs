namespace GenWave.Tts.Tests.Fakes;

using Microsoft.Extensions.Http;

/// <summary>
/// Returns a fresh <see cref="HttpClient"/> per call, with no base address — mirrors how
/// <see cref="LlmCopyWriter"/> actually uses <see cref="IHttpClientFactory"/> (absolute URI built
/// per render) closely enough for specs, without standing up the full DI-based factory.
/// </summary>
public sealed class FakeHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new();
}
