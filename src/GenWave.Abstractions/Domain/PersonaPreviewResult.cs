namespace GenWave.Core.Domain;

/// <summary>
/// Outcome of <see cref="Abstractions.IPersonaPreviewWriter.WritePreviewAsync"/> (SPEC F35.6).
/// Mirrors <see cref="PersonaWriteResult"/>'s closed-hierarchy shape. Unlike
/// <see cref="Abstractions.ISegmentCopyWriter"/>'s always-succeeds contract, a preview NEVER
/// substitutes template copy for a failed LLM call — that would misrepresent the persona being
/// previewed — so an LLM miss is <see cref="Failed"/>, not a quiet template <see cref="Success"/>.
/// </summary>
public abstract record PersonaPreviewResult
{
    private PersonaPreviewResult() { }

    /// <summary>
    /// The rendered copy, ready to display. Carries either genuine LLM output or — for
    /// <see cref="SegmentKind.StationId"/>/<see cref="SegmentKind.TimeDate"/>, which never touch the
    /// LLM even on-air — the exact template text production would air for that kind.
    /// </summary>
    public sealed record Success(string Text) : PersonaPreviewResult;

    /// <summary>
    /// The LLM could not produce usable copy. <see cref="Detail"/> is safe to surface to the caller
    /// (no raw exception text/stack) — the controller maps this straight to a 502 ProblemDetails.
    /// </summary>
    public sealed record Failed(string Detail) : PersonaPreviewResult;

    /// <summary>
    /// The LLM's single-flight gate (SPEC F69.6) was still held by an on-air render when the
    /// preview's bounded queue wait (<c>Llm:PreviewQueueWaitSeconds</c>) expired. Nothing was
    /// attempted and nothing failed — the interactive caller is declined fast instead of queueing
    /// unboundedly behind a render-ahead burst; the controller maps this to a 503 with Retry-After.
    /// </summary>
    public sealed record Busy : PersonaPreviewResult;
}
