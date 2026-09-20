using System.Reflection;

namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// SPEC F193.2 (STORY-460, PLAN T537) — every public constructor in
/// <see cref="ProductionAssemblies.Orchestration"/>, reflected: no parameter with a default value
/// whose type is an interface (an "optional seam"). Deliberately NOT wired through the shared
/// L1-L11/<see cref="ExemptionBaseline"/> apparatus: <c>Story290_DependencyLaws.cs</c>'s own
/// <c>ABaselinedViolationIsNamedAndDatedInTheTestItself</c> fact requires every
/// <see cref="ArchitectureExemption.LawId"/> already be a member of <see cref="LawId.All"/>, and
/// this rule is not one of CONTRIBUTING.md's numbered laws — giving it one would mean adding a
/// const to <see cref="LawId"/> and a matching CONTRIBUTING.md row, both outside this task's file
/// ownership (Story460's own spec, GenWave.Orchestration, and this Support/ folder). So this
/// baseline is local: same spirit as <see cref="ArchitectureExemption"/> (F105.2 — named, dated,
/// reasoned pre-existing debt) but its own small list, wired through nothing shared.
///
/// <para>
/// <see cref="KnownViolationsAsOf20260920"/> is exactly what <see cref="FindOptionalInterfaceParameters"/>
/// found already violating the rule the day PLAN T537 made this fact real — fixing all of them
/// (measured per-class call-site counts needing an edit, none of which T537 may touch: MusicSelectionPolicy
/// 47, ScheduleResolver 42, RollingPatterDurationEstimator 35, CachingScheduleResolver 25,
/// BreakRenderer 21, PersonaRanker 13, HandoffCeremonyProducer 11, ClockAnchoredImagingProducer 11,
/// BreakPlanner 10, Orchestrator 9, across GenWave.Orchestration.Tests and GenWave.TestSupport) is out
/// of this task's scope. A NEW optional-interface parameter added anywhere in the namespace — on
/// <see cref="GenWave.Orchestration.Orchestrator"/> itself or on a class with no prior violations —
/// still fails here immediately; only these named, dated pairs are forgiven.
/// </para>
///
/// <para>
/// <b>Round 2 (PLAN T537 review finding F4) — burn-down, not just growth-detection.</b> The fact
/// reading this list is <c>Assert.Equal(KnownViolationsAsOf20260920, found)</c> — set equality on two
/// already-sorted, already-distinct lists — not a one-directional <c>found.Except(baseline)</c>. That
/// means a genuinely NEW violation fails it (as before), but ALSO a violation fixed anywhere in
/// GenWave.Orchestration must have its entry struck from this list in the SAME change, or the fact
/// fails the other direction (baseline names an entry <see cref="FindOptionalInterfaceParameters"/> no
/// longer finds). A follow-up PLAN task should burn this list down the way gh-#406 burned down L2's
/// own debt rows (see <see cref="ExemptionBaseline"/>'s own remarks) — and this fact now enforces that
/// burn-down instead of silently re-forgiving a reintroduced violation forever.
/// </para>
/// </summary>
internal static class OrchestrationConstructorSeams
{
    /// <summary>Every <c>Type.Parameter</c> pair already violating SPEC F193.2 on 2026-09-20, when
    /// PLAN T537 made this pin real — one entry per (declaring type, parameter name), not one per
    /// class, so a currently-clean class (e.g. <c>BreakDelivery</c>, whose own optional-seam
    /// parameters PLAN T536 review finding F5 already removed) gets no forgiveness at all: any
    /// optional interface parameter it grows later still fails immediately. Sorted ordinal — required
    /// for the fact's sequence-equal comparison against <see cref="FindOptionalInterfaceParameters"/>'s
    /// own ordinal-sorted output (round 2, F4) to hold.</summary>
    public static readonly IReadOnlyList<string> KnownViolationsAsOf20260920 = new[]
    {
        "GenWave.Orchestration.BreakPlanner.adCadenceProvider",
        "GenWave.Orchestration.BreakPlanner.adSpotVend",
        "GenWave.Orchestration.BreakPlanner.announcementSource",
        "GenWave.Orchestration.BreakPlanner.catalog",
        "GenWave.Orchestration.BreakPlanner.contextSettings",
        "GenWave.Orchestration.BreakPlanner.imagingSettings",
        "GenWave.Orchestration.BreakPlanner.personaStore",
        "GenWave.Orchestration.BreakPlanner.speakerSnapshots",
        "GenWave.Orchestration.BreakPlanner.stationClock",
        "GenWave.Orchestration.BreakPlanner.voiceLister",
        "GenWave.Orchestration.BreakRenderer.announcementCopyWriter",
        "GenWave.Orchestration.BreakRenderer.announcementRenderer",
        "GenWave.Orchestration.CachingScheduleResolver.showStore",
        "GenWave.Orchestration.ClockAnchoredImagingProducer.stationClock",
        "GenWave.Orchestration.HandoffCeremonyProducer.personaStore",
        "GenWave.Orchestration.HandoffCeremonyProducer.speakerSnapshots",
        "GenWave.Orchestration.MusicSelectionPolicy.envelopeProvider",
        "GenWave.Orchestration.MusicSelectionPolicy.personaPickProvider",
        "GenWave.Orchestration.MusicSelectionPolicy.requestFulfillmentSource",
        "GenWave.Orchestration.Orchestrator.imagingSettings",
        "GenWave.Orchestration.Orchestrator.observer",
        "GenWave.Orchestration.Orchestrator.patterEstimator",
        "GenWave.Orchestration.PersonaRanker.stationClock",
        "GenWave.Orchestration.RollingPatterDurationEstimator.copyBounds",
        "GenWave.Orchestration.ScheduleResolver.stationClock",
    };

    /// <summary>Every <c>"DeclaringType.FullName.ParameterName"</c> pair, across every public
    /// instance constructor <paramref name="assembly"/> declares, whose parameter both has a
    /// default value and is typed as an interface — SPEC F193.2's own Given/When/Then, read
    /// directly off the compiled assembly rather than any one type at a time.</summary>
    public static IReadOnlyList<string> FindOptionalInterfaceParameters(Assembly assembly) =>
        (from type in LoadableTypes(assembly)
         from ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
         from parameter in ctor.GetParameters()
         where parameter.HasDefaultValue && parameter.ParameterType.IsInterface
         select $"{type.FullName}.{parameter.Name}")
        .Distinct(StringComparer.Ordinal)
        .OrderBy(entry => entry, StringComparer.Ordinal)
        .ToList();

    /// <summary><see cref="Assembly.GetTypes"/> throws <see cref="ReflectionTypeLoadException"/>
    /// whole-assembly on a single type's partial load failure; that exception's own
    /// <see cref="ReflectionTypeLoadException.Types"/> still carries every type that DID load (with a
    /// null slot per failure), so the scan degrades to "every loadable type" instead of throwing away
    /// the whole result. Deliberately not <see cref="Assembly.GetExportedTypes"/> — that would silently
    /// narrow the scan to public types only, past internal classes with public constructors SPEC
    /// F193.2 must still catch.</summary>
    static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }
}
