// STORY-323 — Two more laws join F105's table (SPEC F126.4 · PLAN T277)
using GenWave.Architecture.Tests.Support;

namespace GenWave.Architecture.Tests.Specs;

/// <summary>
/// Feature: L7 and L8, T277's pair. <c>GenWave.Host.Tests.Specs.Story323_AuditionsTellTheTruth</c>'s
/// own AC3 comment already names this file as the fitness law's one home — "deliberately lives in
/// GenWave.Architecture.Tests as T277, not in this file: one home per law, beside the F105 laws it
/// joins" — this feature IS that reconciliation; no pending placeholder existed anywhere to remove
/// (the AC3 comment already pointed here).
///
/// L7 (<see cref="TtsSynthesizeContextSeam"/>) closes the audit's TTS finding: T274
/// (<c>TtsPreviewController</c>) and T276 (<c>SafeSegmentAuthor</c>) each fixed one bypass of
/// <c>ITtsSynthesizer</c>'s context-aware <c>SynthesizeAsync</c> overload; this law makes a third
/// caller nobody has written yet unreachable from production code, rather than trusting review to
/// catch a fourth bypass by hand.
///
/// L8 (<see cref="PronunciationResolveSeam"/>) closes the T274 review rider: a draft of the preview
/// endpoint once hand-merged station-over-persona (inverted from the shipped persona-over-station
/// precedence) by calling <c>PronunciationRuleSet.Merge</c> directly instead of through
/// <c>PronunciationRuleResolver.ResolveForRender</c> — and the whole solution stayed green, because
/// no existing behavioral fact could tell "the resolver ran" apart from "a lookalike hand-merge ran and
/// happened to agree today". Only a structural law closes that gap for good.
///
/// <b>T277 review round 2:</b> the first draft's exemption lists were unverified — corrupting either
/// L7 relay's name still left the whole suite green, because nothing exercised the filter (neither
/// relay actually calls the forbidden overload; each only DEFINES it). <see cref="ScenarioExemptionsResolve"/>
/// and <see cref="ScenarioViolationsAreRedAndNamed"/> below close that gap: the first proves every
/// named exemption resolves to a real production type (a phantom/typo'd entry silently matches
/// nothing, exactly the T212 seam-list lesson every other law's own resolution fact already guards
/// against), the second proves — over fixtures built for the purpose, in
/// <c>Fixtures/MemberCallSiteProbe/</c> — that the shared <see cref="MemberCallSiteScan"/> and its
/// unified <see cref="MemberCallSiteExemption"/> shape actually discriminate an exempt caller from an
/// unlisted one, and a member-level exemption from a type-level one, rather than merely compiling.
///
/// <b>Mutation-verified at T277 adoption</b> (all temporary, all reverted before this task closed —
/// see the task's own report for the exact red output): (1) a temporary
/// <c>synthesizer.SynthesizeAsync(text, voice, ct)</c> call added to a Host controller turned L7 red,
/// naming that controller; (2) a temporary <c>PronunciationRuleProvider.BuildMerged(...)</c> call added
/// to <c>TtsPreviewController</c> turned L8 red, naming it; (3) corrupting one of
/// <see cref="TtsSynthesizeContextSeam.DesignatedRelays"/>'s names turned the round-2 resolution fact
/// red, naming the phantom entry.
/// </summary>
public sealed class FeatureAuditionSeamLaws
{
    public sealed class ScenarioTheFitnessLawHoldsTheSeamShut
    {
        [Fact]
        public void NoProductionCallSiteInvokesTheContextlessSynthesizeOverloadOutsideTheRelays()
        {
            var assemblyPaths = ProductionAssemblies.AllProductionAssemblies().Select(assembly => assembly.Location);

            var violations = TtsSynthesizeContextSeam.FindViolations(assemblyPaths);

            DependencyLawAssert.AssertNone(violations, ExemptionBaseline.Entries);
        }

        [Fact]
        public void NoHostProductionCodeReferencesThePronunciationMergeSeamOutsideTheResolver()
        {
            // Scoped to outside GenWave.Tts (the resolver's own home) — see
            // PronunciationResolveSeam's remarks for why GenWave.Tts internals are not subject to
            // this law at all, not merely exempted from it.
            var assemblyPaths = ProductionAssemblies.AllProductionAssemblies()
                .Where(assembly => assembly != ProductionAssemblies.Tts)
                .Select(assembly => assembly.Location);

            var violations = PronunciationResolveSeam.FindViolations(assemblyPaths);

            DependencyLawAssert.AssertNone(violations, ExemptionBaseline.Entries);
        }
    }

    public sealed class ScenarioExemptionsResolve
    {
        // One resolution fact serves both laws (T277 review finding 1+2) — the unified
        // MemberCallSiteExemption shape means L7's DesignatedRelays and L8's DesignatedExemptions are
        // the SAME element type, so one loop checks both lists the way Story291's
        // EveryDesignatedSeamResolvesToARealProductionType checks HttpClientSeams.DesignatedSeams.
        [Fact]
        public void EveryDesignatedExemptionResolvesToARealProductionType()
        {
            var exemptions = TtsSynthesizeContextSeam.DesignatedRelays.Concat(PronunciationResolveSeam.DesignatedExemptions);

            foreach (var exemption in exemptions)
            {
                Assert.True(
                    ProductionAssemblies.HasType(exemption.Type),
                    $"\"{exemption.Type}\" does not resolve to any loaded production type — a phantom " +
                    "exemption entry would silently match nothing.");
            }
        }
    }

    public sealed class ScenarioViolationsAreRedAndNamed
    {
        // Real forbidden signatures (ITtsSynthesizer's plain overload, plus PronunciationRuleSet's two
        // Merge overloads), exercised over the fixtures below rather than production code — proving the
        // SHARED MemberCallSiteScan mechanism (arity disambiguation, per-(type,member) exemption
        // granularity, the exemption filter itself) discriminates correctly, independent of today's
        // actual L7/L8 call graph. One probe serves both laws because both now share one detector.
        private static readonly IReadOnlyList<ForbiddenMemberSignature> ProbeForbiddenSignatures = new[]
        {
            TtsSynthesizeContextSeam.ForbiddenSignature,
            // .Single(by Description), not a positional index — reorder-proof against
            // PronunciationResolveSeam.ForbiddenSignatures ever changing element order.
            PronunciationResolveSeam.ForbiddenSignatures.Single(s => s.Description == "PronunciationRuleSet.Merge"),
            PronunciationResolveSeam.ForbiddenSignatures.Single(s => s.Description == "PronunciationRuleSet.MergeWithProvenance"),
        };

        private const string FixtureNamespacePrefix = "GenWave.Architecture.Tests.Fixtures.MemberCallSiteProbe.";

        private static readonly IReadOnlyList<MemberCallSiteExemption> ProbeExemptions = new[]
        {
            new MemberCallSiteExemption($"{FixtureNamespacePrefix}DesignatedRelayLike"),
            new MemberCallSiteExemption(
                $"{FixtureNamespacePrefix}PartiallyExemptCaller", "PronunciationRuleSet.MergeWithProvenance"),
        };

        private readonly IReadOnlyList<LawViolation> violations;

        public ScenarioViolationsAreRedAndNamed()
        {
            var fixtureAssemblyPath = typeof(Fixtures.MemberCallSiteProbe.DesignatedRelayLike).Assembly.Location;

            // Out-of-scope types (everything outside this probe's own fixture namespace — the rest of
            // this huge test project) count as exempt too, exactly the way Story291's L3 fixture probe
            // scopes itself — this scan otherwise runs over the WHOLE Architecture.Tests.dll.
            bool IsOutOfScopeOrExempt(string type, string member) =>
                !type.StartsWith(FixtureNamespacePrefix, StringComparison.Ordinal)
                || ProbeExemptions.Any(exemption => exemption.Matches(type, member));

            // "L7L8Probe" is a plain string, deliberately not a LawId const: LawId.All discovers every
            // law id by reflecting over LawId's own public const string fields (Story293's suite<->doc
            // parity source of truth) — a probe-only id living there would masquerade as a ninth real
            // law and desync CONTRIBUTING.md's table for no reason. This literal only ever labels a
            // LawViolation.LawId string this test itself reads back; it never needs to resolve to a
            // real law.
            violations = MemberCallSiteScan.FindViolations(
                [fixtureAssemblyPath], ProbeForbiddenSignatures, "L7L8Probe", IsOutOfScopeOrExempt);
        }

        [Fact]
        public void ExactlyTwoFixturesAreFlagged() =>
            Assert.Equal(2, violations.Select(v => v.Member).Distinct().Count());

        [Fact]
        public void TheDesignatedRelayLikeFixtureIsNeverFlagged() =>
            Assert.DoesNotContain(violations, v => v.Member.EndsWith("DesignatedRelayLike", StringComparison.Ordinal));

        [Fact]
        public void TheUndesignatedCallerFixtureIsFlaggedForThePlainOverload()
        {
            var violation = Assert.Single(violations, v => v.Member.EndsWith("UndesignatedCaller", StringComparison.Ordinal));
            Assert.Equal("L7L8Probe", violation.LawId);
            Assert.Equal(
                "references ITtsSynthesizer.SynthesizeAsync(string, string, CancellationToken) directly",
                violation.Detail);
        }

        [Fact]
        public void ThePartiallyExemptCallerIsFlaggedOnlyForItsNonExemptMember()
        {
            var violation = Assert.Single(violations, v => v.Member.EndsWith("PartiallyExemptCaller", StringComparison.Ordinal));
            Assert.Equal("references PronunciationRuleSet.Merge directly", violation.Detail);
        }

        [Fact]
        public void TheContextOverloadNearMissFixtureIsNeverFlagged() =>
            Assert.DoesNotContain(violations, v => v.Member.EndsWith("CallsOnlyTheContextOverload", StringComparison.Ordinal));

        [Fact]
        public void TheCleanFixtureIsNeverFlagged() =>
            Assert.DoesNotContain(violations, v => v.Member.EndsWith("StaysClean", StringComparison.Ordinal));
    }
}
