// STORY-469 — Aired means recorded once (gh-#773 · SPEC F202 · PLAN T554)
//
// BDD specification — xUnit. RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body —
// remove the Skip only in the task that makes it green. Each Given comment names the arrange the scenario needs.

namespace GenWave.Host.Tests.Specs;

public static class FeatureAiredmeansrecordedonce
{
    const string Pending = "pending: T554 — Aired means recorded once (STORY-469)";

    public sealed class ScenarioAFullQueue
    {
        // Given: AnnouncementAiredEventSink with 10,000 queued signals

        /// <summary>AC1 — TryWrite returns true</summary>
        [Fact(Skip = Pending)]
        public void CannotRefuseASignal() => Assert.Fail(Pending);
    }

    public sealed class ScenarioAnAlreadyAiredRow
    {
        // Given: MarkAiredAsync twice for one id (db fixture)

        /// <summary>AC2 — aired_at unchanged</summary>
        [Fact(Skip = Pending)]
        public void IsIdempotent() => Assert.Fail(Pending);
    }

    public sealed class ScenarioTwoSignalsForOneId
    {
        // Given: the drain with a recording logger

        /// <summary>AC3 — no WARN</summary>
        [Fact(Skip = Pending)]
        public void StaysSilentOnADuplicate() => Assert.Fail(Pending);
    }

    public sealed class ScenarioAStoreThatBlinksOnce
    {
        // Given: throws on the first write, succeeds on the second; FakeTimeProvider

        /// <summary>AC4 — marked by t = 1 s</summary>
        [Fact(Skip = Pending)]
        public void RecoversOnTheFirstRetry() => Assert.Fail(Pending);
    }

    public sealed class ScenarioTheGuardianGrace
    {
        // Given: AnnouncementLifecycleGuardianService.ReArmGrace

        /// <summary>AC5 — 6 minutes</summary>
        [Fact(Skip = Pending)]
        public void LeavesTheGraceAlone() => Assert.Fail(Pending);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioAStoreThatKeepsFailing
    {
        // Given: throws four times; drain to t = 40 s; the guardian pass after

        /// <summary>AC6 — exactly one WARN with the id</summary>
        [Fact(Skip = Pending)]
        public void WarnsOnceOnExhaustion() => Assert.Fail(Pending);

        /// <summary>AC7 — no re-arm inside the grace</summary>
        [Fact(Skip = Pending)]
        public void NeverReArms() => Assert.Fail(Pending);
    }

}
