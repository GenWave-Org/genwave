// STORY-291 — The convention laws run red-on-violation (SPEC F105.1 · PLAN T212, T213)
using System.Text.RegularExpressions;
using GenWave.Architecture.Tests.Support;

namespace GenWave.Architecture.Tests.Specs;

/// <summary>
/// Feature: the convention laws. L3 (HttpClient/handler-family construction only at designated client
/// seams — the SSRF-surface enumeration), L4-immutability (no mutable public state in Abstractions),
/// L6 (Abstractions never references Core). L3 built at T212 (STORY-291 AC1, AC4's L3 half), moved off
/// ArchUnitNET onto <see cref="HttpClientMetadataScan"/> at the T212 review (F1: an ArchUnitNET-based
/// scan cannot see a compiler-generated type nested inside another compiler-generated type — every
/// async lambda body — so it stayed green over a stray HttpClient inside one).
///
/// L4-immutability and L6 built at T213 (AC2, AC3). L4-immutability
/// (<see cref="AbstractionsImmutability"/>) is plain reflection over the assembly's exported
/// types — a member-shape question (is this accessor <c>init</c> or an ordinary <c>set</c>?), not a
/// dependency-graph one, so ArchUnitNET buys nothing here that <see cref="System.Reflection"/>
/// doesn't already answer directly. L6 (<see cref="ScenarioL6Direction"/>) reuses L1's exact detector
/// primitive (<see cref="AssemblyReferenceScan"/>'s direct AssemblyRef-table read), scoped to one
/// literal assembly name (<c>GenWave.Core</c>) instead of a family prefix.
///
/// <b>L6's relationship to L4-references (Story290_DependencyLaws.cs).</b> L6 is deliberately NOT
/// layered on top of it — they read different metadata and therefore cover different bypasses.
/// For the REALISTIC violation shape a contributor would actually write — a
/// <c>ProjectReference</c> from Abstractions to Core — <c>ScenarioL4ReferenceHygiene
/// .AbstractionsReferencesNothingBeyondTheBcl</c> already catches it too: a <c>ProjectReference</c>
/// is restored through the same NuGet graph <see cref="DepsJsonDependencyScan"/> reads, confirmed by
/// GenWave.Core's OWN build-output deps.json, which lists its <c>ProjectReference</c> to Abstractions
/// as a library entry exactly like a package would (T213 review; a live Abstractions→Core
/// <c>ProjectReference</c> could not itself be added to PROVE this by mutation — Core already
/// references Abstractions, so the reverse edge is a circular dependency MSBuild refuses to restore
/// at all, itself a stronger guarantee than any test could add). But L6's own detector
/// (<see cref="AssemblyReferenceScan"/>, the compiled DLL's raw AssemblyRef table) sees a DIFFERENT,
/// NARROWER thing than L4-references' deps.json library-map read — and a live T213 mutation-check
/// proved the gap is real, not theoretical: a raw <c>&lt;Reference HintPath="...GenWave.Core.dll"&gt;</c>
/// (bypassing NuGet/project restore entirely, unlike a <c>PackageReference</c>/<c>ProjectReference</c>)
/// compiles a genuine AssemblyRef to <c>GenWave.Core</c> into Abstractions.dll, yet never appears in
/// deps.json's "libraries" map at all (that map is populated from the restore graph, not the raw
/// references list) — L6 reds naming <c>GenWave.Core</c>, L4-references stays fully green. So L6 is
/// not redundant messaging sugar over L4-references; it independently closes a real coverage gap
/// (a hand-added file reference) alongside being the sharper, more teachable message for the
/// ordinary case both catch — naming <c>GenWave.Core</c> specifically points straight at the
/// seam-placement criterion (ARCHITECTURE.md gh-#400 — "does a third-party module need to
/// implement/consume this? -> Abstractions; else -> Core/Abstractions") a stray reference would
/// actually be breaking, where "depends on non-BCL package" does not.
///
/// AC4's ORIGINAL scope was "every law's violations run red and name the offender" for L3, L4, and
/// L6 alike — <see cref="ScenarioViolationsAreRedAndNamed"/> below is that proof for all three laws
/// as of T213 (L4-immutability and L6 added alongside L3's own T212 proof).
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
        [Fact]
        public void NoPublicTypeInAbstractionsCarriesMutablePublicState()
        {
            var violations = AbstractionsImmutability.FindViolations(ProductionAssemblies.Abstractions.GetExportedTypes());

            DependencyLawAssert.AssertNone(violations, ExemptionBaseline.Entries);
        }
    }

    public sealed class ScenarioL6Direction
    {
        [Fact]
        public void AbstractionsReferencesNoCoreType()
        {
            // See the class doc's "L6's relationship to L4-references" section: this is deliberately
            // its own assertion, not a helper layered on ScenarioL4ReferenceHygiene — it reads the
            // compiled DLL's own AssemblyRef table directly, so it also catches a raw file reference
            // that bypasses the NuGet/project restore graph L4-references' deps.json read relies on
            // (verified by mutation at T213 review), on top of producing a sharper failure message
            // than "depends on a non-BCL package" for the ordinary ProjectReference case both catch.
            var forbiddenReferences = AssemblyReferenceScan.ForbiddenReferences(
                ProductionAssemblies.Abstractions.Location, name => name == "GenWave.Core");

            var violations = forbiddenReferences
                .Select(reference => new LawViolation(
                    LawId.L6,
                    "GenWave.Abstractions",
                    $"references \"{reference}\" — the seam-placement criterion (gh-#400) confines " +
                    "Abstractions to demonstrated third-party need; a Core reference belongs in Core itself"))
                .ToList();

            DependencyLawAssert.AssertNone(violations, ExemptionBaseline.Entries);
        }
    }

    public sealed class ScenarioViolationsAreRedAndNamed
    {
        // ── L3 (HttpClientMetadataScan-based detector) ──────────────────────────────────────────
        private readonly IReadOnlyList<LawViolation> l3Violations;
        private readonly LawViolation l3Violation;
        private readonly string l3Message;

        // ── L4-immutability (reflection-based AbstractionsImmutability) ─────────────────────────
        private readonly IReadOnlyList<LawViolation> l4ImmutabilityViolations;
        private readonly string l4ImmutabilityMessage;

        // ── L6 (AssemblyReferenceScan, scoped to one literal assembly name) ─────────────────────
        private readonly IReadOnlyList<string> l6MatchedOnACoreReference;
        private readonly IReadOnlyList<string> l6MatchedOnANameItNeverReferences;
        private readonly LawViolation l6Violation;
        private readonly string l6Message;

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

            // L4-immutability's probe target: the five fixtures under Fixtures/L4ImmutabilityProbe —
            // never GenWave.Abstractions' real, live types — covering both violating shapes (a
            // settable property, a mutable field) and every false-positive edge the T213 design
            // notes call out (a clean init-only record, a const/static-readonly holder shaped like
            // the real MoodVocabulary, and an enum).
            l4ImmutabilityViolations = AbstractionsImmutability.FindViolations(
            [
                typeof(Fixtures.L4ImmutabilityProbe.MutablePropertyFixture),
                typeof(Fixtures.L4ImmutabilityProbe.MutableFieldFixture),
                typeof(Fixtures.L4ImmutabilityProbe.CleanRecordFixture),
                typeof(Fixtures.L4ImmutabilityProbe.CleanConstAndStaticReadonlyFixture),
                typeof(Fixtures.L4ImmutabilityProbe.CleanEnumFixture),
            ]);
            l4ImmutabilityMessage = DependencyLawAssert.Format(
                l4ImmutabilityViolations.Single(v => v.Member.Contains("MutablePropertyFixture", StringComparison.Ordinal)));

            // L6's probe target: this same test project's OWN compiled assembly — it genuinely
            // ProjectReferences GenWave.Core (ProductionAssemblies.cs's own Core anchor,
            // `typeof(GenWave.Core.Abstractions.IScheduleStore)`, forces the reference to actually be
            // emitted, not merely declared in the csproj) — never GenWave.Abstractions' real dll, so
            // this proof is decoupled from Abstractions' live reference graph exactly the way L1's
            // own probe below (ArchUnitNET.dll) is decoupled from GenWave's.
            var l6ProbeAssembly = typeof(FeatureConventionLaws).Assembly.Location;
            l6MatchedOnACoreReference = AssemblyReferenceScan.ForbiddenReferences(
                l6ProbeAssembly, name => name == "GenWave.Core");
            l6MatchedOnANameItNeverReferences = AssemblyReferenceScan.ForbiddenReferences(
                l6ProbeAssembly, name => name == "GenWave.NoSuchAssembly");
            l6Violation = new LawViolation(
                LawId.L6, "Probe.TestAssembly", $"references \"{l6MatchedOnACoreReference[0]}\"");
            l6Message = DependencyLawAssert.Format(l6Violation);
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

        [Fact]
        public void TheL4ImmutabilityProbeFindsExactlyItsTwoViolatingFixtures() =>
            Assert.Equal(2, l4ImmutabilityViolations.Count);

        [Fact]
        public void TheL4ImmutabilityViolationsAreTaggedWithLawL4Immutability() =>
            Assert.All(l4ImmutabilityViolations, v => Assert.Equal(LawId.L4Immutability, v.LawId));

        [Fact]
        public void TheL4ImmutabilityViolationNamesTheOffendingTypeAndProperty() =>
            Assert.Contains(l4ImmutabilityViolations, v => v.Member ==
                "GenWave.Architecture.Tests.Fixtures.L4ImmutabilityProbe.MutablePropertyFixture.Name");

        [Fact]
        public void TheL4ImmutabilityViolationNamesTheOffendingTypeAndField() =>
            Assert.Contains(l4ImmutabilityViolations, v => v.Member ==
                "GenWave.Architecture.Tests.Fixtures.L4ImmutabilityProbe.MutableFieldFixture.Count");

        [Fact]
        public void TheFormattedL4ImmutabilityMessageNamesTheLaw() =>
            Assert.Contains(LawId.L4Immutability, l4ImmutabilityMessage);

        [Fact]
        public void TheFormattedL4ImmutabilityMessageNamesTheOffendingMember() =>
            Assert.Contains("MutablePropertyFixture.Name", l4ImmutabilityMessage);

        // The clean record, the const/static-readonly holder, and the enum each exercise one
        // false-positive edge the T213 design notes name explicitly — none may appear among the
        // violations.
        [Fact]
        public void TheCleanRecordFixtureIsNotFlagged() =>
            Assert.DoesNotContain(l4ImmutabilityViolations, v => v.Member.Contains("CleanRecordFixture"));

        [Fact]
        public void TheConstAndStaticReadonlyHolderIsNotFlagged() =>
            Assert.DoesNotContain(l4ImmutabilityViolations, v => v.Member.Contains("CleanConstAndStaticReadonlyFixture"));

        [Fact]
        public void TheEnumFixtureIsNotFlagged() =>
            Assert.DoesNotContain(l4ImmutabilityViolations, v => v.Member.Contains("CleanEnumFixture"));

        [Fact]
        public void TheL6ProbeFindsTheKnownCoreReference() =>
            Assert.Equal(new[] { "GenWave.Core" }, l6MatchedOnACoreReference);

        [Fact]
        public void TheL6ProbeFindsNothingForANameItNeverReferences() =>
            Assert.Empty(l6MatchedOnANameItNeverReferences);

        [Fact]
        public void TheL6ViolationIsTaggedWithLawL6() => Assert.Equal(LawId.L6, l6Violation.LawId);

        [Fact]
        public void TheFormattedL6MessageNamesTheLawAndCore() =>
            Assert.Equal("[L6] Probe.TestAssembly: references \"GenWave.Core\"", l6Message);
    }
}
