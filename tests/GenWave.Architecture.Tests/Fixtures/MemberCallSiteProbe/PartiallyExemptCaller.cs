// Fixture type for T277 review's self-exercising probe (Story323_FitnessLawsHoldTheSeamsShut.cs).
// Never wired into any DI container or call path.

namespace GenWave.Architecture.Tests.Fixtures.MemberCallSiteProbe;

using GenWave.Tts;

/// <summary>Calls TWO distinct forbidden members off the SAME type — <c>MergeWithProvenance</c> (named
/// in the probe's exemption list at (type, member) granularity) and <c>Merge</c> (named nowhere) —
/// proving a per-member exemption never widens to "this whole type is clear". Mirrors L8's real shape:
/// <c>PronunciationsController</c> is exempt for <c>MergeWithProvenance</c>'s display projection alone,
/// never for <c>Merge</c> itself.</summary>
public sealed class PartiallyExemptCaller
{
    public IReadOnlyList<MergedPronunciationRule> CallTheExemptMember(PronunciationRuleSet station, PronunciationRuleSet card) =>
        PronunciationRuleSet.MergeWithProvenance(station, card);

    public PronunciationRuleSet CallTheNonExemptMember(PronunciationRuleSet station, PronunciationRuleSet card) =>
        PronunciationRuleSet.Merge(station, card);
}
