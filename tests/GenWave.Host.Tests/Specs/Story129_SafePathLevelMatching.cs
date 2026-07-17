// STORY-129 — Safe plays air level-matched with honest gainDb (Epic U / SPEC F37, closes gitea-#200)
//
// BDD specification — xUnit. Authored PENDING at /plan time (2026-07-13, house rule since Epic S):
// every fact is Skip-pinned to the task that will prove it and carries an Assert.Fail body so an
// accidentally-unskipped run is loud, never silently green. U3 lands the two genwave.liq edits
// (safe-branch amplify + replay_gain export) and converts the repo-content facts to real greps;
// U7 proves the live halves against U1's recorded baselines on a scratch stack.

namespace GenWave.Host.Tests.Specs;

public static class FeatureSafePathLevelMatching
{
    /// <summary>Repo root, resolved relative to the test assembly's build output (Story074/102/107's convention).</summary>
    static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    static string EngineMetadataSourceText =>
        File.ReadAllText(Path.Combine(RepoRoot, "src", "GenWave.Core", "Domain", "EngineMetadata.cs"));

    static string EngineScriptText =>
        File.ReadAllText(Path.Combine(RepoRoot, "engine", "genwave.liq"));

    /// <summary>The `settings.encoder.metadata.export` append list's own text — sliced narrowly so a
    /// membership check can't false-positive against the other, unrelated `replay_gain` mentions
    /// (the override= call, code comments) elsewhere in the script.</summary>
    static string ExportAppendListText(string script)
    {
        const string marker = "list.append(settings.encoder.metadata.export(),";
        var start = script.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "settings.encoder.metadata.export list.append(...) call not found in genwave.liq");
        var end = script.IndexOf("])", start, StringComparison.Ordinal);
        Assert.True(end >= 0, "closing '])' for the export list.append(...) call not found in genwave.liq");
        return script[start..(end + 2)];
    }

    public sealed class ScenarioEngineGraphAppliesGainOnTheSafeBranch
    {
        const string AmplifyLine = "safe = amplify(1., override=\"replay_gain\", safe)";
        const string GapAppendLine = "append(safe, fun (_) -> blank(duration=gw_safe_gap_seconds))";

        [Fact]
        public void SafeSourceIsWrappedByAmplifyWithTheReplayGainOverride()
        {
            // F37.1: the safe_lib request.dynamic source (rebound as `safe`) is wrapped by the same
            // amplify(1., override="replay_gain", …) operator the main queue already uses — mirrored
            // verbatim so safe plays level-match main-rotation plays.
            Assert.Contains(AmplifyLine, EngineScriptText, StringComparison.Ordinal);
        }

        [Fact]
        public void AmplifySitsBeforeTheGapAppendSoBlankGapsCarryNoGain()
        {
            // F37.1: placed BEFORE the F29.6 gap append block — the blank(duration=...) gap track has
            // no replay_gain annotation of its own, so it must never pass through the amplify wrapper.
            var script = EngineScriptText;
            var amplifyIndex = script.IndexOf(AmplifyLine, StringComparison.Ordinal);
            var gapAppendIndex = script.IndexOf(GapAppendLine, StringComparison.Ordinal);

            Assert.True(amplifyIndex >= 0, "safe-branch amplify line not found in genwave.liq");
            Assert.True(gapAppendIndex >= 0, "F29.6 gap append block not found in genwave.liq");
            Assert.True(amplifyIndex < gapAppendIndex,
                "amplify must appear before the gap append so the blank gap carries no gain");
        }

        // Live half — the house engine-change discipline (`liquidsoap --check` on the pinned image)
        // is not a CI-runnable fact: no liquidsoap binary in the test image. Recorded as run, not
        // inferred (S8/T11 evidence-pinned idiom).
        const string Skip =
            "U3 --check spike run (2026-07-13): `docker run --rm -v \"$PWD/engine/genwave.liq:/genwave.liq:ro\" " +
            "--entrypoint liquidsoap savonet/liquidsoap:v2.4.4 --check /genwave.liq` (savonet/liquidsoap:v2.4.4 " +
            "is the exact base the pinned engine/Dockerfile builds FROM — compose.yaml's `engine` service has " +
            "no top-level `image:`, it's a `build:` context, so this is the pinned tag itself, not a proxy for " +
            "it) — exit code 0, empty stdout/stderr (a clean --check on this script prints nothing; house R4/T11 " +
            "convention: silence + exit 0 = pass). No -e env dummies were needed: every environment.get(...) " +
            "call in the script already carries a default=. Verdict: PASS.";

        [Fact(Skip = Skip)]
        public void TheScriptTypechecksOnPinnedLiquidsoap()
        {
            // U3: liquidsoap --check on pinned v2.4.4 passed cleanly against the edited script — see
            // the Skip reason above for the exact command, image, and result (F37.1, house discipline).
        }
    }

    public sealed class ScenarioReplayGainReachesTheOutputMetadata
    {
        [Fact]
        public void ReplayGainIsInTheEncoderExportAppendList()
        {
            // F37.2: "replay_gain" joins the settings.encoder.metadata.export append list so
            // engine-initiated plays' frames carry it — scoped to the append array's own text so an
            // unrelated `replay_gain` mention elsewhere in the script can't false-positive this fact.
            var exportListText = ExportAppendListText(EngineScriptText);
            Assert.Contains("replay_gain", exportListText, StringComparison.Ordinal);
        }

        // U7-rewritten (2026-07-13, u7smoke) — the live half this Skip text used to defer to U7.
        const string Skip = "U7 (2026-07-13, u7smoke): PUT Station:SafeScope:LibraryIds=[1] so the "
            + "drain pulled Quiet Track (stamped gain 20.10 dB, NOT peak-capped — the target-reachable "
            + "row U3's own live half flagged as still owed). GET /api/now-playing during the drain: "
            + "mediaId=1, gainDb=20.1 — exactly Quiet Track's own stamped replay_gain — against U1(b)'s "
            + "gainDb:0 pre-fix baseline. Evidence: scratchpad u7/partb_now_playing.json, "
            + "partb_quiet_row.json.";

        [Trait("Category", "Integration")]
        [Fact(Skip = Skip)]
        public void EngineInitiatedPlaysReportTheStampedGainDbWithZeroCsharpChanges()
        {
            // U7 proved live: gainDb=20.1 matches Quiet Track's stamped replay_gain, zero C# changes
            // (F37.2–F37.3).
        }
    }

    public sealed class ScenarioDrainAirsAtTargetLoudness
    {
        const string Skip = "U7 (2026-07-13, u7smoke): 110s of the drain stream recorded (ffmpeg against "
            + ":18000/stream) while Quiet Track (gain 20.10 dB, not peak-capped) looped in the SafeScope=[1] "
            + "rotation. ffmpeg silencedetect precisely located the F29.6 7.00s inter-safe gaps, isolating "
            + "a clean single-track window (offset 16.0-29.5s). Full-recording ebur128: -16.4 LUFS. Trimmed "
            + "clean window ebur128: -16.3 LUFS. Both within F37.3's -16 ±2.5 LU band, against U1(b)'s "
            + "pre-fix -25.8 LUFS baseline. Evidence: scratchpad u7/partb_ebur128.txt, "
            + "partb_drain_recording.mp3, partb_frame_history.log.";

        [Trait("Category", "Integration")]
        [Fact(Skip = Skip)]
        public void TheRecordedDrainWindowLandsAtTheConfiguredTargetWithinTolerance()
        {
            // U7 measured: -16.4 LUFS (full recording) / -16.3 LUFS (trimmed clean window), both
            // within ±2.5 LU of the -16 target (F37.3).
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioFalseDocumentationIsCorrected
    {
        [Fact]
        public void EngineMetadataXmlDocsNoLongerClaimAmplifyConsumesTheKey()
        {
            // Real, always-run, non-Skip repo-content assertion (Story102/107/S8's grep-assert
            // idiom) — no live stack needed. F37.4: neither the "consumed by amplify" claim (main-
            // queue tracks) nor the "bypass … survives to the output" claim (safe tracks) may
            // remain — both were falsified by the 2026-07-13 v2.4.4 source pass: amplify only READS
            // its override key and never deletes it; the missing key was always the
            // settings.encoder.metadata.export filter, not amplify consumption/bypass. Pinned on
            // distinctive OLD-text substrings being ABSENT and a distinctive NEW-text substring
            // (the export-list mechanism) being PRESENT.
            var source = EngineMetadataSourceText;

            Assert.DoesNotContain("have it consumed by", source, StringComparison.Ordinal);
            Assert.DoesNotContain("bypass the", source, StringComparison.Ordinal);
            Assert.DoesNotContain("survives to the output", source, StringComparison.Ordinal);
            Assert.Contains("settings.encoder.metadata.export", source, StringComparison.Ordinal);
        }
    }
}
