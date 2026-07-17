namespace GenWave.Tts.Tests.Fakes;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

public sealed class FakeAuthoredCatalogWriter : IAuthoredCatalogWriter
{
    public int Calls { get; private set; }
    public AuthoredMediaInsert? LastInsert { get; private set; }
    public long NextId { get; set; } = 1;

    /// <summary>When non-null, the next call to InsertAuthoredAsync will throw this exception.</summary>
    public Exception? ThrowOnNextCall { get; set; }

    public Task<long> InsertAuthoredAsync(AuthoredMediaInsert insert, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        LastInsert = insert;

        if (ThrowOnNextCall is { } ex)
        {
            ThrowOnNextCall = null;
            throw ex;
        }

        Calls++;
        return Task.FromResult(NextId);
    }
}
