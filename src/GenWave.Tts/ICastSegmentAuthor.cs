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
}
