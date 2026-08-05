namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// Hands every named-client request to the same fake handler (never disposed by the client) — the
/// shared home for what had drifted into four verbatim per-file copies (Gh131_GenreRequestPredicates,
/// Story225_WishParsing, Story273_ThemeShelfPreview, Story278_ThemeCatalogIsolation; review finding,
/// STORY-278/T190). <c>Story234_CatalogProxyGuardedDoor</c> keeps its OWN copy: it additionally sets
/// <see cref="HttpClient.MaxResponseContentBufferSize"/> to mirror <c>CatalogProxyService</c>'s real
/// client registration exactly, so it is not a verbatim duplicate of this one.
/// </summary>
sealed class SingleHandlerHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
