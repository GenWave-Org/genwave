namespace GenWave.Tts;

using GenWave.Core.Domain;

/// <summary>
/// Seam over <see cref="CastSegmentAuthor"/> (SPEC F161.2, F161.3; STORY-391; PLAN T401 review F1) —
/// the <see cref="ISafeSegmentAuthor"/> precedent one sibling over: <c>GenWave.Ads.AdRenderService</c>
/// (the seam's one caller, across the project boundary) can be exercised in unit tests with a fake,
/// without spinning up the real render/mix/measure pipeline. <see cref="CastSegmentAuthor"/> remains
/// the sole production implementation.
/// </summary>
public interface ICastSegmentAuthor
{
    /// <inheritdoc cref="CastSegmentAuthor.AuthorAsync"/>
    Task<CastSegmentAuthorResult> AuthorAsync(
        CastAssemblyRequest assemblyRequest,
        Func<CrosstalkAssemblyResult.Assembled, AuthoredMediaInsert> buildInsert,
        Func<long, CancellationToken, Task<bool>> confirmAsync,
        CancellationToken ct);

    /// <summary>
    /// Renders and returns the file; lands nothing — SPEC F174.4 preview mode (PLAN T442). Unlike
    /// <see cref="AuthorAsync"/>, no catalog row is inserted and no confirmation delegate is called;
    /// the caller owns the resulting artifact's path entirely.
    /// </summary>
    Task<CrosstalkAssemblyResult> AssembleOnlyAsync(CastAssemblyRequest request, CancellationToken ct);

    /// <inheritdoc cref="CastSegmentAuthor.LandAsync"/>
    Task<CastSegmentAuthorResult> LandAsync(
        CrosstalkAssemblyResult.Assembled assembled,
        Func<CrosstalkAssemblyResult.Assembled, AuthoredMediaInsert> buildInsert,
        Func<long, CancellationToken, Task<bool>> confirmAsync,
        CancellationToken ct);

    /// <summary>
    /// Loudness/cue/duration for an EXISTING file at <paramref name="path"/> (SPEC F174.5; STORY-425;
    /// PLAN T445) — never a fresh ffmpeg invocation of this seam's own; the file was already produced
    /// (elsewhere, by <see cref="AssembleOnlyAsync"/>'s own earlier preview render) and this method
    /// only measures what is already on disk, so it can be handed to <see cref="LandAsync"/> exactly
    /// like a freshly-assembled artifact.
    /// </summary>
    Task<CrosstalkAssemblyResult.Assembled> MeasureAsync(string path, CancellationToken ct);
}
