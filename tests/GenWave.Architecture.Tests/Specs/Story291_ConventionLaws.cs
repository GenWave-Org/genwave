// STORY-291 — The convention laws run red-on-violation (SPEC F105.1 · PLAN T212, T213)
using System.Text.RegularExpressions;
using GenWave.Architecture.Tests.Support;

namespace GenWave.Architecture.Tests.Specs;

/// <summary>
/// Feature: the convention laws. L3 (HttpClient/handler-family construction only at designated client
/// seams — the SSRF-surface enumeration), L4's immutability half (no mutable public state in
/// Abstractions), L6 (Abstractions never references Core). L3 built at T212 (STORY-291 AC1, AC4's L3
/// half), moved off ArchUnitNET onto <see cref="HttpClientMetadataScan"/> at the T212 review (F1: an
/// ArchUnitNET-based scan cannot see a compiler-generated type nested inside another compiler-
/// generated type — every async lambda body — so it stayed green over a stray HttpClient inside one);
/// L4-immutability/L6 pending until T213.
///
/// AC4's ORIGINAL scope was "every law's violations run red and name the offender" for L3, L4, and
/// L6 alike — <see cref="ScenarioViolationsAreRedAndNamed"/> below is that proof, but only for L3.
/// There is no skipped placeholder fact standing in for L4/L6's half of AC4 (unlike
/// <see cref="ScenarioL4Immutability"/>/<see cref="ScenarioL6Direction"/>, which each carry an
/// explicit <c>[Fact(Skip = "pending — T213 ...")]</c> for the law itself) — T213 must add L4's and
/// L6's red-and-named proofs deliberately; their absence here is not itself a tracked pending marker.
/// </summary>
public sealed class FeatureConventionLaws
{
    public sealed class ScenarioL3HttpClientSeams
    {
        [Fact]
        public void EveryHttpClientConstructionSiteIsOnTheDesignatedSeamList()
        {
            var seamListed = new HashSet<string>(HttpClientSeams.DesignatedSeams, StringComparer.Ordinal);
            var assemblyPaths = ProductionAssemblies.AllProductionAssemblies().Select(a => a.Location);

            var violations = HttpClientSeams.FindViolations(assemblyPaths, seamListed.Contains);

            DependencyLawAssert.AssertNone(violations, ExemptionBaseline.Entries);
        }

        [Fact]
        public void TheSeamListIsANamedConstantInTheSuite() => Assert.NotEmpty(HttpClientSeams.DesignatedSeams);

        [Fact]
        public void EveryDesignatedSeamResolvesToARealProductionType()
        {
            // A deleted or typo'd DesignatedSeams entry would otherwise just match nothing and
            // silently stop excluding anything (proven at review: a phantom entry added zero
            // violations, not a red test) — the same disease ExemptionBaseline's own resolution
            // fact (Story290_DependencyLaws.cs) guards against, via the one shared mechanism.
            foreach (var seam in HttpClientSeams.DesignatedSeams)
            {
                Assert.True(
                    ProductionAssemblies.HasType(seam),
                    $"\"{seam}\" does not resolve to any loaded production type — a phantom seam " +
                    "entry would silently match nothing.");
            }
        }
    }

    public sealed class ScenarioL3ProgramCompositionRoot
    {
        // HttpClientMetadataScan sees THAT Program.cs builds a handler; it cannot see WHAT boolean
        // that handler was built with, nor distinguish "registered via AddHttpClient" from "hand-
        // rolled inline" by construction shape alone — a metadata scan already proves those
        // structurally, not textually. Pinned by source text instead — the one thing that can catch a
        // 4th registration, an AllowAutoRedirect=true regression, or a bypass-the-DI-container raw
        // client neither the metadata scan nor a type-count assertion would notice (STORY-291 review).
        private static string ReadProgramText()
        {
            var solutionRoot = Path.GetDirectoryName(SolutionLocator.Find())
                ?? throw new InvalidOperationException($"\"{SolutionLocator.Find()}\" has no containing directory.");
            return File.ReadAllText(Path.Combine(solutionRoot, "src", "GenWave.Host", "Program.cs"));
        }

        [Fact]
        public void ProgramRegistersExactlyThreeHttpClientsAndDisablesAutoRedirectOnTheNamedOne()
        {
            var programText = ReadProgramText();

            var registrationCount = Regex.Matches(programText, @"\bAddHttpClient(<[^>]+>)?\(").Count;
            Assert.Equal(3, registrationCount);

            // F90.2's no-redirect guarantee (CatalogProxyService's named client): a redirect response
            // is a fetch failure, never a hop this process takes.
            Assert.Contains("AllowAutoRedirect = false", programText, StringComparison.Ordinal);
        }

        [Fact]
        public void ProgramNeverHandRollsAClientOutsideTheThreeRegistrations() =>
            Assert.DoesNotContain("new HttpClient(", ReadProgramText());
    }

    public sealed class ScenarioL4Immutability
    {
        [Fact(Skip = "pending — T213 builds this (STORY-291 AC2)")]
        public void NoPublicTypeInAbstractionsCarriesMutablePublicState() => Assert.Fail("pending");
    }

    public sealed class ScenarioL6Direction
    {
        [Fact(Skip = "pending — T213 builds this (STORY-291 AC3)")]
        public void AbstractionsReferencesNoCoreType() => Assert.Fail("pending");
    }

    public sealed class ScenarioViolationsAreRedAndNamed
    {
        // ── L3 (HttpClientMetadataScan-based detector) ──────────────────────────────────────────
        private readonly IReadOnlyList<LawViolation> l3Violations;
        private readonly LawViolation l3Violation;
        private readonly string l3Message;

        public ScenarioViolationsAreRedAndNamed()
        {
            // Reuses HttpClientSeams.FindViolations — the exact function
            // EveryHttpClientConstructionSiteIsOnTheDesignatedSeamList runs above — scoped to a
            // fixture namespace within THIS SAME test assembly instead of the real production
            // assemblies, so this proof needs no production edit and stays stable regardless of the
            // codebase's current seam membership. "Scoped" here means the exclusion predicate, not a
            // separately loaded assembly: HttpClientMetadataScan reads compiled files directly, so
            // everything outside the probe's own namespace is simply excluded rather than never
            // loaded — exactly what the seam-listed provider excludes from the real subjects too.
            var fixtureAssemblyPath = typeof(Fixtures.L3Probe.SeamListed.CompliantHttpClientUser).Assembly.Location;

            static bool IsOutOfScopeOrSeamListed(string typeFullName) =>
                !typeFullName.StartsWith("GenWave.Architecture.Tests.Fixtures.L3Probe.", StringComparison.Ordinal)
                || typeFullName == "GenWave.Architecture.Tests.Fixtures.L3Probe.SeamListed.CompliantHttpClientUser";

            l3Violations = HttpClientSeams.FindViolations([fixtureAssemblyPath], IsOutOfScopeOrSeamListed);
            l3Violation = Assert.Single(l3Violations, v => v.Member.EndsWith("ViolatesSeamConfinement", StringComparison.Ordinal));
            l3Message = DependencyLawAssert.Format(l3Violation);
        }

        [Fact]
        public void TheL3ProbeFindsExactlyItsFourViolatingFixtures() => Assert.Equal(4, l3Violations.Count);

        [Fact]
        public void TheL3ViolationIsTaggedWithLawL3() => Assert.Equal(LawId.L3, l3Violation.LawId);

        [Fact]
        public void TheL3ViolationNamesTheOffendingType() => Assert.Equal(
            "GenWave.Architecture.Tests.Fixtures.L3Probe.Elsewhere.ViolatesSeamConfinement", l3Violation.Member);

        [Fact]
        public void TheL3ViolationDetailNamesHttpClient() => Assert.Contains("HttpClient", l3Violation.Detail);

        [Fact]
        public void TheFormattedL3MessageNamesTheLaw() => Assert.Contains(LawId.L3, l3Message);

        [Fact]
        public void TheFormattedL3MessageNamesTheOffendingType() =>
            Assert.Contains("ViolatesSeamConfinement", l3Message);

        // The async-lambda fixture (F1's exact review probe): an HttpClient constructed inside an
        // async lambda's compiler-generated, doubly-nested state machine is still attributed to the
        // ordinary type that declared it, not left invisible or misattributed to a compiler-generated
        // name.
        [Fact]
        public void TheAsyncLambdaFixtureIsCaughtAndAttributedToItsDeclaringType() =>
            Assert.Contains(l3Violations, v => v.Member.EndsWith("AsyncLambdaConstructsClient", StringComparison.Ordinal));

        // The handler-only and invoker-only fixtures (F4): a working outbound client built without
        // ever naming HttpClient itself still trips the widened forbid.
        [Fact]
        public void TheHandlerOnlyFixtureIsCaught() =>
            Assert.Contains(l3Violations, v => v.Member.EndsWith("HandlerOnlyConstruction", StringComparison.Ordinal)
                && v.Detail.Contains("SocketsHttpHandler", StringComparison.Ordinal));

        [Fact]
        public void TheInvokerOnlyFixtureIsCaught() =>
            Assert.Contains(l3Violations, v => v.Member.EndsWith("InvokerOnlyConstruction", StringComparison.Ordinal)
                && v.Detail.Contains("HttpMessageInvoker", StringComparison.Ordinal));

        // The seam-listed fixture is never evaluated (excluded from subjects, exactly as the real
        // seam list excludes its designated types) and the clean "elsewhere" fixture passes — the
        // rule doesn't fail indiscriminately.
        [Fact]
        public void TheSeamListedFixtureIsNeverFlagged() =>
            Assert.DoesNotContain(l3Violations, v => v.Member.Contains("CompliantHttpClientUser"));

        [Fact]
        public void TheCleanElsewhereFixtureIsNotFlagged() =>
            Assert.DoesNotContain(l3Violations, v => v.Member.Contains("StaysClean"));
    }
}
