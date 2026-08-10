// STORY-317 — Dated specials shadow the grid (F120) — store half · 🪂 DROPPABLE SLICE
//
// BDD specification — xUnit, PENDING scaffold (planned 2026-08-10). Comment-bodied on
// purpose: db/36 + the specials repository land at T258 — and this slice is the epic's
// ONE ruled-droppable tail; dropping it removes these pendings with it. The resolver
// rung half lives in Orchestration.Tests/Story317_SpecialsRung.cs.

namespace GenWave.MediaLibrary.Tests.Specs;

using Xunit;

public static class FeatureSpecialsStore
{
    public sealed class ScenarioDatedRows
    {
        [Fact(Skip = "Pending (T258)")]
        public void ASpecialRoundTripsWithDateSpanPersonaShowEnvelope()
        {
            // Given a special for 2026-12-24 19:00–21:00 with persona/show/envelope
            // When  it is written and re-read
            // Then  every field round-trips; minutes obey the 30-min steps (F91 mirrored)
        }
    }

    public sealed class ScenarioRejectingOverlap
    {
        [Fact(Skip = "Pending (T258)")]
        public void OverlappingSpecialsOnADateAreRejectedByTheDatabase()
        {
            // Given a special already covering a span on a date
            // When  a second special overlaps it
            // Then  the per-date EXCLUDE guard rejects at the database (F120.1) —
            //       the weekly table's own invariant is untouched by construction
        }
    }
}
