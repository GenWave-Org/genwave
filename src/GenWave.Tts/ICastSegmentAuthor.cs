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
}
