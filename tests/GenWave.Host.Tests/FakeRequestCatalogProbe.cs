using GenWave.Core.Abstractions;

namespace GenWave.Host.Tests;

/// <summary>
/// Scriptable <see cref="IRequestCatalogProbe"/> double (STORY-226, PLAN T89): hands back whatever
/// <see cref="Result"/> is currently set to (default <see langword="null"/> — "no catalog match"),
/// and records every call's artist/title so a spec can assert exactly what predicate reached the
/// probe without a real Postgres connection.
/// </summary>
sealed class FakeRequestCatalogProbe : IRequestCatalogProbe
{
    public long? Result { get; set; }
    public List<(string? Artist, string? Title)> Calls { get; } = [];

    public Task<long?> FindBestAsync(string? artist, string? title, CancellationToken ct)
    {
        Calls.Add((artist, title));
        return Task.FromResult(Result);
    }
}
