// STORY-454 — The plan is a value (gh-#401 · SPEC F187 · PLAN T520)
//
// BDD specification — xUnit. AC1/AC2 reflect the types; AC3–AC8 build plans by hand and read
// ordinals, traces, reservations and speakers. GREEN: no planner exists yet (T521), so AC7/AC8
// build a plan by hand from the F185.1/F186 script order — a valid arrange because it exercises
// the record's own shape, not the (not-yet-written) planner.

using System.Diagnostics;
using System.Reflection;
using GenWave.Core.Domain;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureThePlanIsAValue
{
    // ---------------------------------------------------------------------
    // Shared builders — one script-shaped plan, arranged once per fact.
    // ---------------------------------------------------------------------

    /// <summary>Kinds SPEC F186/F187.3 name as carrying no reservation.</summary>
    static readonly SegmentKind[] UnreservedKinds =
    [
        SegmentKind.BackAnnounce,
        SegmentKind.LeadIn,
        SegmentKind.SignOff,
        SegmentKind.SignOn,
        SegmentKind.TimeDate,
        SegmentKind.ContextSegment,
    ];

    static BreakContext MinimalContext() => new(
        Previous: null,
        Next: null,
        UnitDjName: "Nova",
        Cadence: new CadenceConfig(),
        Identity: new StationIdentity("station-1", "GenWave", "voice-1"),
        Now: DateTimeOffset.UnixEpoch,
        DrainAsOf: null,
        Hold: new HashSet<SpeechDeferralKind>(),
        QueuedAhead: TimeSpan.Zero);

    static SegmentRequest BuildRequest(SegmentKind kind, string? personaName = "Nova") => new(
        kind, "voice-1", "GenWave", Track: null, LocalNow: DateTimeOffset.UnixEpoch,
        StationId: "station-1", PersonaName: personaName);

    static MediaItem MakeItem(string mediaId, SegmentKind kind) =>
        new(mediaId, $"/media/{mediaId}.mp3", mediaId, new Loudness(-16.0, -1.0, true), SegmentKind: kind);

    static PlannedSlot RenderSlot(int ordinal, SegmentKind kind, Reservation? reservation = null, DropPolicy? drop = null) =>
        new(ordinal, kind, new RenderSource(BuildRequest(kind)), reservation, drop ?? new WarnDrop(), ObserveDuration: true);

    static PlannedSlot VerbatimSlot(int ordinal, SegmentKind kind, Reservation reservation) =>
        new(ordinal, kind, new VerbatimSource(BuildRequest(kind), new SegmentCopy($"{kind} copy", FreshPerAiring: false)), reservation, new WarnDrop(), ObserveDuration: true);

    static PlannedSlot ReadySlot(int ordinal, SegmentKind kind, string mediaId, Reservation reservation) =>
        new(ordinal, kind, new ReadySource(MakeItem(mediaId, kind)), reservation, new WarnDrop(), ObserveDuration: false);

    /// <summary>
    /// A full break, hand-built in kick order straight off the F186 table / T520's own dispatch
    /// list: BackAnnounce render, Crosstalk ready+Crosstalk res, Announcement verbatim+Announcement
    /// res, StationId ready+StationIdPool res, Ad ready+AdSpot res, Context render, TimeDate render,
    /// SignOff render, SignOn render, LeadIn render.
    /// </summary>
    static BreakPlan BuildScriptPlan()
    {
        PlannedSlot[] slots =
        [
            RenderSlot(1, SegmentKind.BackAnnounce),
            ReadySlot(2, SegmentKind.Crosstalk, "crosstalk-file", new Reservation(ReservationKind.Crosstalk, "crosstalk-file")),
            VerbatimSlot(3, SegmentKind.Announcement, new Reservation(ReservationKind.Announcement, "9001")),
            ReadySlot(4, SegmentKind.StationId, "pooled-station-id", new Reservation(ReservationKind.StationIdPool, "pooled-station-id")),
            ReadySlot(5, SegmentKind.Ad, "ad-spot-1", new Reservation(ReservationKind.AdSpot, "1")),
            RenderSlot(6, SegmentKind.ContextSegment),
            RenderSlot(7, SegmentKind.TimeDate),
            RenderSlot(8, SegmentKind.SignOff, drop: new WarnAndEventDrop()),
            RenderSlot(9, SegmentKind.SignOn, drop: new WarnAndEventDrop()),
            RenderSlot(10, SegmentKind.LeadIn),
        ];

        return new BreakPlan(1, MinimalContext(), TimeSpan.FromSeconds(5), slots);
    }

    /// <summary>The speaker a slot's source names, mirroring <see cref="BreakPlan.ToTrace"/>'s own
    /// interim rule (T520/T527): <see langword="null"/> for a Ready slot, which never has one.</summary>
    static string? PersonaNameOf(SlotSource source) => source switch
    {
        RenderSource render => render.Request.PersonaName,
        VerbatimSource verbatim => verbatim.Request.PersonaName,
        ReadySource => null,
        SlotSource => throw new UnreachableException($"Unhandled {nameof(SlotSource)} case: {source.GetType()}"),
    };

    /// <summary>The C# compiler emits a non-public "EqualityContract" property on every record — the
    /// reliable, reflection-visible marker that distinguishes a record from an ordinary sealed class.</summary>
    static bool IsRecord(Type type) =>
        type.GetProperty("EqualityContract", BindingFlags.NonPublic | BindingFlags.Instance) is not null;

    static void AssertIsAPublicSealedRecordIn(Type type, string ns) =>
        Assert.Equal(
            (IsPublic: true, IsSealed: true, IsRecord: true, Namespace: ns),
            (IsPublic: type.IsPublic, IsSealed: type.IsSealed, IsRecord: IsRecord(type), Namespace: type.Namespace));

    static void AssertHierarchyClosedWithinItsAssembly(Type baseType)
    {
        var assembly = baseType.Assembly;
        var subtypes = assembly.GetTypes().Where(t => t.IsSubclassOf(baseType));

        Assert.All(subtypes, t => Assert.True(t.IsSealed && t.Assembly == assembly));
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioThePlanTypesReflected
    {
        // Given: typeof(BreakPlan), PlannedSlot, Reservation, BreakContext

        /// <summary>AC1 — public, sealed, a record, in GenWave.Orchestration</summary>
        [Fact]
        public void BreakPlanIsAPublicSealedRecord() => AssertIsAPublicSealedRecordIn(typeof(BreakPlan), "GenWave.Orchestration");

        /// <summary>AC1 — public, sealed, a record, in GenWave.Orchestration</summary>
        [Fact]
        public void PlannedSlotIsAPublicSealedRecord() => AssertIsAPublicSealedRecordIn(typeof(PlannedSlot), "GenWave.Orchestration");

        /// <summary>AC1 — public, sealed, a record, in GenWave.Orchestration</summary>
        [Fact]
        public void ReservationIsAPublicSealedRecord() => AssertIsAPublicSealedRecordIn(typeof(Reservation), "GenWave.Orchestration");

        /// <summary>AC1 — public, sealed, a record, in GenWave.Orchestration</summary>
        [Fact]
        public void BreakContextIsAPublicSealedRecord() => AssertIsAPublicSealedRecordIn(typeof(BreakContext), "GenWave.Orchestration");
    }

    public sealed class ScenarioTheClosedHierarchies
    {
        // Given: every subtype of SlotSource and SlotOutcome in the assembly

        /// <summary>AC2 — Render, Verbatim, Ready</summary>
        [Fact]
        public void EverySlotSourceIsSealed() => AssertHierarchyClosedWithinItsAssembly(typeof(SlotSource));

        /// <summary>AC2 — Rendered, TimedOut, Failed, Abandoned</summary>
        [Fact]
        public void EverySlotOutcomeIsSealed() => AssertHierarchyClosedWithinItsAssembly(typeof(SlotOutcome));
    }

    public sealed class ScenarioAThreeSlotPlan
    {
        // Given: a plan with three slots

        /// <summary>AC3 — ordinals 1, 2, 3 in list order</summary>
        [Fact]
        public void NumbersThemFromOne()
        {
            var plan = new BreakPlan(1, MinimalContext(), TimeSpan.FromSeconds(5),
            [
                RenderSlot(1, SegmentKind.LeadIn),
                RenderSlot(2, SegmentKind.BackAnnounce),
                RenderSlot(3, SegmentKind.StationId),
            ]);

            Assert.Equal([1, 2, 3], plan.Slots.Select(s => s.Ordinal));
        }
    }

    public sealed class ScenarioARenderSlotTrace
    {
        // Given: Render slot LeadIn, speaker Ada, no reservation
        readonly BreakPlan plan = new(1, MinimalContext(), TimeSpan.FromSeconds(5),
            [new PlannedSlot(1, SegmentKind.LeadIn, new RenderSource(BuildRequest(SegmentKind.LeadIn, "Ada")), Reservation: null, Drop: new WarnDrop(), ObserveDuration: true)]);

        /// <summary>AC4 — #1 LeadIn render speaker=Ada res=-</summary>
        [Fact]
        public void PrintsTheLine() => Assert.Equal("#1 LeadIn render speaker=Ada res=-", plan.ToTrace());
    }

    public sealed class ScenarioAReservedReadySlotTrace
    {
        // Given: Ready slot Ad with reservation AdSpot:42
        readonly BreakPlan plan = new(1, MinimalContext(), TimeSpan.FromSeconds(5),
            [new PlannedSlot(1, SegmentKind.Ad, new ReadySource(MakeItem("any-ad-spot", SegmentKind.Ad)), new Reservation(ReservationKind.AdSpot, "42"), new WarnDrop(), ObserveDuration: false)]);

        /// <summary>AC5 — #1 Ad ready speaker=- res=AdSpot:42</summary>
        [Fact]
        public void PrintsTheLine() => Assert.Equal("#1 Ad ready speaker=- res=AdSpot:42", plan.ToTrace());
    }

    public sealed class ScenarioTheEmptyPlan
    {
        // Given: a plan with no slots
        readonly BreakPlan plan = new(1, MinimalContext(), TimeSpan.FromSeconds(5), []);

        /// <summary>AC6 — (empty)</summary>
        [Fact]
        public void PrintsEmpty() => Assert.Equal("(empty)", plan.ToTrace());
    }

    public sealed class ScenarioAPlanFromTheScript
    {
        // Given: the F185.1 break planned, hand-built (no planner exists yet, T521)
        readonly BreakPlan plan = BuildScriptPlan();

        /// <summary>AC7 — BackAnnounce, LeadIn, SignOff, SignOn, TimeDate, Context</summary>
        [Fact]
        public void UnclaimedKindsCarryNoReservation()
        {
            var unclaimed = plan.Slots.Where(s => UnreservedKinds.Contains(s.Kind));

            Assert.All(unclaimed, slot => Assert.Null(slot.Reservation));
        }

        /// <summary>AC7 — every other slot</summary>
        [Fact]
        public void ClaimedKindsCarryOne()
        {
            var claimed = plan.Slots.Where(s => !UnreservedKinds.Contains(s.Kind));

            Assert.All(claimed, slot => Assert.NotNull(slot.Reservation));
        }

        /// <summary>AC8 — Render and Verbatim</summary>
        [Fact]
        public void SpokenSlotsCarryASpeaker()
        {
            var spoken = plan.Slots.Where(s => s.Source is RenderSource or VerbatimSource);

            Assert.All(spoken, slot => Assert.NotNull(PersonaNameOf(slot.Source)));
        }

        /// <summary>AC8 — Ready</summary>
        [Fact]
        public void ReadySlotsCarryNone()
        {
            var ready = plan.Slots.Where(s => s.Source is ReadySource);

            Assert.All(ready, slot => Assert.Null(PersonaNameOf(slot.Source)));
        }
    }
}
