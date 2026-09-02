using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Ads.Tests.Fakes;

/// <summary>
/// <see cref="IAuthoredCatalogWriter"/> double for <see cref="AdSpotWorker"/> specs (PLAN T402) — the
/// worker only ever calls <see cref="SetEligibleAsync"/> (the repair sweep and the retire flip);
/// <see cref="InsertAuthoredAsync"/> is <c>CastSegmentAuthor</c>'s own seam, unreachable through the
/// <see cref="FakeCastSegmentAuthor"/> path these specs render through, and throws if ever called (the
/// <see cref="FakeAdSpotCatalog"/> precedent, this same Fakes directory).
/// </summary>
public sealed class FakeAuthoredCatalogWriter : IAuthoredCatalogWriter
{
    public int SetEligibleCalls { get; private set; }
    public List<(long MediaId, bool Eligible)> SetEligibleHistory { get; } = [];
    public bool SetEligibleResult { get; set; } = true;

    public Task<bool> SetEligibleAsync(long mediaId, bool eligible, CancellationToken ct)
    {
        SetEligibleCalls++;
        SetEligibleHistory.Add((mediaId, eligible));
        return Task.FromResult(SetEligibleResult);
    }

    public Task<long> InsertAuthoredAsync(AuthoredMediaInsert insert, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotWorker.");
}
