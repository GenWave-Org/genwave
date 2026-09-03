namespace GenWave.Tts;

using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// Authors ONE voice-cast segment end to end (SPEC F161.2, F161.3; STORY-391; PLAN T401): assembles
/// through <see cref="CrosstalkAssembler.AssembleCastAsync"/>, then lands the artifact via the
/// authored tail (<see cref="IAuthoredCatalogWriter.InsertAuthoredAsync"/>) — the
/// <c>SafeSegmentAuthor</c> shape one sibling over, widened for a two-round-trip caller.
///
/// <para>
/// <b>Ineligible until confirmed (SPEC F161.3's as-built rider, the T398 review MATERIAL
/// carry-forward).</b> The insert this class performs ALWAYS lands <c>eligible = false</c> —
/// <paramref name="buildInsert"/>'s own <see cref="AuthoredMediaInsert.Eligible"/> value is
/// unconditionally overridden after the call (never trusted, never merely defaulted), so the
/// invariant cannot be gotten wrong by a careless caller; a dedicated "insert facts, no Eligible
/// field" record was considered and rejected at T401 review (F7) as needless duplication of all
/// 13 OTHER <see cref="AuthoredMediaInsert"/> fields for a value this method already forces
/// unconditionally regardless of what the record carries. Only after <paramref name="confirmAsync"/>
/// — a caller-supplied delegate, the SAME "caller-supplied delegate, never a direct reference"
/// posture <c>AdScriptWriter</c>'s own F160.1 rider already established for this exact project
/// boundary (GenWave.Tts must never reference GenWave.Ads) — reports the caller's OWN bookkeeping is
/// now consistent (for an ad spot: <c>IAdSpotStore.MarkReadyAsync</c> returned
/// <see langword="true"/>, a station-role write behind the db/22 role boundary this project can
/// never cross) does this class flip the row eligible via
/// <see cref="IAuthoredCatalogWriter.SetEligibleAsync"/>. A crash, or a declined confirmation,
/// between the insert and the flip leaves the row ineligible — inert, never an AIRABLE orphan the
/// F158.5 picker could still find (the gh-#610 family this ordering exists to close).
/// </para>
///
/// <para>
/// <b>Not a novel debris class (T401 review F4).</b> An ineligible, undeleted row is not something
/// this class invents: SPEC F159.3 already ships this EXACT shape (<c>eligible=false</c>, never
/// deleted) for a routinely-retired <c>ready</c> spot's own media row — the refresh path produces
/// this residue ON PURPOSE. <see cref="IMediaPurge"/> hard-deletes media rows on an unrelated,
/// age-based <c>unavailable_since</c> sweep — it does not reach either kind. And it is not truly
/// PERMANENT either: an operator can flip <c>Eligible</c> back by hand via the existing
/// <c>PATCH /api/media/{id}</c> seam (<c>IAdminMediaWrite.UpdateReturningVersionAsync</c>,
/// <c>MediaPatch.Eligible</c>) if a specific row is worth reclaiming.
/// </para>
///
/// <para>
/// <b>⚠️ The retry consequence (T401 review F6, gh-#3's GC class).</b> <c>IAdSpotStore.RetryAsync</c>
/// moves a <c>Failed</c> spot back to <c>Approved</c> for a fresh render attempt — but a PRIOR
/// attempt that already reached the insert before failing (a declined/thrown confirmation, or a
/// cancel before the flip — see <see cref="AuthorAsync"/>'s own remarks) already left its own
/// ineligible row behind, and nothing here deletes it on retry. Each retry of the SAME spot can
/// therefore accumulate one more orphaned, ineligible row per failed attempt; closing that gap
/// (garbage collection, or a purge extension) is a future task's own concern — this class only
/// documents the shape, per the review.
/// </para>
///
/// <para>
/// All-or-nothing UP TO the insert (mirrors <c>SafeSegmentAuthor</c>'s own posture): any failure —
/// including a cancellation — before the row commits deletes every file this attempt wrote,
/// including <paramref name="buildInsert"/>'s own failure (T401 review F3: folded INTO the same
/// cleanup boundary as the insert call itself, not left to leak the final artifact on a caller
/// bug). A failure AFTER the row commits leaves it ineligible instead of being rolled back — see
/// the "not a novel debris class" remarks above.
/// </para>
/// </summary>
public sealed class CastSegmentAuthor(
    CrosstalkAssembler assembler,
    IAuthoredCatalogWriter catalogWriter,
    ILogger<CastSegmentAuthor> logger) : ICastSegmentAuthor
{
    /// <summary>
    /// Assembles <paramref name="assemblyRequest"/>, inserts the result INELIGIBLE, and — only once
    /// <paramref name="confirmAsync"/> reports success — flips it eligible.
    /// </summary>
    /// <param name="assemblyRequest">Everything <see cref="CrosstalkAssembler.AssembleCastAsync"/>
    /// needs to render and mix the cast.</param>
    /// <param name="buildInsert">
    /// Builds the <see cref="AuthoredMediaInsert"/> from the assembled artifact's own measured
    /// facts (path, loudness, cue, duration) — every OTHER field (library id, tags, kind, show id)
    /// is the caller's own static knowledge, closed over. <see cref="AuthoredMediaInsert.Eligible"/>
    /// on the returned value is ignored — this method always inserts ineligible (see the class
    /// remarks).
    /// </param>
    /// <param name="confirmAsync">
    /// Called with the newly-inserted media id once it exists. Returning
    /// <see langword="true"/> confirms the caller's own bookkeeping is consistent and the row should
    /// become eligible; <see langword="false"/> (or a thrown exception) leaves it ineligible forever.
    /// </param>
    public async Task<CastSegmentAuthorResult> AuthorAsync(
        CastAssemblyRequest assemblyRequest,
        Func<CrosstalkAssemblyResult.Assembled, AuthoredMediaInsert> buildInsert,
        Func<long, CancellationToken, Task<bool>> confirmAsync,
        CancellationToken ct)
    {
        CrosstalkAssemblyResult assembled;
        try
        {
            assembled = await assembler.AssembleCastAsync(assemblyRequest, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cast segment assembly failed");
            return CastSegmentAuthorResult.Failure(CastSegmentFailureReason.AssemblyFailed, ex.Message);
        }

        if (assembled is CrosstalkAssemblyResult.Discarded discarded)
            return CastSegmentAuthorResult.Failure(CastSegmentFailureReason.Discarded, discarded.Reason);

        var result = (CrosstalkAssemblyResult.Assembled)assembled;

        // T401 review F3: buildInsert lives INSIDE this try (folded together with the insert
        // itself) — a throw from a caller's own buildInsert closure gets the identical cleanup an
        // InsertAuthoredAsync failure already got, rather than leaking the final artifact.
        long mediaId;
        try
        {
            var insert = buildInsert(result) with { Eligible = false };
            mediaId = await catalogWriter.InsertAuthoredAsync(insert, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Nothing has committed yet — buildInsert or the insert itself was cancelled mid-flight
            // (T402 cancels an in-flight render routinely, SPEC F161.1's break-window gate). Past
            // this point a cancel must NOT delete the artifact — the row already owns that path;
            // deleting it out from under an inserted (if ineligible) row would be worse than leaving
            // it ineligible (see the class remarks). Honest caveat (T401 round-2 review): a cancel
            // observed WHILE awaiting InsertAuthoredAsync cannot prove the server-side INSERT itself
            // never committed — a vanishingly narrow client/server race could still leave the artifact
            // deleted out from under an ineligible, undeleted row; that row stays permanently inert
            // (never airable, never re-confirmed) rather than actively harmful, so this is accepted
            // rather than engineered around.
            DeleteIfExists(result.Path);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cast segment catalog insert failed for {Path}", result.Path);
            DeleteIfExists(result.Path);
            return CastSegmentAuthorResult.Failure(CastSegmentFailureReason.InsertFailed, ex.Message);
        }

        bool confirmed;
        try
        {
            confirmed = await confirmAsync(mediaId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The row already committed and is NOT deleted (see the class remarks — F159.3 already
            // ships this exact shape). Logged loud (Error, not Warning): an ineligible orphan row is
            // a standing, silent-by-default gap unless it is flagged here.
            logger.LogError(ex,
                "Cast segment {MediaId} inserted but its confirmation threw — the row stays ineligible, never airable",
                mediaId);
            return CastSegmentAuthorResult.Failure(CastSegmentFailureReason.ConfirmationFailed, ex.Message);
        }

        if (!confirmed)
        {
            logger.LogError(
                "Cast segment {MediaId} inserted but its confirmation reported failure — the row stays ineligible, never airable",
                mediaId);
            return CastSegmentAuthorResult.Failure(CastSegmentFailureReason.ConfirmationFailed, "confirmation declined");
        }

        if (!await catalogWriter.SetEligibleAsync(mediaId, eligible: true, ct))
        {
            // The confirmation itself succeeded (the caller's own bookkeeping is consistent), but the
            // eligibility flip found no matching row — a genuinely bizarre race, never expected in
            // practice. Logged loud; still reported as SUCCESS to the caller, whose own state has
            // already committed a "ready" outcome this method cannot un-commit.
            logger.LogError(
                "Cast segment {MediaId} confirmed ready but the eligibility flip found no matching row",
                mediaId);
        }

        return CastSegmentAuthorResult.Success(mediaId);
    }

    static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup — mirrors SafeSegmentAuthor/CrosstalkAssembler's own identical
            // precedent: a locked/undeletable file is a secondary concern, never worth masking the
            // real outcome this call is already returning.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup — see the IOException arm's own remarks.
        }
    }
}
