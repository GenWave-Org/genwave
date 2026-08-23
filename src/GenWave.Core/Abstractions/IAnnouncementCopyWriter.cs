using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// The flavored half of an owner announcement's on-air delivery (SPEC F144.3/F144.4, STORY-358, PLAN
/// T342): asks the active on-air persona to work <c>message</c> into copy in character, and hands back
/// the result — or <see langword="null"/> for ANY failure (a disabled/unreachable LLM, a blown render
/// budget, or the F138.4 re-ask ladder exhausting on either a fabrication or the F144.3 containment
/// check). Mirrors <see cref="ISegmentCopyWriter"/>'s own never-throws-toward-the-caller contract, but
/// hands back a bare <see langword="string"/>? rather than a <see cref="SegmentCopy"/> — there is no
/// template rung to fall back to here (that is the CALLER's own job, THE FALLBACK LAW, F144.4): a
/// <see langword="null"/> result is the caller's signal to render the owner's message verbatim instead,
/// through the SAME <see cref="IVerbatimSegmentRenderer"/> a genuinely flavored result also renders
/// through — flavored copy IS exact once written, so nothing downstream needs to know which one it got.
/// </summary>
public interface IAnnouncementCopyWriter
{
    /// <summary>
    /// Returns flavored copy for <paramref name="message"/> in the persona voicing
    /// <paramref name="request"/>, or <see langword="null"/> on any failure (SPEC F144.4). Never
    /// throws except for the caller's own <paramref name="ct"/> cancellation.
    /// </summary>
    Task<string?> WriteAnnouncementAsync(SegmentRequest request, string message, CancellationToken ct);
}
