namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// L8's named, dated constant (SPEC F126.4, STORY-323, T274 review rider, PLAN T277):
/// <see cref="GenWave.Tts.PronunciationRuleResolver.ResolveForRender"/> is the ONE resolve seam for
/// air (<c>TtsSegmentSource</c>) and audition (<c>TtsPreviewController</c>) alike — no production code
/// OUTSIDE <c>GenWave.Tts</c> may call <c>PronunciationRuleSet.Merge</c>,
/// <c>PronunciationRuleSet.MergeWithProvenance</c>, or <c>PronunciationRuleProvider.BuildMerged</c>
/// directly. The T274 review proved WHY a law, not a behavioral fact, has to hold this seam shut: an
/// inverted-precedence hand-merge dropped into the controller (station rules layered over persona
/// rules instead of under) ran the whole solution green — parity between air and audition is
/// structural, not something any existing assertion can distinguish from a coincidence.
///
/// <b>Scope: OUTSIDE <c>GenWave.Tts</c> only.</b> <c>PronunciationRuleResolver</c> itself lives in
/// <c>GenWave.Tts</c> and calls <c>PronunciationRuleProvider.BuildMerged</c> and
/// <c>PronunciationRuleSet.Merge</c> internally — that IS the seam, not a violation of it. This law's
/// production fact therefore scopes its subjects to every production assembly EXCEPT
/// <c>GenWave.Tts</c> (<see cref="ProductionAssemblies.AllProductionAssemblies"/> minus
/// <see cref="ProductionAssemblies.Tts"/>) — GenWave.Tts internals are free to use the merge primitives
/// directly; only a caller reaching past the resolver from outside the module is forbidden.
///
/// <b>Detector.</b> <see cref="MemberCallSiteScan"/>, the same L7 uses — three forbidden signatures
/// instead of one, each with exactly one overload in play
/// (<see cref="ForbiddenMemberSignature.ParameterCount"/> is <see langword="null"/> for all three; no
/// arity disambiguation is needed the way <c>SynthesizeAsync</c>'s two overloads required). See
/// <see cref="MemberCallSiteScan"/>'s own remarks for the full boundary enumeration this law inherits.
///
/// <b>The one named exemption — deliberately per-(type, member), not per-type.</b>
/// <c>PronunciationsController.BuildRows</c> calls <c>PronunciationRuleSet.MergeWithProvenance</c>
/// directly, and that is BY DESIGN: that method exists for the DISPLAY projection
/// (<c>GET /api/pronunciations</c>, tagging each rule with its source and whether it is in effect),
/// never for matching — see <c>MergeWithProvenance</c>'s own remarks, including the T274 review
/// finding that reverted the one draft that tried to reuse it as a render seam. The
/// <see cref="MemberCallSiteExemption"/> below names exactly that one (type, member) pair, not the
/// whole controller: were <c>PronunciationsController</c> ever to also call <c>Merge</c> or
/// <c>BuildMerged</c> directly — genuinely reaching for a resolve seam rather than a display
/// projection — that call is NOT exempt and this law reds naming it, which is why
/// <see cref="MemberCallSiteScan"/> reports one violation per distinct (type, forbidden member) pair
/// rather than collapsing a type's hits to one (see its own remarks).
/// </summary>
internal static class PronunciationResolveSeam
{
    /// <summary>The three forbidden call targets — two on <c>PronunciationRuleSet</c>, one on
    /// <c>PronunciationRuleProvider</c>.</summary>
    public static readonly IReadOnlyList<ForbiddenMemberSignature> ForbiddenSignatures = new[]
    {
        new ForbiddenMemberSignature(
            DeclaringNamespace: "GenWave.Tts",
            DeclaringName: "PronunciationRuleSet",
            MemberName: "Merge",
            ParameterCount: null,
            Description: "PronunciationRuleSet.Merge"),
        new ForbiddenMemberSignature(
            DeclaringNamespace: "GenWave.Tts",
            DeclaringName: "PronunciationRuleSet",
            MemberName: "MergeWithProvenance",
            ParameterCount: null,
            Description: "PronunciationRuleSet.MergeWithProvenance"),
        new ForbiddenMemberSignature(
            DeclaringNamespace: "GenWave.Tts",
            DeclaringName: "PronunciationRuleProvider",
            MemberName: "BuildMerged",
            ParameterCount: null,
            Description: "PronunciationRuleProvider.BuildMerged"),
    };

    /// <summary>The one designed exemption — see the class remarks for why it is scoped to this exact
    /// (type, member) pair rather than the whole controller.</summary>
    public static readonly IReadOnlyList<MemberCallSiteExemption> DesignatedExemptions = new[]
    {
        new MemberCallSiteExemption("GenWave.Host.Api.PronunciationsController", "PronunciationRuleSet.MergeWithProvenance"),
    };

    /// <summary>Evaluates "no type in <paramref name="assemblyPaths"/> outside
    /// <see cref="DesignatedExemptions"/> calls any of <see cref="ForbiddenSignatures"/>", via the
    /// shared <see cref="MemberCallSiteScan.FindViolations"/>. <paramref name="assemblyPaths"/> is the
    /// caller's responsibility to scope to outside <c>GenWave.Tts</c> (see the class remarks).</summary>
    public static IReadOnlyList<LawViolation> FindViolations(IEnumerable<string> assemblyPaths) =>
        MemberCallSiteScan.FindViolations(
            assemblyPaths,
            ForbiddenSignatures,
            LawId.L8,
            (type, member) => DesignatedExemptions.Any(exemption => exemption.Matches(type, member)));
}
