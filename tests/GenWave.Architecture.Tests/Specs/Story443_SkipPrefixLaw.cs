// STORY-443 — Every skip says why, and the obsolete ones are gone (SPEC F182.1 · PLAN T505–T507)
//
// BDD specification — xUnit. AC1–AC4 drive SkipReasons.Classify; AC5 drives SkipReasons.Scan over a
// scratch file with a const reason; AC6–AC8 scan the real tests/ tree. AC9 (the audit table in the
// PR body) is review evidence — no spec.
//
// LIVE at T505: the law itself (Classify + Scan, AC1–AC5) is green — LawId.L11. AC6 (zero
// violations) and AC7/AC8 (zero obsolete/gate facts remaining) stay `pending: T506`/`pending: T507`
// until the real skip population is rewritten onto the four prefixes and the obsolete/gate facts it
// surfaces are deleted.
//
// Three extra AC5-style scratch-file scenarios pin real edge cases the Roslyn-based scanner (see
// SkipReasons's own remarks) must not mishandle: a verbatim string that itself contains a raw-
// string-shaped quote run (the real Story173_SpectatorPage.cs site), a custom FactAttribute
// subclass that sets Skip in its own constructor rather than via a Skip= argument (the real
// Story324_RespellOracle.cs site), and two sibling nested classes that each declare their own
// `const string Skip` — a file-scoped (rather than lexically-scoped) const lookup resolves both to
// whichever class's const was declared last (the real pattern in, e.g.,
// Story062_EngineSafeSourceResilience.cs and Story133_AcceptanceGateOnAirMetadataFidelity.cs;
// build-loop review, round 2).

using GenWave.Architecture.Tests.Support;

namespace GenWave.Architecture.Tests.Specs;

public static class FeatureEverySkipSaysWhy
{
    const string PendingRewrite = "pending: T506 — every existing skip rewritten onto the four prefixes (STORY-443)";
    const string PendingDelete = "pending: T507 — obsolete: and gate: facts deleted (STORY-443)";

    static string TestsRoot => Path.Combine(SolutionLocator.Root(), "tests");

    // ---------------------------------------------------------------------
    // HAPPY PATH — classification
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheFourPrefixesAreAllowed
    {
        [Fact]
        public void PendingWithATaskId() => Assert.Equal(SkipClass.Pending, SkipReasons.Classify("pending: T012 — see docs/PLAN.md"));

        [Fact]
        public void Manual() => Assert.Equal(SkipClass.Manual, SkipReasons.Classify("manual: a person listening to the stream"));

        [Fact]
        public void Gate() => Assert.Equal(SkipClass.Gate, SkipReasons.Classify("gate: stack_gate.sh fresh leg"));

        [Fact]
        public void Obsolete() => Assert.Equal(SkipClass.Obsolete, SkipReasons.Classify("obsolete: T012 removed"));
    }

    public sealed class ScenarioAConstReasonResolvesThroughTheFile
    {
        readonly IReadOnlyList<SkipReason> scanned;

        public ScenarioAConstReasonResolvesThroughTheFile()
        {
            var root = Directory.CreateTempSubdirectory("story443-scan-").FullName;
            try
            {
                File.WriteAllText(Path.Combine(root, "Sample.cs"), """
                    public sealed class Sample
                    {
                        const string Pending = "pending: T012";

                        [Fact(Skip = Pending)]
                        public void ViaConst() { }
                    }
                    """);
                scanned = SkipReasons.Scan(root);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void TheFactIsAllowed() =>
            Assert.Equal(SkipClass.Pending, Assert.Single(scanned, s => s.Fact == "ViaConst").Class);
    }

    public sealed class ScenarioARawStringLookalikeInsideAStringDoesNotSwallowTheRestOfTheFile
    {
        readonly IReadOnlyList<SkipReason> scanned;

        public ScenarioARawStringLookalikeInsideAStringDoesNotSwallowTheRestOfTheFile()
        {
            var root = Directory.CreateTempSubdirectory("story443-scan-").FullName;
            try
            {
                // The verbatim literal @"""x" is a legal C# string (value: "x — an escaped leading
                // quote) whose text happens to LOOK like the start of a raw string literal. A text
                // scanner that misreads it as one swallows everything after it to EOF (build-loop review,
                // round 1, real site: Story173_SpectatorPage.cs's `@"""/api/"`).
                File.WriteAllText(Path.Combine(root, "Sample.cs"), """"
                    public sealed class Sample
                    {
                        const string Lookalike = @"""x";

                        [Fact(Skip = "pending: T012")]
                        public void AfterTheLookalike() { }
                    }
                    """");
                scanned = SkipReasons.Scan(root);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void TheFactAfterTheLookalikeIsStillFound() =>
            Assert.Equal(SkipClass.Pending, Assert.Single(scanned, s => s.Fact == "AfterTheLookalike").Class);
    }

    public sealed class ScenarioACustomFactAttributeSettingSkipInItsConstructorIsFound
    {
        readonly IReadOnlyList<SkipReason> scanned;

        public ScenarioACustomFactAttributeSettingSkipInItsConstructorIsFound()
        {
            var root = Directory.CreateTempSubdirectory("story443-scan-").FullName;
            try
            {
                // Real site: Story324_RespellOracle.cs's RequiresRealEspeakNgAttribute sets Skip
                // itself, from its own constructor, rather than via a Skip= attribute argument
                // (build-loop review, round 1).
                File.WriteAllText(Path.Combine(root, "Gated.cs"), """
                    public sealed class GatedAttribute : FactAttribute
                    {
                        public GatedAttribute()
                        {
                            Skip = "manual: needs a person";
                        }
                    }
                    """);
                scanned = SkipReasons.Scan(root);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void TheAttributeTypeItselfIsTheFact() =>
            Assert.Equal(SkipClass.Manual, Assert.Single(scanned, s => s.Fact == "GatedAttribute").Class);
    }

    public sealed class ScenarioSiblingNestedConstsDoNotLeakBetweenClasses
    {
        readonly IReadOnlyList<SkipReason> scanned;

        public ScenarioSiblingNestedConstsDoNotLeakBetweenClasses()
        {
            var root = Directory.CreateTempSubdirectory("story443-scan-").FullName;
            try
            {
                // Two sibling nested classes each declare their own `const string Skip`. A
                // file-scoped (rather than lexically-scoped) const lookup keys both consts by the
                // bare name "Skip" and lets whichever declaration comes last in the file win for
                // every usage — so ClassA's fact would wrongly report ClassB's reason (build-loop review,
                // round-2 finding; real sites: Story062_EngineSafeSourceResilience.cs,
                // Story133_AcceptanceGateOnAirMetadataFidelity.cs).
                File.WriteAllText(Path.Combine(root, "Siblings.cs"), """
                    public sealed class Container
                    {
                        public sealed class ClassA
                        {
                            const string Skip = "manual: a person listening to the stream";

                            [Fact(Skip = Skip)]
                            public void InA() { }
                        }

                        public sealed class ClassB
                        {
                            const string Skip = "gate: stack_gate.sh asserts it";

                            [Fact(Skip = Skip)]
                            public void InB() { }
                        }
                    }
                    """);
                scanned = SkipReasons.Scan(root);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void TheFactInClassAKeepsClassAsOwnReason() =>
            Assert.Equal(SkipClass.Manual, Assert.Single(scanned, s => s.Fact == "InA").Class);

        [Fact]
        public void TheFactInClassBKeepsClassBsOwnReason() =>
            Assert.Equal(SkipClass.Gate, Assert.Single(scanned, s => s.Fact == "InB").Class);
    }

    public sealed class ScenarioTheSolutionObeysTheLaw
    {
        readonly IReadOnlyList<SkipReason> all = SkipReasons.Scan(TestsRoot);

        [Fact(Skip = PendingRewrite)]
        public void ZeroViolations() =>
            Assert.Empty(all.Where(s => s.Class == SkipClass.Violation).Select(s => $"{s.File}: {s.Fact}: {s.Reason}"));

        [Fact(Skip = PendingDelete)]
        public void ZeroObsoleteFactsRemain() => Assert.DoesNotContain(all, s => s.Class == SkipClass.Obsolete);

        [Fact(Skip = PendingDelete)]
        public void ZeroGateFactsRemain() => Assert.DoesNotContain(all, s => s.Class == SkipClass.Gate);
    }

    // ---------------------------------------------------------------------
    // SAD PATH — violations
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnUnprefixedReasonIsAViolation
    {
        [Fact]
        public void TheOldDeferredWording() =>
            Assert.Equal(SkipClass.Violation, SkipReasons.Classify("Deferred (operator-verified live, E9/E10)"));
    }

    public sealed class ScenarioPendingWithoutATaskIdIsAViolation
    {
        [Fact]
        public void PendingLater() => Assert.Equal(SkipClass.Violation, SkipReasons.Classify("pending: later"));
    }
}
