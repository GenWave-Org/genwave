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

    /// <summary>gh-#854 — when true, blocks right after <c>buildInsert</c> and before
    /// <c>confirmAsync</c> is ever called, until <see cref="ReleaseBeforeConfirm"/> completes (or
    /// <c>ct</c> cancels) — the window a spec needs to prove that a fact changing mid-render (the old
    /// media going operator-disabled) is caught by <c>confirmAsync</c>'s own fresh re-check, never by
    /// whatever was true when the render started.</summary>
    public bool BlockBeforeConfirm { get; set; }

    /// <summary>Completes the instant <see cref="AuthorAsync"/> reaches the block above, ONLY when
    /// <see cref="BlockBeforeConfirm"/> is set — a spec awaits this to know the render has produced its
    /// media and is about to confirm, before driving whatever change it means to prove
    /// <c>confirmAsync</c> catches.</summary>
    public TaskCompletionSource EnteredBeforeConfirm { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>A spec completes this to release <see cref="AuthorAsync"/> from its
    /// <see cref="BlockBeforeConfirm"/> wait and let <c>confirmAsync</c> actually run.</summary>
    public TaskCompletionSource ReleaseBeforeConfirm { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The request <see cref="AssembleOnlyAsync"/> was most recently called with (PLAN T442
    /// preview mode) — a spec asserting <c>AdRenderService.RenderPreviewAsync</c>'s own build never
    /// reads <see cref="LastRequest"/> for this, since a preview render never calls
    /// <see cref="AuthorAsync"/> at all.</summary>
    public CastAssemblyRequest? LastAssembleOnlyRequest { get; private set; }

    /// <summary>Overrides what <see cref="AssembleOnlyAsync"/> returns — a real, on-disk
    /// <see cref="CrosstalkAssemblyResult.Assembled"/> by default (see that method's own remarks); set
    /// to a <see cref="CrosstalkAssemblyResult.Discarded"/> to simulate a preview that never produces a
    /// file at all.</summary>
    public CrosstalkAssemblyResult? AssembleOnlyResult { get; set; }

    /// <summary>The extension <see cref="AssembleOnlyAsync"/> writes its default, on-disk
    /// <see cref="CrosstalkAssemblyResult.Assembled"/> with — <c>"wav"</c> by default. Set to
    /// <c>"mp3"</c> (PLAN T442 ruling) to simulate a station whose <c>Tts:Format</c> is
    /// not <c>wav</c>, so a spec can prove <see cref="AdRenderService"/>'s own preview path fails
    /// closed instead of silently renaming the wrong container.</summary>
    public string AssembleOnlyExtension { get; set; } = "wav";

    public async Task<CastSegmentAuthorResult> AuthorAsync(
        CastAssemblyRequest assemblyRequest,
        Func<CrosstalkAssemblyResult.Assembled, AuthoredMediaInsert> buildInsert,
        Func<long, CancellationToken, Task<bool>> confirmAsync,
        CancellationToken ct,
        bool flipEligibleOnConfirm = true)
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

                if (BlockBeforeConfirm)
                {
                    EnteredBeforeConfirm.TrySetResult();
                    await ReleaseBeforeConfirm.Task.WaitAsync(ct);
                }

                // The PRODUCTION confirmAsync closure — genuinely invoked, not skipped, so a spec
                // can prove it reaches the real IAdSpotStore.MarkReadyAsync (review F1, mutant 3).
                // gh-#854: this fake never flips eligibility itself — confirmAsync IS
                // AdSpotRepository.SwapRenderedMediaAsync (or IAdSpotStore.MarkReadyAsync for the
                // first-render path), and either one now owns both flips atomically inside itself;
                // flipEligibleOnConfirm is accepted only to match ICastSegmentAuthor's shape.
                ConfirmResult = await confirmAsync(MediaIdToConfirm, ct);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        // gh-#854 — mirrors the real CastSegmentAuthor's own contract: a declined confirmAsync
        // (invoked for real above, never a lookalike) is always a genuine failure, never whatever the
        // caller preset Result to before the call. Result only governs the InvokeDelegates=false case
        // (confirmAsync never reached at all) or a genuine success.
        if (ConfirmResult == false)
            return CastSegmentAuthorResult.Failure(CastSegmentFailureReason.ConfirmationFailed, "confirmation declined");

        return Result;
    }

    /// <summary>The <c>assembled</c> value <see cref="LandAsync"/> was most recently called with
    /// (PLAN T445) — a spec asserting <c>AdRenderService.PromotePreviewAsync</c> measured the MOVED
    /// file (not the original preview path) reads this against the destination path it expects.</summary>
    public CrosstalkAssemblyResult.Assembled? LastLandAssembled { get; private set; }

    /// <summary>The <c>buildInsert</c> result <see cref="LandAsync"/> most recently captured — same
    /// "invoke the real production closure, never a test-local lookalike" posture as
    /// <see cref="CapturedInsert"/> above, one seam over.</summary>
    public AuthoredMediaInsert? CapturedLandInsert { get; private set; }

    /// <summary>What <c>confirmAsync</c> returned the last time <see cref="LandAsync"/> genuinely
    /// invoked it.</summary>
    public bool? LandConfirmResult { get; private set; }

    /// <summary>Whether <see cref="LandAsync"/> should actually call <c>buildInsert</c>/<c>confirmAsync</c>
    /// (true, default) — set false to simulate a landing attempt that never reaches the authored tail
    /// at all (mirrors <see cref="InvokeDelegates"/> above, one seam over).</summary>
    public bool InvokeLandDelegates { get; set; } = true;

    /// <summary>The media id handed to <c>confirmAsync</c> from <see cref="LandAsync"/> — a fixed,
    /// caller-controllable stand-in for what a real <c>InsertAuthoredAsync</c> would have returned.</summary>
    public long MediaIdToConfirmOnLand { get; set; } = 4200;

    /// <summary>What <see cref="LandAsync"/> returns — success by default.</summary>
    public CastSegmentAuthorResult LandResult { get; set; } = CastSegmentAuthorResult.Success(4200);

    /// <inheritdoc cref="ICastSegmentAuthor.LandAsync"/>
    public async Task<CastSegmentAuthorResult> LandAsync(
        CrosstalkAssemblyResult.Assembled assembled,
        Func<CrosstalkAssemblyResult.Assembled, AuthoredMediaInsert> buildInsert,
        Func<long, CancellationToken, Task<bool>> confirmAsync,
        CancellationToken ct,
        bool flipEligibleOnConfirm = true)
    {
        LastLandAssembled = assembled;

        if (InvokeLandDelegates)
        {
            // The PRODUCTION buildInsert/confirmAsync closures AdRenderService.PromotePreviewAsync
            // itself builds — never a test-local lookalike, one seam over at PLAN T445.
            CapturedLandInsert = buildInsert(assembled);
            LandConfirmResult = await confirmAsync(MediaIdToConfirmOnLand, ct);
        }

        return LandResult;
    }

    /// <summary>The path <see cref="MeasureAsync"/> was most recently called with (PLAN T445).</summary>
    public string? LastMeasurePath { get; private set; }

    /// <summary>Overrides what <see cref="MeasureAsync"/> returns — built from the CALLED path by
    /// default (so a spec that never overrides this still gets a coherent
    /// <see cref="CrosstalkAssemblyResult.Assembled.Path"/> back); set to prove
    /// <c>AdRenderService.PromotePreviewAsync</c> hands the MEASURED result's loudness/cue/duration
    /// straight through to <c>buildInsert</c> untouched.</summary>
    public CrosstalkAssemblyResult.Assembled? MeasureResult { get; set; }

    /// <summary>When set, <see cref="MeasureAsync"/> throws this instead of returning (PLAN T445) —
    /// simulates a failure AFTER <c>AdRenderService.PromotePreviewAsync</c>'s own <c>File.Move</c> has
    /// already relocated the preview file to its destination, so a spec built on this can prove that
    /// path's own cleanup (nothing left under the ads root) and its <see cref="AdPromotionOutcome.Failed.FileMoved"/>
    /// value.</summary>
    public Exception? MeasureAsyncThrows { get; set; }

    /// <inheritdoc cref="ICastSegmentAuthor.MeasureAsync"/>
    public Task<CrosstalkAssemblyResult.Assembled> MeasureAsync(string path, CancellationToken ct)
    {
        LastMeasurePath = path;
        if (MeasureAsyncThrows is { } exceptionToThrow)
            throw exceptionToThrow;

        var assembled = MeasureResult ?? new CrosstalkAssemblyResult.Assembled(
            path, new GenWave.Core.Domain.Loudness(-16.0, -1.0, true), Cue: null, DurationMs: 1000);
        return Task.FromResult(assembled);
    }

    /// <summary>
    /// Writes a REAL file to disk, INTO <paramref name="request"/>'s own
    /// <see cref="CastAssemblyRequest.OutputDirectory"/> (PLAN T442 ruling — never
    /// <c>Path.GetTempPath()</c>: the real <see cref="AdRenderService.RenderPreviewCoreAsync"/> creates
    /// that exact directory before calling this method, and a preview render that assembled outside its
    /// own preview root would defeat the whole point of a caller writing there in the first place) so a
    /// caller's own <c>File.Move</c> off the returned path succeeds — <see cref="AdRenderService"/>'s
    /// preview path never stats or reads this file beyond moving it, but a stale/missing path would
    /// throw exactly like the real assembler's own output would.
    /// </summary>
    public Task<CrosstalkAssemblyResult> AssembleOnlyAsync(CastAssemblyRequest request, CancellationToken ct)
    {
        LastAssembleOnlyRequest = request;

        if (AssembleOnlyResult is not null)
            return Task.FromResult(AssembleOnlyResult);

        var path = Path.Combine(request.OutputDirectory, $"{Guid.NewGuid():N}.{AssembleOnlyExtension}");
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        CrosstalkAssemblyResult assembled = new CrosstalkAssemblyResult.Assembled(
            path, new GenWave.Core.Domain.Loudness(-16.0, -1.0, true), Cue: null, DurationMs: 1000);
        return Task.FromResult(assembled);
    }
}
