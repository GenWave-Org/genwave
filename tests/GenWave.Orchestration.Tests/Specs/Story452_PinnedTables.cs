// STORY-452 — pinned trace tables for the break characterisation replay (gh-#401 · PLAN T515, T522)
//
// FROZEN alongside Story452_BreakCharacterisationReplay.cs — see that file's header for the exact
// freeze rule (only PLAN T522 may touch either file, and only to add the AC6 trace table). These
// four tables are OBSERVED output from one deterministic run of that file's RunScriptAsync(), never
// hand-designed values.

namespace GenWave.Orchestration.Tests.Specs;

static class Story452PinnedTables
{
    public static readonly string[] BufferedMediaIds =
    [
        // O1 — two pending owner announcements + the opening lead-in (no back-announce yet)
        "tts:announcement:9001:tts:1",
        "tts:announcement:9002:tts:2",
        "tts:leadin-1",
        "t1",
        // O2 — the vended crosstalk exchange
        "tts:backannounce-2",
        "tts:crosstalk:genwave-story452-crosstalk",
        "tts:leadin-3",
        "t2",
        // O3 — the ad cadence hit
        "tts:backannounce-4",
        "ad-spot-1",
        "tts:leadin-5",
        "t3",
        // O4 — the station-id cadence hit + a due context segment
        "tts:backannounce-6",
        "tts:stationid-7",
        "tts:contextsegment-8",
        "tts:leadin-9",
        "t4",
        // O5 — the sign-off render exceeds the render budget and is dropped; the fallback track airs
        "t5",
        // O6 — the show-boundary straddle: sign-off drains, sign-on holds, the track crosses the boundary
        "tts:backannounce-11",
        "tts:signoff-12",
        "tts:leadin-13",
        "t6-straddle",
        // O7 — the held sign-on drains; the context segment's render returns null and is dropped
        "tts:backannounce-14",
        "tts:signon-15",
        "tts:leadin-17",
        "t5",
        // O8 — on-time and late time/date deferrals render; the expired one is dropped undrained
        // (on-time and late are indistinguishable here: only their count and the expired drop are pinned,
        // so row order never proves the honesty threshold)
        "tts:backannounce-18",
        "tts:timedate-19",
        "tts:timedate-20",
        "tts:leadin-21",
        "t6-straddle",
        // O9 — the ceremony-only finale: the decline fires before any music is planned
        "tts:signoff-22",
    ];

    public static readonly string[] DjNames =
    [
        "Nova", "Nova", "(none)", "Nova", // O1
        "(none)", "Nova", "(none)", "Nova", // O2
        "(none)", "Nova", "(none)", "Nova", // O3
        "(none)", "Nova", "(none)", "(none)", "Nova", // O4
        "Nova", // O5
        "(none)", "(none)", "(none)", "Nova", // O6
        "(none)", "(none)", "(none)", "Nova", // O7
        "(none)", "(none)", "(none)", "(none)", "Nova", // O8
        "(none)", // O9
    ];

    public static readonly string[] EventKinds = ["HandoffPieceDropped"];

    public static readonly string[] Warnings =
    [
        "Handoff piece SignOff dropped (render budget exceeded) — that half of the ceremony airs nothing; the other piece still airs if it rendered, and the next boundary retries the full ceremony (SPEC F92.4).",
        "Context segment for provider traffic dropped (render returned null) — no context item reaches air this boundary; music continues, and the next drain retries (SPEC F107.6).",
        "TimeDate deferral armed for 10:02 dropped undrained — 300s past its armed hour (budget 180s); a late time check would invent the hour.",
    ];
}
