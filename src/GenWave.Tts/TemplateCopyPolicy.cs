namespace GenWave.Tts;

using GenWave.Core.Domain;

/// <summary>
/// The one rule for which kinds may NEVER air template copy (SPEC F92.4/F92.5/F107.6/F144.2/F144.4):
/// a handoff piece, a context segment or an owner announcement airs LLM-authored copy or nothing.
/// <see cref="TtsSegmentSource"/> drops a non-fresh copy of these kinds; <see cref="LlmCopyWriter"/>
/// records a template fallback (SPEC F218.1) only for the LLM-authored kinds this rule does NOT
/// cover, because those are the ones whose template line reaches the air.
/// </summary>
static class TemplateCopyPolicy
{
    public static bool NeverAirsTemplateCopy(SegmentKind kind) =>
        kind is SegmentKind.SignOff or SegmentKind.SignOn or SegmentKind.ContextSegment
            or SegmentKind.Announcement; // widened at T338 — see TtsSegmentSource for the F144.4 fallback law
}
