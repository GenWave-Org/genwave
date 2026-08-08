namespace GenWave.Context.Tests.Fakes;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// Scripted <see cref="IContextProvider"/> double: <see cref="NextResult"/> supplies each call's
/// return value (or throws, when the script itself throws), and <see cref="FetchCount"/> counts
/// every invocation — the pipeline's fetch-once-per-slot contract (SPEC F107.2) is proven directly
/// against this counter rather than a mock-framework call-count assertion.
/// </summary>
sealed class FakeContextProvider(string key) : IContextProvider
{
    public string Key { get; } = key;

    public int FetchCount { get; private set; }

    /// <summary>Invoked once per <see cref="FetchAsync"/> call; defaults to "nothing to say".</summary>
    public Func<ContextContent?> NextResult { get; set; } = () => null;

    public Task<ContextContent?> FetchAsync(CancellationToken ct)
    {
        FetchCount++;
        return Task.FromResult(NextResult());
    }
}
