namespace GenWave.Ads;

/// <summary>
/// Outcome of <see cref="AdRenderService.RenderPreviewAsync"/> (SPEC F174.4; STORY-424; PLAN T442) —
/// mirrors <see cref="AdRenderOutcome"/>'s own closed-hierarchy posture for the preview render's own,
/// narrower contract: a preview never marks the spot <see cref="Core.Domain.AdState.Failed"/> — the
/// caller's own job stamps <c>job_error</c> instead (see <see cref="Failed"/>'s own remarks) — so this
/// carries the data both branches actually need rather than reusing an enum built for the state-mutating
/// write render.
/// </summary>
public abstract record AdPreviewOutcome
{
    AdPreviewOutcome() { }

    /// <summary>The preview rendered successfully. <paramref name="Path"/> is the file's own final,
    /// on-disk location (already moved into place under the preview root); <paramref name="Key"/> is
    /// the staleness key (<see cref="AdPreviewKey.Compute"/>) computed from the SAME stamped row the
    /// render itself used.</summary>
    public sealed record Rendered(string Path, string Key) : AdPreviewOutcome;

    /// <summary>No preview file was produced. <paramref name="Reason"/> is the failure detail the
    /// caller's own job stamps as <c>job_error</c> — never <c>fail_reason</c>: a preview failure never
    /// changes the spot's own <see cref="Core.Domain.AdState"/>.</summary>
    public sealed record Failed(string Reason) : AdPreviewOutcome;
}
