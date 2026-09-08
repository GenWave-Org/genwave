// STORY-422 — One job at a time, waits for the station, cancellable (SPEC F174.2 · PLAN T441)

namespace GenWave.Host.Tests.Specs;

public static class FeatureOneJobAtATimeWaitsForTheStationCancellable
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioOneJobAtATimeSecondIsQueued
    {
        [Fact]
        public void BothEnqueuesAre202()
            => Assert.Fail("pending: T441 POST /api/ads/{id}/preview ×2 — AC1");

        [Fact]
        public void OnlyOneRowIsRunningAtAnyMoment()
            => Assert.Fail("pending: T441 single-slot channel — AC1");
    }

    public sealed class ScenarioTheRowSurfacesTheJobState
    {
        [Fact]
        public void GetCarriesJobKindStartedAtWaitingAndError()
            => Assert.Fail("pending: T441 GET /api/ads/{id} job object — AC4");
    }

    public sealed class ScenarioWaitingForKokoroShowsInTheRow
    {
        [Fact]
        public void WaitingForStationIsTrueWhileInFlight()
            => Assert.Fail("pending: T441 IOnAirRenderSignal.InFlight wait — AC5");

        [Fact]
        public void TheRenderProceedsOnceInFlightClears()
            => Assert.Fail("pending: T441 wait releases — AC5");
    }

    public sealed class ScenarioCancelClearsTheStamps
    {
        [Fact]
        public void DeleteJobIs204()
            => Assert.Fail("pending: T441 DELETE /api/ads/{id}/job — AC6");

        [Fact]
        public void TheRowShowsJobNull()
            => Assert.Fail("pending: T441 cancel clears stamps — AC6");

        [Fact]
        public void TheRunningJobsTokenWasCancelled()
            => Assert.Fail("pending: T441 cancel triggers the token — AC6");
    }

    public sealed class ScenarioTheRunnerLivesInGenWaveAds
    {
        [Fact]
        public void AdSpotJobServiceAssemblyIsGenWaveAds()
            => Assert.Fail("pending: T441 no new project — AC7");
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    public sealed class ScenarioRejectingWhenBusyOrFull
    {
        [Fact]
        public void ASecondJobOnTheSameSpotIs409AdJobBusy()
            => Assert.Fail("pending: T441 409 ad_job_busy — AC2");

        [Fact]
        public void AFullQueueIs429AdJobQueueFull()
            => Assert.Fail("pending: T441 429 ad_job_queue_full — AC3");
    }
}
