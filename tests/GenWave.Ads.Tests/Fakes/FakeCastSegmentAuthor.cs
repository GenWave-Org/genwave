using GenWave.Core.Domain;
using GenWave.Tts;

namespace GenWave.Ads.Tests.Fakes;

/// <summary>
/// <see cref="ICastSegmentAuthor"/> double for <see cref="AdRenderService"/> specs (T401 review F1)
/// — records the REAL <see cref="CastAssemblyRequest"/> <see cref="AdRenderService"/> built (so a
/// spec can assert the ceiling formula), and — unlike a double that merely stores the
/// <c>buildInsert</c>/<c>confirmAsync</c> delegates without ever CALLING them — actually INVOKES
/// both with controllable, fabricated inputs and captures what the PRODUCTION closures return. This
/// is deliberate: a fake that only records the delegates would let <see cref="AdRenderService"/>'s
/// own <c>BuildInsert</c> (Kind/LibraryId/Tags) and <c>confirmAsync</c> (does it truly reach
/// <c>IAdSpotStore.MarkReadyAsync</c>?) drift silently — the exact "test-local lookalike drifts
/// silently" finding the review named.
/// </summary>
public sealed class FakeCastSegmentAuthor : ICastSegmentAuthor
{
    public CastAssemblyRequest? LastRequest { get; private set; }
    public AuthoredMediaInsert? CapturedInsert { get; private set; }
    public bool? ConfirmResult { get; private set; }

    /// <summary>Whether to actually call <c>buildInsert</c>/<c>confirmAsync</c> (true, default) or
    /// skip straight to <see cref="Result"/> — set false to simulate an assembly-stage failure that
    /// never reaches the authored tail at all.</summary>
    public bool InvokeDelegates { get; set; } = true;

    /// <summary>
    /// PLAN T402 (<see cref="AdSpotWorker"/>'s own cancel-in-flight specs) — when true,
    /// <see cref="AuthorAsync"/> signals <see cref="Entered"/> and then blocks on its own
    /// <paramref name="ct"/> forever, mirroring the CrosstalkWorkerHarness fake synthesizer's own
    /// "genuinely in flight, cancellable" shape (GenWave.Host.Tests, PLAN T286): a spec awaits
    /// <see cref="Entered"/> to know a render has genuinely started, then drives the cancellation it
    /// means to prove, then asserts <see cref="WasCancelled"/>.
    /// </summary>
    public bool BlockUntilCancelled { get; set; }

    /// <summary>Completes the instant <see cref="AuthorAsync"/> is called, ONLY when
    /// <see cref="BlockUntilCancelled"/> is set — the positive-control signal a spec awaits before
    /// driving its own cancellation.</summary>
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Whether the <paramref name="ct"/> <see cref="AuthorAsync"/> was called with was
    /// genuinely observed cancelled — only meaningful when <see cref="BlockUntilCancelled"/> was
    /// set.</summary>
    public bool WasCancelled { get; private set; }

    /// <summary>The media id handed to <c>confirmAsync</c> — a fixed, caller-controllable stand-in
    /// for what a real <c>InsertAuthoredAsync</c> would have returned.</summary>
    public long MediaIdToConfirm { get; set; } = 4200;

    /// <summary>What this fake returns from <see cref="AuthorAsync"/> — success by default.</summary>
    public CastSegmentAuthorResult Result { get; set; } = CastSegmentAuthorResult.Success(4200);

    public async Task<CastSegmentAuthorResult> AuthorAsync(
        CastAssemblyRequest assemblyRequest,
        Func<CrosstalkAssemblyResult.Assembled, AuthoredMediaInsert> buildInsert,
        Func<long, CancellationToken, Task<bool>> confirmAsync,
        CancellationToken ct)
    {
        LastRequest = assemblyRequest;

        if (BlockUntilCancelled)
        {
            Entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
                throw;
            }
        }

        if (InvokeDelegates)
        {
            // A REAL file on disk — AdRenderService's own BuildInsert stats it (new FileInfo(...)),
            // which throws against a path that does not exist.
            var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.wav");
            File.WriteAllBytes(path, [1, 2, 3, 4]);
            try
            {
                var assembled = new CrosstalkAssemblyResult.Assembled(
                    path, new GenWave.Core.Domain.Loudness(-16.0, -1.0, true), Cue: null, DurationMs: 1000);

                // The PRODUCTION buildInsert closure — never a test-local lookalike (T401 review F1).
                CapturedInsert = buildInsert(assembled);

                // The PRODUCTION confirmAsync closure — genuinely invoked, not skipped, so a spec
                // can prove it reaches the real IAdSpotStore.MarkReadyAsync (review F1, mutant 3).
                ConfirmResult = await confirmAsync(MediaIdToConfirm, ct);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        return Result;
    }
}
