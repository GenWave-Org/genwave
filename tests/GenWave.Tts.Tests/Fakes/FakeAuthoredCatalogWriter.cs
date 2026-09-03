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

    /// <summary>When set, the next call to <see cref="InsertAuthoredAsync"/> cancels this source
    /// BEFORE observing its own <paramref name="ct"/> — simulates a cancellation racing the insert
    /// call itself (T401 review F3b: CastSegmentAuthor's own cancellation-cleanup arm over the
    /// insert stage), the one place a real caller (e.g. T402's break-window gate) could genuinely
    /// cancel mid-flight before any row commits.</summary>
    public CancellationTokenSource? CancelOnNextInsert { get; set; }

    /// <summary>How many times <see cref="SetEligibleAsync"/> has been called — lets a spec assert
    /// the F161.3 ordering (the flip never happens before a caller's own confirmation succeeds).</summary>
    public int SetEligibleCalls { get; private set; }

    /// <summary>The <c>mediaId</c> most recently passed to <see cref="SetEligibleAsync"/>.</summary>
    public long? LastSetEligibleMediaId { get; private set; }

    /// <summary>The <c>eligible</c> value most recently passed to <see cref="SetEligibleAsync"/>.</summary>
    public bool? LastSetEligibleValue { get; private set; }

    /// <summary>What <see cref="SetEligibleAsync"/> returns — true by default (the row was found).</summary>
    public bool SetEligibleResult { get; set; } = true;

    public Task<long> InsertAuthoredAsync(AuthoredMediaInsert insert, CancellationToken ct)
    {
        if (CancelOnNextInsert is { } cts)
        {
            CancelOnNextInsert = null;
            cts.Cancel();
        }

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

    public Task<bool> SetEligibleAsync(long mediaId, bool eligible, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        SetEligibleCalls++;
        LastSetEligibleMediaId = mediaId;
        LastSetEligibleValue = eligible;
        return Task.FromResult(SetEligibleResult);
    }
}
