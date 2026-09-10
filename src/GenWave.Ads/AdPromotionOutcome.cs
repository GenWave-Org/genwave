namespace GenWave.Ads;

/// <summary>
/// Outcome of <see cref="AdRenderService.PromotePreviewAsync"/> (SPEC F174.5; STORY-425; STORY-429
/// AC1; PLAN T445) — mirrors <see cref="AdPreviewOutcome"/>'s own closed-hierarchy posture one seam
/// over: a promotion never marks the spot itself <see cref="Core.Domain.AdState.Failed"/> — that
/// decision belongs to the CALLER (<c>AdsController.Approve</c>, via
/// <see cref="Core.Abstractions.IAdSpotStore.ReArmAsync"/>, back to <see cref="Core.Domain.AdState.Approved"/>,
/// never <c>Failed</c> — see <see cref="AdRenderService.PromotePreviewAsync"/>'s own remarks), so this
/// carries only what that caller actually needs to decide its own next write.
/// </summary>
public abstract record AdPromotionOutcome
{
    AdPromotionOutcome() { }

    /// <summary>The preview landed as a real, airable <c>library.media</c> row.
    /// <paramref name="MediaId"/> is the id <see cref="Core.Abstractions.IAdSpotStore.MarkReadyAsync"/>
    /// already confirmed against the spot's own row.</summary>
    public sealed record Landed(long MediaId) : AdPromotionOutcome;

    /// <summary>
    /// Promotion did not complete. <paramref name="Reason"/> is the failure detail the caller's own
    /// 500 response logs.
    /// </summary>
    /// <param name="FileMoved">
    /// Whether the ORIGINAL preview file has already been relocated off <c>AdSpot.PreviewPath</c> by
    /// the time this outcome was produced — <see langword="false"/> means nothing on disk moved
    /// (the caller's own preview stamps still point at a real file and are left exactly as they
    /// were), <see langword="true"/> means the file is gone from its stamped path (the caller must
    /// clear the stamps, since they now point at nothing). <see cref="AdRenderService.PromotePreviewAsync"/>
    /// itself never touches the filesystem a second time to answer this after the fact — it reports
    /// what it already knows from its own <c>File.Move</c> call, so the caller
    /// (<c>AdsController.Approve</c>) can decide without any filesystem access of its own beyond the
    /// staleness check it already performs.
    /// </param>
    public sealed record Failed(string Reason, bool FileMoved) : AdPromotionOutcome;
}
