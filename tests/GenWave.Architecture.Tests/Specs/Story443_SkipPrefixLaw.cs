// STORY-443 — Every skip says why, and the obsolete ones are gone (SPEC F182 · PLAN T505–T507)
//
// BDD specification — xUnit. AC1–AC4 drive SkipReasons.Classify; AC5 drives SkipReasons.Scan over a
// scratch file with a const reason; AC6–AC8 scan the real tests/ tree. AC9 (the audit table in the
// PR body) is review evidence — no spec.
//
// RED at plan time: SkipReasons is a throwing skeleton; 434 skips carry the old wording.

using GenWave.Architecture.Tests.Support;

namespace GenWave.Architecture.Tests.Specs;

public static class FeatureEverySkipSaysWhy
{
    const string PendingLaw = "pending: T505 — the skip-prefix law + SkipReasons scanner (STORY-443)";
    const string PendingRewrite = "pending: T506 — every existing skip rewritten onto the four prefixes (STORY-443)";
    const string PendingDelete = "pending: T507 — obsolete: and gate: facts deleted (STORY-443)";

    static string TestsRoot => Path.Combine(SolutionLocator.Root(), "tests");

    // ---------------------------------------------------------------------
    // HAPPY PATH — classification
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheFourPrefixesAreAllowed
    {
        [Fact(Skip = PendingLaw)]
        public void PendingWithATaskId() => Assert.Equal(SkipClass.Pending, SkipReasons.Classify("pending: T012 — see docs/PLAN.md"));

        [Fact(Skip = PendingLaw)]
        public void Manual() => Assert.Equal(SkipClass.Manual, SkipReasons.Classify("manual: LLL ear"));

        [Fact(Skip = PendingLaw)]
        public void Gate() => Assert.Equal(SkipClass.Gate, SkipReasons.Classify("gate: stack_gate.sh fresh leg"));

        [Fact(Skip = PendingLaw)]
        public void Obsolete() => Assert.Equal(SkipClass.Obsolete, SkipReasons.Classify("obsolete: T012 removed"));
    }

    public sealed class ScenarioAConstReasonResolvesThroughTheFile
    {
        readonly IReadOnlyList<SkipReason> scanned;

        public ScenarioAConstReasonResolvesThroughTheFile()
        {
            var root = Directory.CreateTempSubdirectory("story443-scan-").FullName;
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

        [Fact(Skip = PendingLaw)]
        public void TheFactIsAllowed() =>
            Assert.Equal(SkipClass.Pending, Assert.Single(scanned, s => s.Fact == "ViaConst").Class);
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
        [Fact(Skip = PendingLaw)]
        public void TheOldDeferredWording() =>
            Assert.Equal(SkipClass.Violation, SkipReasons.Classify("Deferred (operator-verified live, E9/E10)"));
    }

    public sealed class ScenarioPendingWithoutATaskIdIsAViolation
    {
        [Fact(Skip = PendingLaw)]
        public void PendingLater() => Assert.Equal(SkipClass.Violation, SkipReasons.Classify("pending: later"));
    }
}
