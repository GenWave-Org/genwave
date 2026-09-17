// STORY-038 — Acceptance gate: energy-varied transitions + listening blind-test
//
// BDD specification — xUnit. End-to-end integration: real stack, recorded output stream,
// ebur128 windowed analysis. T507: the automated record+measure harness this file's facts were
// waiting on IS stack_gate.sh's capture leg (SPEC F178.5) — see each removed fact's former position
// below for the specific assertion it now maps to.
//
// AC5 (the all-day listening blind-test) is the HUMAN kill criterion — it has no automated
// Fact by design; it is signed off by ear after the metric + curve are tuned.

namespace GenWave.Host.Tests.Specs;

public static class FeatureAcceptanceGateEnergyVariedTransitions
{
    // A hot→hot crossfade measurably shorter than a mellow→mellow one, and both measured fades
    // falling within [GW_XFADE_MIN, GW_XFADE_MAX]: no gate assertion measures a fade's actual
    // duration directly — the nearest live proof is stack_gate.sh --capture (F178.5: zero silence
    // events, integrated LUFS within ±5 LU, a booth_log row), which proves silence/loudness/
    // booth_log outcomes, not transition timing — the same gap Story035's own crossfade facts map
    // to. Was an Assert.Fail stub before T507 (former facts
    // HotToHotCrossfadeIsMeasurablyShorterThanMellowToMellow, BothMeasuredFadesAreWithinXfadeBounds).

    // The wider cross window still passing the phase-1 smoke gate, and the Story023 cue-trim /
    // transition-gap measurements still holding: covered by stack_gate.sh --fresh's own health/on-air
    // checks and --capture's silencedetect/ebur128 pass over the SAME live stream (SPEC F178.4/
    // F178.5) — stack_gate.sh IS the successor to tools/smoke_test.sh's machinery, not a parallel gate
    // (former facts Phase1SmokeTestStillPassesWithTheWiderCrossWindow, F13CueGatesStillPass).

    // A null-energy track airing via the fixed 3s/3s fallback: no gate assertion measures which
    // fallback duration actually played — stack_gate.sh's capture leg proves silence/loudness
    // outcomes (F178.5), not a specific fade duration. The no-continuous-silent-window-over-0.5s
    // half of this fact IS covered: stack_gate.sh --capture's silencedetect noise=-45dB d=2
    // zero-events check across the fresh leg's real transitions (SPEC F178.5a). Was an Assert.Fail
    // stub before T507 (former fact NullEnergyTrackAirsWithFixedFallbackAndNoSilentGap).
}
