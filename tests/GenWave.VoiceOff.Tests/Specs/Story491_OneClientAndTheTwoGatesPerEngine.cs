// STORY-491 — One client, and the two gates per engine (gh-#614 · SPEC F214.1, F214.5, F214.6, F214.16 · PLAN T608, T611)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-10-21); the named task
// activates its facts, watches them fail, then makes them pass.

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureOneClientAndTheTwoGatesPerEngine
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — the profile shape loads
    public sealed class ScenarioTheProfileShapeLoads
    {
        // Given engines/kokoro.json with slug, endpoint, tiers, voices per register, markup, license, baseline:true
        // When  the profile set is loaded

        [Fact(Skip = "pending: T608 — see docs/PLAN.md")]
        public void KokoroIsTheSingleBaseline() => Assert.Fail("pending: T608 — Kokoro is the single baseline");
    }

    // AC2 — the client speaks the contract
    public sealed class ScenarioTheClientSpeaksTheContract
    {
        // Given a stub server answering POST /v1/audio/speech
        // When  the client renders "hello there" with voice v1 at speed 1.0

        [Fact(Skip = "pending: T608 — see docs/PLAN.md")]
        public void TheRequestBodyIsInputVoiceSpeedResponseFormatWav() => Assert.Fail("pending: T608 — the request body is {input, voice, speed, response_format:'wav'}");

        [Fact(Skip = "pending: T608 — see docs/PLAN.md")]
        public void TheBytesReturnedAreTheStubS() => Assert.Fail("pending: T608 — the bytes returned are the stub's");
    }

    // AC3 — the client times the render
    public sealed class ScenarioTheClientTimesTheRender
    {
        // Given the stub answers after a known delay
        // When  the client renders a line

        [Fact(Skip = "pending: T608 — see docs/PLAN.md")]
        public void TheResultCarriesWallClockMillisecondsAtLeastThatDelay() => Assert.Fail("pending: T608 — the result carries wall-clock milliseconds at least that delay");
    }

    // AC4 — voices are listed
    public sealed class ScenarioVoicesAreListed
    {
        // Given the stub answers GET /v1/audio/voices with ["a","b"]
        // When  the client lists voices

        [Fact(Skip = "pending: T608 — see docs/PLAN.md")]
        public void ItReturnsAAndB() => Assert.Fail("pending: T608 — it returns a and b");
    }

    // AC5 — distinct voices pass voiceSelect
    public sealed class ScenarioDistinctVoicesPassVoiceSelect
    {
        // Given two voice renders whose fingerprint distance is ≥ 3× the same-voice distance
        // When  the voiceSelect gate is computed

        [Fact(Skip = "pending: T608 — see docs/PLAN.md")]
        public void ItPasses() => Assert.Fail("pending: T608 — it passes");
    }

    // AC6 — a re-pick replaces
    public sealed class ScenarioARePickReplaces
    {
        // Given a profile whose register voice carries a re-pick with a reason
        // When  renders for that register are recorded

        [Fact(Skip = "pending: T608 — see docs/PLAN.md")]
        public void OnlyTheRePickedVoiceSRendersAreKept() => Assert.Fail("pending: T608 — only the re-picked voice's renders are kept");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC7 — same audio fails voiceSelect
    public sealed class ScenarioSameAudioFailsVoiceSelect
    {
        // Given two voice renders with identical fingerprints
        // When  the gate is computed

        [Fact(Skip = "pending: T608 — see docs/PLAN.md")]
        public void VoiceSelectIs() => Assert.Fail("pending: T608 — voiceSelect is ❌");
    }

    // AC8 — no container fails the gate
    public sealed class ScenarioNoContainerFailsTheGate
    {
        // Given a profile whose container is not running
        // When  the container gate is computed

        [Fact(Skip = "pending: T611 — see docs/PLAN.md")]
        public void ItIs() => Assert.Fail("pending: T611 — it is ❌");

        [Fact(Skip = "pending: T611 — see docs/PLAN.md")]
        public void TheEngineGetsNoPairs() => Assert.Fail("pending: T611 — the engine gets no pairs");
    }

    // AC9 — an unreachable engine is recorded
    public sealed class ScenarioAnUnreachableEngineIsRecorded
    {
        // Given a profile whose endpoint refuses connections
        // When  render runs

        [Fact(Skip = "pending: T611 — see docs/PLAN.md")]
        public void TheRunFinishes() => Assert.Fail("pending: T611 — the run finishes");

        [Fact(Skip = "pending: T611 — see docs/PLAN.md")]
        public void TheEngineRowShowsWithTheError() => Assert.Fail("pending: T611 — the engine row shows ❌ with the error");
    }

    // AC10 — two baselines are refused
    public sealed class ScenarioTwoBaselinesAreRefused
    {
        // Given two profiles with baseline:true
        // When  profiles load

        [Fact(Skip = "pending: T608 — see docs/PLAN.md")]
        public void LoadingFails() => Assert.Fail("pending: T608 — loading fails");
    }
}
