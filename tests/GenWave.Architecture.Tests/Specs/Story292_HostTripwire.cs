// STORY-292 — The Host tripwire is armed and seeded (SPEC F105.1, F105.4 · PLAN T214)
using GenWave.Architecture.Tests.Support;

namespace GenWave.Architecture.Tests.Specs;

/// <summary>
/// Feature: law L5 — Host contains no namespace from the graduated/reserved subsystem
/// list, seeded with the F105.4 born-outside reservations (Context, Ads) so gh-#378 and
/// gh-#380 cannot quietly land in Host. Graduated list starts empty; each graduation
/// ruling extends the data. Built at T214: the detector (<see cref="HostNamespaceTripwire"/>) is
/// plain reflection over <c>Assembly.GetTypes()</c>, not ArchUnitNET or a metadata-table read — see
/// its own remarks for why L3's workaround (<see cref="HttpClientMetadataScan"/>) doesn't apply here.
///
/// T213's review carry-forward (N1) — <see cref="ProductionAssemblies.HasType"/> now resolving
/// member-level and assembly-level names, not just type full names — is a resolution-mechanism
/// change, not an L5 fact; its probe lives beside the mechanism's other resolution facts in
/// Story290_DependencyLaws.cs (<c>ScenarioHasTypeResolvesEveryGranularity</c>), not here.
/// </summary>
public sealed class FeatureHostTripwire
{
    public sealed class ScenarioTheMechanismAndTheSeed
    {
        [Fact]
        public void TodaysHostPassesWithTheSeededReservations()
        {
            var violations = HostNamespaceTripwire.FindViolations(
                ProductionAssemblies.Host.GetTypes(), HostReservedNamespaces.Entries);

            DependencyLawAssert.AssertNone(violations, ExemptionBaseline.Entries);
        }

        [Fact]
        public void TheReservedListIsDataAOneLineRulingExtends()
        {
            // Not a vacuous NotEmpty (the T212 seam-list lesson: a list fact that can't discriminate
            // proves nothing). Every entry carries the three fields a future graduation ruling needs
            // to extend the list with — universally checked as non-blank, not tied to today's exact
            // count (a future graduation ruling appends a line here without needing to touch this
            // fact too — that friction is exactly what "one-line ruling extends" promises NOT to
            // cost). The two SEEDED entries are checked by name and pinned to F105.4 specifically —
            // that citation is today's actual ruling, not a shape every future entry must share.
            foreach (var entry in HostReservedNamespaces.Entries)
            {
                Assert.StartsWith("GenWave.Host.", entry.ReservedNamespace, StringComparison.Ordinal);
                Assert.False(string.IsNullOrWhiteSpace(entry.RulingReference));
                Assert.False(string.IsNullOrWhiteSpace(entry.Reason));
            }

            var context = Assert.Single(HostReservedNamespaces.Entries, e => e.ReservedNamespace == "GenWave.Host.Context");
            Assert.Equal("F105.4", context.RulingReference);

            var ads = Assert.Single(HostReservedNamespaces.Entries, e => e.ReservedNamespace == "GenWave.Host.Ads");
            Assert.Equal("F105.4", ads.RulingReference);
        }
    }

    public sealed class ScenarioAReservedNamespaceInHostIsRed
    {
        // A probe-local reservation list, never HostReservedNamespaces itself — this proof stays
        // decoupled from Host's real, live type graph exactly the way L2/L3's own fixture probes stay
        // decoupled from MediaLibrary's/GenWave's real dependency graphs.
        private static readonly IReadOnlyList<HostNamespaceReservation> ProbeReservations = new[]
        {
            new HostNamespaceReservation(
                "GenWave.Architecture.Tests.Fixtures.L5Probe.ReservedHit",
                "F105.4",
                "probe-only stand-in for a born-outside reservation"),
        };

        private readonly IReadOnlyList<LawViolation> violations;

        public ScenarioAReservedNamespaceInHostIsRed()
        {
            var subjects = typeof(Fixtures.L5Probe.ReservedHit.ViolatesReservation).Assembly.GetTypes()
                .Where(t => t.Namespace is { } ns
                    && ns.StartsWith("GenWave.Architecture.Tests.Fixtures.L5Probe.", StringComparison.Ordinal));

            violations = HostNamespaceTripwire.FindViolations(subjects, ProbeReservations);
        }

        [Fact]
        public void ATypeUnderAReservedNamespaceFailsL5NamingTheNamespaceAndTheGraduationRule()
        {
            var violation = Assert.Single(
                violations, v => v.Member.EndsWith("ReservedHit.ViolatesReservation", StringComparison.Ordinal));
            Assert.Equal(LawId.L5, violation.LawId);

            var message = DependencyLawAssert.Format(violation);
            Assert.Contains("GenWave.Architecture.Tests.Fixtures.L5Probe.ReservedHit", message);
            Assert.Contains("graduation rule (F105.4)", message);
        }

        // The async-lambda fixture (mirrors L3's F1 review probe): a compiler-generated closure/
        // state-machine type landed under a reserved namespace is still caught and attributed back to
        // the ordinary type that declared it, not left invisible or reported under an unreadable
        // compiler-generated name.
        [Fact]
        public void TheAsyncLambdaClosureUnderAReservedNamespaceIsStillCaughtAndAttributed() =>
            Assert.Contains(violations, v => v.Member.EndsWith("ReservedHit.AsyncLambdaClosure", StringComparison.Ordinal));

        // The clean fixture, outside the reserved probe namespace, is never flagged — the rule doesn't
        // fail indiscriminately (the L2/L3 "clean elsewhere fixture" precedent).
        [Fact]
        public void TheCleanFixtureOutsideTheReservedNamespaceIsNeverFlagged() =>
            Assert.DoesNotContain(violations, v => v.Member.Contains("StaysClean"));

        [Fact]
        public void ExactlyTheTwoReservedFixturesAreFlagged() => Assert.Equal(2, violations.Count);

        // F1's same-prefix-lookalike proof: a namespace that merely STARTS WITH the reserved
        // namespace's text (no dot boundary) must never match — the exact hole a bare StartsWith
        // would open, closed by reusing AssemblyReferenceScan.HasFamilyPrefix instead of a
        // hand-rolled check (HostNamespaceTripwire's own remarks).
        [Fact]
        public void TheSamePrefixLookalikeNamespaceIsNeverFlagged() =>
            Assert.DoesNotContain(violations, v => v.Member.EndsWith("ReservedHitLike.StaysClean", StringComparison.Ordinal));
    }
}
