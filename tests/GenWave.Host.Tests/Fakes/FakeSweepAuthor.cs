using GenWave.Tts;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// <see cref="ISafeSegmentAuthor"/> double for the re-render sweep: records every request, hands out new media ids,
/// and can be gated so a spec observes the world while a render is in flight.
/// </summary>
sealed class FakeSweepAuthor(FakeSweepCatalog catalog, bool gated = false) : ISafeSegmentAuthor
{
    readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly TaskCompletionSource<SafeSegmentAuthorResult> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    long nextMediaId = FirstNewMediaId;

    public const long FirstNewMediaId = 9001;

    public List<SafeSegmentRequest> Requests { get; } = [];

    /// <summary>The result of the next render; null means success with a fresh media id.</summary>
    public SafeSegmentAuthorResult? NextResult { get; set; }

    /// <summary>Completes once the first render has begun.</summary>
    public Task Started => started.Task;

    public void Release(SafeSegmentAuthorResult result) => gate.TrySetResult(result);

    public async Task<SafeSegmentAuthorResult> AuthorAsync(SafeSegmentRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        started.TrySetResult();

        if (gated)
            return await gate.Task.WaitAsync(ct);

        if (NextResult is { } result)
            return result;

        var id = nextMediaId++;
        catalog.AddRow(id, request.LibraryId, request.AuthoredRoot + "/" + id + ".wav", eligible: true);
        return SafeSegmentAuthorResult.Success(id);
    }
}
