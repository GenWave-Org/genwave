// STORY-460 — The Orchestrator after (gh-#401 · SPEC F193 · PLAN T537)
//
// BDD specification — xUnit. AC1 reflects every public constructor in GenWave.Orchestration
// (Support/OrchestrationConstructorSeams.cs is the detector plus a named, dated, STORY-460-local
// baseline of what was already violating the rule when this fact went live — see that type's own
// remarks for why it is not wired through the shared L1-L11/ExemptionBaseline apparatus). AC2 reads
// the shared text scan (Support/ConstructorCallScan.cs, lifted here from
// Story451_ConstructionPins.cs) as one set-equality claim. AC3 reflects typeof(Orchestrator)
// directly (IBoundaryFitLog is internal to GenWave.Orchestration, so it is matched by interface
// FullName rather than typeof — Architecture.Tests has no InternalsVisibleTo there). AC4 drives one
// ceremony-only unit straight through OrchestratorBuilder + CapturingBreakPlanObserver, mirroring
// Story452_BreakCharacterisationReplay.cs's own O9 arrange (that file and Story452_PinnedTables.cs
// are FROZEN — read here, never edited) rather than text-matching the frozen replay's own pinned
// trace table, whose O9 line is byte-identical to O5's (an ordinary, non-ceremony-only unit) and
// would prove nothing on its own. AC5 substitutes the real, narrow SPEC F193.5 claim
// ("GenWave.Orchestration still references only Core + Abstractions") for a vacuous "the suite runs
// green" fact — the same substitution Story405_L10PackTables.cs's
// L10SweepsEveryProjectTheEpicTouched and Story431_L10Sponsor.cs's NoL1ToL10ViolationAppears both
// made for their own epics: L1/L5/L10/L11 are proven, every run, by their own spec files.

using System.Reflection;
using GenWave.Architecture.Tests.Support;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration;
using GenWave.TestSupport;
using GenWave.TestSupport.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Architecture.Tests.Specs;

public static class FeatureTheOrchestratorAfter
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioEveryPublicConstructorInOrchestration
    {
        // Given: every public constructor in GenWave.Orchestration, reflected
        // (Support/OrchestrationConstructorSeams.cs)

        /// <summary>AC1 — no parameter whose type is an interface carries a default value, beyond
        /// exactly the named, dated 2026-09-20 baseline (see OrchestrationConstructorSeams's own
        /// remarks). Set equality, not a one-directional Except: a genuinely NEW violation fails this
        /// (as always), and — round 2, PLAN T537 review finding F4 — so does a violation that gets
        /// fixed without its baseline entry being struck in the same change, closing the fail-open
        /// hole where a burnt-down entry could linger forever and silently re-forgive a reintroduced
        /// violation.</summary>
        [Fact]
        public void NoInterfaceParameterHasADefault()
        {
            var found = OrchestrationConstructorSeams.FindOptionalInterfaceParameters(
                ProductionAssemblies.Orchestration);

            Assert.Equal(OrchestrationConstructorSeams.KnownViolationsAsOf20260920, found);
        }
    }

    public sealed class ScenarioTheTextScanAfterTheSplit
    {
        // Given: the Orchestrator's own construction call, scanned over src/ and tests/
        // (Support/ConstructorCallScan.cs, shared with Story451_ConstructionPins.cs)

        /// <summary>AC2 — Story451_ConstructionPins.cs's own three facts (HitsOrchestratorBuilder +
        /// HitsTheServiceCollectionExtensions + HitsNothingElse, i.e. exactly two hits) already
        /// imply this set equality — a set of two containing both required members can be nothing
        /// else. This fact states AC2's own Given/When/Then directly, as one assertion, reading the
        /// SAME scan rather than re-deriving or restating Story451's three facts.</summary>
        [Fact]
        public void HitsExactlyTheBuilderAndTheRoot()
        {
            var hits = ConstructorCallScan.Hits
                .Select(Path.GetFileName)
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal);

            Assert.Equal(
                new HashSet<string>(StringComparer.Ordinal)
                {
                    "OrchestratorBuilder.cs",
                    "OrchestrationServiceCollectionExtensions.cs",
                },
                hits);
        }
    }

    public sealed class ScenarioTheOrchestratorReflected
    {
        // Given: typeof(Orchestrator)

        static readonly Type OrchestratorType = typeof(Orchestrator);

        /// <summary>AC3 — SPEC F193.3</summary>
        [Fact]
        public void ImplementsINextItemProvider() =>
            Assert.Contains(typeof(INextItemProvider), OrchestratorType.GetInterfaces());

        /// <summary>AC3 — SPEC F193.3. IBoundaryFitLog is internal to GenWave.Orchestration and
        /// Architecture.Tests carries no InternalsVisibleTo there, so it is matched by interface
        /// FullName rather than typeof.</summary>
        [Fact]
        public void ImplementsIBoundaryFitLog() =>
            Assert.Contains(
                OrchestratorType.GetInterfaces(),
                type => type.FullName == "GenWave.Orchestration.IBoundaryFitLog");

        /// <summary>AC3 — SPEC F193.3; Host reads this field (SPEC F142).</summary>
        [Fact]
        public void ExposesSignOffLeadTime() =>
            Assert.NotNull(OrchestratorType.GetField(
                nameof(Orchestrator.SignOffLeadTime), BindingFlags.Public | BindingFlags.Static));
    }

    public sealed class ScenarioTheCeremonyOnlyUnitsTrace
    {
        // Given: one ceremony-only unit driven directly through OrchestratorBuilder +
        // CapturingBreakPlanObserver, mirroring Story452_BreakCharacterisationReplay.cs's own O9
        // arrange (SPEC F124/gh-#300: a SignOff+SignOn pair queued ahead of a boundary the
        // queued-ahead tail already crosses) rather than reading the frozen replay's own pinned
        // trace table directly — O9's trace there is byte-identical to O5's (an ordinary,
        // non-ceremony-only unit), so a naive "find the SignOff trace" read of that table would not
        // actually prove ceremony-only.
        //
        // Round 2 (PLAN T537 review finding F1) — LeadInBeforeEachTrack is TRUE below, unlike the
        // frozen O9 arrange. Driven live: TryServeCeremonyOnlyUnitAsync's own decline check fires
        // BEFORE any music selection runs, so the ceremony-only unit plans no lead-in regardless of
        // cadence (SPEC F186.2(e) — "no crosstalk, no cadence enqueues, no lead-in, no track"). With
        // cadence asking for a lead-in, ListsNoLeadIn below only stays green because that decline
        // check genuinely fired; neuter it (fall through to the ordinary music path) and a lead-in
        // DOES appear, since cadence now asks for one on every unit. Round 1's arrange left
        // LeadInBeforeEachTrack false, under which no unit — ceremony or not — ever gets a lead-in,
        // so that fact passed for the wrong reason and proved nothing about the ceremony itself.
        // BackAnnounceAfterEachTrack stays FALSE, matching the frozen O9 arrange's own reason (see
        // that file's own O9 comment): a pending back-announce would air INSTEAD of the ceremony
        // being tested here, not alongside it.

        static async Task<BreakPlan> DriveTheCeremonyOnlyUnitAsync()
        {
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2030-01-01T00:00:00Z"));
            var handoff = new HandoffContext("af_flip", "Nova", "Milo");
            var queue = new SpeechDeferralQueue(clock);
            queue.Enqueue(
                SpeechDeferralKind.SignOff, "test: decline signoff",
                due: clock.GetUtcNow() + TimeSpan.FromSeconds(30), handoff: handoff);
            queue.Enqueue(
                SpeechDeferralKind.SignOn, "test: decline signon",
                due: clock.GetUtcNow() + TimeSpan.FromSeconds(45), handoff: handoff);

            var observer = new CapturingBreakPlanObserver();
            var chain = new OrchestratorBuilder()
                .WithTime(clock)
                .WithDeferralQueue(queue)
                .WithCadence(new CadenceConfig
                {
                    LeadInBeforeEachTrack = true,
                    BackAnnounceAfterEachTrack = false,
                    StationIdEveryNUnits = 0,
                })
                // gh-#300's own boundary-bias lookahead (Gh300_DeclineTheFinalUnit.cs's BuildOrchestrator):
                // the builder's own default is a zero-lookahead FakeBoundaryBiasProvider, under which
                // GetNextAsync's own fit stays null (untilDue never <= Current) and the decline path
                // never even looks at the queued SignOff — widening it is what actually lets this unit
                // reach TryServeCeremonyOnlyUnitAsync at all.
                .WithLookahead(TimeSpan.FromMinutes(10))
                .WithPlanObserver(observer)
                .Build();

            await chain.Orchestrator.GetNextAsync(
                new PlayoutContext([], QueuedAheadMs: 200_000), CancellationToken.None);

            return Assert.Single(observer.Plans);
        }

        /// <summary>AC4 — the decline serves the sign-off as the whole unit.</summary>
        [Fact]
        public async Task ListsTheSignOff()
        {
            var plan = await DriveTheCeremonyOnlyUnitAsync();

            Assert.Contains("SignOff", plan.ToTrace(), StringComparison.Ordinal);
        }

        /// <summary>AC4 — no lead-in is planned alongside it, even though cadence asks for one on
        /// every unit (see this scenario's own remarks, round 2 F1): the decline check in
        /// TryServeCeremonyOnlyUnitAsync fires before music selection ever runs, so lead-in never
        /// gets a music slot to attach to.</summary>
        [Fact]
        public async Task ListsNoLeadIn()
        {
            var plan = await DriveTheCeremonyOnlyUnitAsync();

            Assert.DoesNotContain("LeadIn", plan.ToTrace(), StringComparison.Ordinal);
        }

        /// <summary>AC4 (round 2, PLAN T537 review finding F1) — the unit draws nothing from the
        /// catalog: the whole plan is a single slot (SPEC F186.2(e) — "no track"; the frozen O9
        /// comment's own words, "drawing nothing from the catalog"). This is the assertion that
        /// actually makes this scenario mutation-sensitive: with the decline path neutered, the
        /// ordinary music path still drains the SAME queued SignOff into slot #1 (so ListsTheSignOff
        /// above stays green either way) but adds a second slot beside it — this fact alone goes red
        /// when that happens.</summary>
        [Fact]
        public async Task DrawsNothingFromTheCatalog()
        {
            var plan = await DriveTheCeremonyOnlyUnitAsync();

            Assert.Single(plan.Slots);
        }
    }

    public sealed class ScenarioTheLaws
    {
        // Given: SPEC F193.5's own narrow claim — GenWave.Orchestration references only Core +
        // Abstractions (L1, L5, L10, L11 themselves are proven, every run, by their own spec files;
        // this single fact proves none of them directly — see its own remarks).

        /// <summary>AC5 — replaces a vacuous "the suite runs green" claim (already covered by every
        /// other law's own theory data) with the one claim specific to this epic, the same
        /// substitution Story405_L10PackTables.cs's L10SweepsEveryProjectTheEpicTouched and
        /// Story431_L10Sponsor.cs's NoL1ToL10ViolationAppears both made for their own epics.</summary>
        [Fact]
        public void OrchestrationReferencesOnlyCoreAndAbstractions()
        {
            var forbidden = AssemblyReferenceScan.ForbiddenReferences(
                ProductionAssemblies.Orchestration.Location,
                name => name.StartsWith("GenWave.", StringComparison.Ordinal)
                    && name is not "GenWave.Core" and not "GenWave.Abstractions");

            Assert.Empty(forbidden);
        }
    }
}
