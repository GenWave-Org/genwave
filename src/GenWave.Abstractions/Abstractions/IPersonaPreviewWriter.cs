using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F35.6, STORY-123) — renders a segment's copy for the Admin UI's persona preview
/// WITHOUT the on-air fallback ladder <see cref="ISegmentCopyWriter"/> degrades through: a preview
/// that silently substituted template text on an LLM miss would misrepresent the persona being
/// auditioned, so a failure here is reported, never swallowed.
///
/// Deliberately a SEPARATE interface from <see cref="ISegmentCopyWriter"/> rather than a second
/// mode flag on it — the two ports have contradictory failure contracts (always-succeeds vs.
/// never-silently-degrades) and forcing them into one method signature would need a "is this a
/// preview?" branch at every call site. The production implementation
/// (<c>GenWave.Tts.LlmCopyWriter</c>) implements both interfaces over the SAME prompt-building
/// and copy-hygiene code, so the preview's text is provably what production would say.
/// </summary>
public interface IPersonaPreviewWriter
{
    /// <summary>
    /// Renders <paramref name="request"/>'s copy using <paramref name="personaOverride"/> — a saved
    /// persona, a draft persona built from unsaved fields, or <see langword="null"/> for the
    /// neutral house scaffold — in place of whatever persona is actually active on-air.
    ///
    /// <see cref="SegmentKind.StationId"/>/<see cref="SegmentKind.TimeDate"/> requests route
    /// straight to the template rung (mirrors production's own kind-based routing — those kinds
    /// never call the LLM on-air either, so this is not a fallback). LeadIn/BackAnnounce requests
    /// call the LLM and never degrade: any failure (disabled endpoint, timeout, non-2xx,
    /// empty/over-length copy) yields <see cref="PersonaPreviewResult.Failed"/> instead of
    /// substituting template text.
    /// </summary>
    Task<PersonaPreviewResult> WritePreviewAsync(
        SegmentRequest request, Persona? personaOverride, CancellationToken ct);
}
