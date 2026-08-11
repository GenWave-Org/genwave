// STORY-317 — Dated specials shadow the grid (F120) — resolver-rung half · 🪂 DROPPABLE SLICE
//
// BDD specification — xUnit, PENDING scaffold (planned 2026-08-10). Comment-bodied on
// purpose: the specials-first rung lands at T258. Downstream consumers (ceremony, idents,
// stamps, spectator) are unchanged by construction — they read the resolver, which is
// exactly why the rung is the ONLY orchestration change this slice makes.

namespace GenWave.Orchestration.Tests.Specs;

using Xunit;

public static class FeatureSpecialsRung
{
    public sealed class ScenarioTheShadow
    {
        [Fact(Skip = "Pending (T258)")]
        public void TheResolverServesTheSpecialForItsSpan()
        {
            // Given a special covering 19:00–21:00 today over a differently-staffed weekly block
            // When  the resolver snapshot is read inside the span
            // Then  persona/show/envelope come from the special (specials-first rung, F120.2)
        }

        [Fact(Skip = "Pending (T258)")]
        public void DownstreamConsumersFollowWithZeroSpecialCasing()
        {
            // Given the special on the air
            // When  ceremony context, ident preference, and the booth stamp read identity
            // Then  all read the same snapshot — no consumer knows specials exist
        }
    }

    public sealed class ScenarioTheDayAfter
    {
        [Fact(Skip = "Pending (T258)")]
        public void TheWeeklyGridServesExactlyAsBefore()
        {
            // Given the special's date has passed
            // When  the same wall-clock span arrives next day/week
            // Then  the weekly grid resolves byte-identically to pre-special behavior
        }
    }
}
