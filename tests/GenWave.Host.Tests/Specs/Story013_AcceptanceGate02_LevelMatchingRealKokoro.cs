// STORY-013 — Acceptance gate §0.2: level-matching with real Kokoro
//
// Integration gate against the REAL Kokoro container running on the core network.
// This is the cousin of tools/smoke_test.sh, applied to voice. Reuses its
// ebur128 recording machinery where possible.

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GenWave.Core.Domain;
using GenWave.Core.Playout;
using GenWave.Host.Engine;
using GenWave.Host.Options;
using GenWave.Tts;

// Alias to disambiguate the GenWave.Loudness namespace from the Loudness domain type.
using GenWave.Host.Tests.Fakes;
using FfmpegAnalyzer = GenWave.Loudness.FfmpegLoudnessAnalyzer;
using TrackLoudness = GenWave.Core.Domain.Loudness;

namespace GenWave.Host.Tests.Specs;

/// <summary>
/// Minimal <see cref="IOptionsMonitor{T}"/> that returns <see cref="CurrentValue"/> on every read
/// (mirrors Story120's/Story123's file-scoped precedent). <c>KokoroTtsSynthesizer</c> reads
/// <c>TtsOptions.Endpoint</c> through this per call (SPEC F36.1–F36.2) instead of a boot-frozen
/// <c>HttpClient.BaseAddress</c>.
/// </summary>
file sealed class FakeOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

public static class FeatureAcceptanceGate02LevelMatchingRealKokoro
{
    // -----------------------------------------------------------------------
    // Constants shared across scenarios
    // -----------------------------------------------------------------------

    const string KokoroBaseUrl = "http://127.0.0.1:18880";
    const string Voice = "af_heart";
    const double TargetLufs = -16.0;
    const double CeilingDbtp = -1.0;

    // -----------------------------------------------------------------------
    // Recorded-program LUFS gate band math (SPEC F64.1, STORY-161)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Pure band computation for the recorded-program LUFS gate
    /// (<see cref="ScenarioOutputStreamSitsAtTargetAcrossMixedSequence"/>): the assertion band is
    /// <c>(effectiveTargetLufs - offset) ± tolerance</c>. Extracted as an internal, testable helper
    /// (STORY-161) so the band math itself can be pinned by unit-level facts without spinning up
    /// the live stack this gate otherwise requires.
    /// </summary>
    internal static (double Lower, double Upper) ComputeExpectedLufsBand(
        double effectiveTargetLufs, double offset, double tolerance)
    {
        var expected = effectiveTargetLufs - offset;
        return (expected - tolerance, expected + tolerance);
    }

    // -----------------------------------------------------------------------
    // Shared segment render helper (used by the Kokoro collection scenarios)
    // -----------------------------------------------------------------------

    static async Task<MediaItem> RenderSegmentAsync(string cacheRoot, CancellationToken ct)
    {
        var ttsOptionsValue = new TtsOptions
        {
            Endpoint  = KokoroBaseUrl,
            Format    = "wav",
            CacheRoot = cacheRoot,
        };
        var ttsOptsMonitor = new FakeOptionsMonitor<TtsOptions>(ttsOptionsValue);

        var http        = new HttpClient();
        var synthesizer = new KokoroTtsSynthesizer(http, ttsOptsMonitor);
        var analyzer    = new FfmpegAnalyzer();

        var correctionsProvider = new SpeechCorrectionProvider(
            new FakeOptionsMonitor<TtsCorrectionsOptions>(new TtsCorrectionsOptions()),
            NullLogger<SpeechCorrectionProvider>.Instance);
        var personaCorrectionsCache = new ActivePersonaCorrectionsCache(
            new FakeActivePersonaAccessor(), TimeProvider.System);

        var source = new TtsSegmentSource(
            new TemplateCopyWriter(new PatterTemplateRenderer()),
            synthesizer,
            analyzer,
            new FakeCueAnalyzer(),
            correctionsProvider,
            personaCorrectionsCache,
            ttsOptsMonitor,
            NullLogger<TtsSegmentSource>.Instance);

        var request = new SegmentRequest(
            Kind:        SegmentKind.StationId,
            Voice:       Voice,
            StationName: "GenWave",
            Track:       null,
            LocalNow:    DateTimeOffset.UtcNow,
            StationId:   "test-station");

        var item = await source.RenderAsync(request, ct);
        Assert.NotNull(item);
        return item;
    }

    // -----------------------------------------------------------------------
    // HAPPY PATH — rendered segment has measured loudness
    // One shared fixture renders once; three facts assert different properties.
    // -----------------------------------------------------------------------

    [Collection("Kokoro")]
    [Trait("Category", "Integration")]
    public sealed class ScenarioRenderedSegmentHasMeasuredLoudness : IAsyncLifetime
    {
        readonly KokoroFixture fixture;
        DirectoryInfo cacheDir = null!;
        MediaItem renderedItem = null!;

        public ScenarioRenderedSegmentHasMeasuredLoudness(KokoroFixture fixture)
        {
            this.fixture = fixture;
        }

        public async Task InitializeAsync()
        {
            cacheDir = System.IO.Directory.CreateTempSubdirectory("genwave-t016-");
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            renderedItem = await RenderSegmentAsync(cacheDir.FullName, cts.Token);
        }

        public Task DisposeAsync()
        {
            if (cacheDir.Exists) cacheDir.Delete(recursive: true);
            return Task.CompletedTask;
        }

        [Fact]
        public void RenderedMediaItemLoudnessIsMeasurable()
        {
            Assert.True(renderedItem.Loudness.Measurable,
                $"Expected Loudness.Measurable=true; got IntegratedLufs={renderedItem.Loudness.IntegratedLufs}, TruePeakDbtp={renderedItem.Loudness.TruePeakDbtp}");
        }

        [Fact]
        public void IntegratedLufsIsFinite()
        {
            Assert.True(double.IsFinite(renderedItem.Loudness.IntegratedLufs),
                $"IntegratedLufs is not finite: {renderedItem.Loudness.IntegratedLufs}");
        }

        [Fact]
        public void TruePeakDbtpIsFinite()
        {
            Assert.True(double.IsFinite(renderedItem.Loudness.TruePeakDbtp),
                $"TruePeakDbtp is not finite: {renderedItem.Loudness.TruePeakDbtp}");
        }
    }

    // -----------------------------------------------------------------------
    // HAPPY PATH — feeder applies NormGainDb to TTS like music
    // No Kokoro needed; uses a fixed Loudness value to prove the formatting path.
    // -----------------------------------------------------------------------

    public sealed class ScenarioFeederAppliesNormGainDbToTtsLikeMusic : IAsyncDisposable
    {
        // A fixed measurable loudness so the test is deterministic and fast.
        static readonly TrackLoudness FixedLoudness = new(-20.0, -3.5, true);

        readonly FakeEngineServer engineServer;
        readonly LiquidsoapControl control;
        readonly MediaItem ttsItem;
        readonly double expectedGainDb;
        string capturedAnnotation = "";

        public ScenarioFeederAppliesNormGainDbToTtsLikeMusic()
        {
            engineServer = new FakeEngineServer(_ => "42");   // numeric RID — valid push response
            control = new LiquidsoapControl(
                new LiquidsoapOptions
                {
                    Host = "127.0.0.1",
                    Port = engineServer.Port,
                },
                stationId: "st-01",
                new FakeStationIdentityProvider(new StationIdentity("st-01", "GenWave", "af_heart")),
                new ArtworkUrlResolver(
                    new FakeOptionsMonitor<StationOptions>(new StationOptions()), new FakeArtworkTokenStore()),
                NullLogger<LiquidsoapControl>.Instance);

            ttsItem = new MediaItem("tts:abc123", "/tmp/abc123.wav", "You're listening to GenWave.", FixedLoudness);
            expectedGainDb = Gain.NormGainDb(FixedLoudness, TargetLufs, CeilingDbtp);
        }

        async Task EnsurePushed()
        {
            if (capturedAnnotation.Length > 0) return;
            await control.PushAsync(ttsItem, expectedGainDb, CancellationToken.None);
            // Commands[0] is the q.push command line.
            capturedAnnotation = engineServer.Commands.Count > 0
                ? engineServer.Commands[0]
                : "";
        }

        public async ValueTask DisposeAsync() => await engineServer.DisposeAsync();

        [Fact]
        public async Task PushAnnotationIncludesReplayGainInTheXDotXxDbForm()
        {
            await EnsurePushed();
            Assert.Matches(@"replay_gain=""-?\d+\.\d{2} dB""", capturedAnnotation);
        }

        [Fact]
        public async Task DbValueMatchesNormGainDbForTtsSegment()
        {
            await EnsurePushed();

            var match = Regex.Match(capturedAnnotation, @"replay_gain=""(-?[\d.]+) dB""");
            Assert.True(match.Success, $"Could not parse replay_gain from: {capturedAnnotation}");

            var parsed = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            Assert.Equal(expectedGainDb, parsed, precision: 2);
        }
    }

    // -----------------------------------------------------------------------
    // HAPPY PATH — output stream sits at target across mixed sequence
    // Guarded by environment: skipped if the live stream is not reachable.
    // -----------------------------------------------------------------------

    public sealed class ScenarioOutputStreamSitsAtTargetAcrossMixedSequence
    {
        const string StreamUrl = "http://localhost:8000/stream";

        // -------------------------------------------------------------------------------------
        // DERIVATION CONTRACT (SPEC F64.2) — ProgramLoudnessOffset is empirical and falsifiable.
        // Every reading that feeds it is recorded here honestly, even where readings disagree:
        //
        //   Reading A — target -12 (operator live override), measured mean ~= -15.5 LUFS,
        //   2026-07-12, 180s recording. Offset = target - measured ~= -12 - (-15.5) = 3.5 LU.
        //   This is the gitea-#204 defect: the OLD band (target ± 2.5 = [-14.5, -9.5]) structurally
        //   excluded this true mean — no offset term existed at all.
        //
        //   Reading B — target -16 (appsettings default), measured mean ~= -15.4 LUFS, predates
        //   2026-07-12, 180s recording. Naively: offset = -16 - (-15.4) = -0.6 LU, i.e. program
        //   loudness read ABOVE target at -16, the opposite sign of Reading A's ~3.5 LU gap at
        //   -12. That inconsistency is recorded here rather than silently dropped — it means
        //   either the offset is NOT target-independent (crossfade/gain-clamp interaction shifts
        //   with how much headroom the target leaves) or Reading B predates a change in program
        //   composition.
        //
        //   Reading C — Z11 (STORY-162), 2026-07-16, target -16 (appsettings default, unmodified
        //   on the z11smoke scratch stack), measured mean = -17.5 LUFS, 180s recording via ffmpeg
        //   against the scratch stack's own Icecast mount (host-mapped port 18000, isolated -p
        //   z11smoke project — never the operator's production stream). Offset = target - measured
        //   = -16 - (-17.5) = 1.5 LU. Content note (the derivation contract's own ask): the 180s
        //   window was NOT a real, timbrally-varied music catalog — it was Epic Z's own 5-fixture
        //   lookalike catalog (synthetic ffmpeg sine tones, 4-6s each, gain-normalized up from a
        //   very quiet native measurement) interleaved with TTS lead-in/back-announce patter on
        //   every track (Station:Cadence defaults), crossfading at the GW_XFADE_MIN/MAX=2/8s
        //   compose defaults. A pure sine tone has a far lower crest factor than real program
        //   material, so the true-peak ceiling (-1 dBTP) engages less aggressively than it would on
        //   real music — this reading's smaller gap from target (1.5 LU, versus Reading A's 3.5 LU
        //   on real music) is plausibly a fixture-composition effect, not evidence that Reading A
        //   was wrong. Recorded here exactly as read, not adjusted for that caveat.
        //
        //   All three readings disagree — in magnitude, and Reading B even disagrees in sign. This
        //   is the exact kind of inconsistency the contract insists on recording rather than
        //   quietly resolving. Per the explicit operator ruling (2026-07-15, recorded in
        //   Story162_AcceptanceGateRankingRobustness.cs): Z11's own scratch-stack gate reading is
        //   the authoritative derivation source the constant re-pins from, so ProgramLoudnessOffset
        //   moves to Reading C's value below. Story161's own "known station mean" regression
        //   (Reading A's -15.5 at a -12 override) still lands inside the recomputed band at this
        //   new offset — [-16.0, -11.0], a 0.5 LU margin — so the gitea-#204 defect this gate exists to
        //   prevent does not regress. A future gate that disagrees with 1.5 moves the constant
        //   again, the same way this one just did.
        // -------------------------------------------------------------------------------------
        internal const double ProgramLoudnessOffset = 1.5;

        // Integrate over a long window: a single short capture occasionally lands on a
        // quiet, peak-limited stretch (TTS-heavy or a soft track) and reads several LU
        // below the station mean, flaking the gate. 180s averages content variance out.
        const int RecordSeconds = 180;
        internal const double Tolerance = 2.5;

        readonly Xunit.Abstractions.ITestOutputHelper output;

        public ScenarioOutputStreamSitsAtTargetAcrossMixedSequence(Xunit.Abstractions.ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public async Task RecordedIntegratedLufsApproximatesTargetWithinTolerance()
        {
            // Guard: this fact records 3 minutes of the LIVE stream and asserts program loudness,
            // which is content-dependent (the known-flake note in docs/MEMORY.md). Per-track push
            // gains hit the target exactly (verified against three rows' gain math), but gated
            // program material under crossfade reads systematically lower than the per-track
            // target — the assertion band therefore centers on (effective TargetLufs -
            // ProgramLoudnessOffset), not on TargetLufs itself; see the derivation contract above
            // the ProgramLoudnessOffset/RecordSeconds/Tolerance constants (SPEC F64.1-F64.2) for
            // the full reading-by-reading evidence. The recording gate stays OPT-IN so a running
            // station doesn't block dev builds: set GENWAVE_LIVE_LUFS_GATE=1 to assert it (the
            // R13/pre-release manual-gate context; SPEC F64.3 keeps this unchanged).
            if (Environment.GetEnvironmentVariable("GENWAVE_LIVE_LUFS_GATE") != "1")
            {
                output.WriteLine(
                    "SKIPPED-AT-RUNTIME: GENWAVE_LIVE_LUFS_GATE!=1 — live-stream recording gate is " +
                    "opt-in (content-dependent; see docs/MEMORY.md 2026-07-12)");
                return;
            }

            // Guard: skip if the live stack is not running.
            if (!await IsStreamReachableAsync())
            {
                output.WriteLine(
                    $"SKIPPED-AT-RUNTIME: live stream not reachable at {StreamUrl} — gate not asserted");
                return;
            }

            // Guard: skip if only safe-rotation is playing (no music queued yet).
            // A 5s sample with LUFS well below the safe floor means no real music is flowing.
            var preCheckFile = Path.Combine(Path.GetTempPath(), $"genwave-t016-pre-{Guid.NewGuid():N}.mp3");
            try
            {
                RecordStream(preCheckFile, seconds: 5);

                var preCheckInfo = new FileInfo(preCheckFile);
                Assert.True(preCheckInfo.Exists && preCheckInfo.Length > 0,
                    "Pre-check recording is missing or empty — stream was reachable but ffmpeg produced no output.");

                var preLufs = MeasureIntegratedLufs(preCheckFile);
                if (!double.IsFinite(preLufs) || preLufs < -25.0)
                {
                    // Safe-rotation only: station has no ready tracks in the queue.
                    // This is an environment condition, not a code defect.
                    output.WriteLine(
                        $"SKIPPED-AT-RUNTIME: pre-check LUFS={preLufs:F1} (< -25 dB or non-finite) " +
                        $"— safe-rotation mode active, no music flowing; gate not meaningful");
                    return;
                }
            }
            finally
            {
                if (File.Exists(preCheckFile)) File.Delete(preCheckFile);
            }

            var recFile = Path.Combine(Path.GetTempPath(), $"genwave-t016-rec-{Guid.NewGuid():N}.mp3");
            try
            {
                // Record a window of the live stream.
                RecordStream(recFile, seconds: RecordSeconds);

                var recInfo = new FileInfo(recFile);
                Assert.True(recInfo.Exists && recInfo.Length > 0,
                    "Full recording is missing or empty — stream was reachable but ffmpeg produced no output.");

                // Measure integrated loudness with ebur128.
                var lufs = MeasureIntegratedLufs(recFile);

                Assert.True(double.IsFinite(lufs),
                    $"ebur128 measurement returned non-finite LUFS ({lufs}) on a non-empty recording — " +
                    $"this is a defect in the loudness pipeline, not an environment condition.");

                // SPEC F2.5's invariant is "output ≈ the CONFIGURED target". Since Epic I (F19) the
                // operator can live-override Loudness:TargetLufs via the DB-backed settings overlay,
                // so the effective target must be resolved at runtime rather than assumed to be the
                // appsettings default.
                var effectiveTarget = await ResolveEffectiveTargetLufsAsync();
                var (bandLower, bandUpper) = ComputeExpectedLufsBand(effectiveTarget, ProgramLoudnessOffset, Tolerance);
                output.WriteLine(
                    $"Effective TargetLufs={effectiveTarget:F1}, ProgramLoudnessOffset={ProgramLoudnessOffset:F1} " +
                    $"(measured={lufs:F1}, expected band=[{bandLower:F1}, {bandUpper:F1}], tolerance=±{Tolerance}).");
                Assert.InRange(lufs, bandLower, bandUpper);
            }
            finally
            {
                if (File.Exists(recFile)) File.Delete(recFile);
            }
        }

        /// <summary>
        /// Resolves the effective (overlay-aware) Loudness:TargetLufs by logging into the live
        /// api and reading GET /api/settings, since an operator can live-override the appsettings
        /// default (Epic I, F19). Falls back to the <see cref="TargetLufs"/> constant — logging why —
        /// when the admin password can't be found or any step of the api round-trip fails.
        /// </summary>
        async Task<double> ResolveEffectiveTargetLufsAsync()
        {
            var password = ReadAdminPassword();
            if (password is null)
            {
                output.WriteLine("FALLBACK: ADMIN_PASSWORD not found (env var or repo-root .env) — using constant TargetLufs.");
                return TargetLufs;
            }

            try
            {
                using var handler = new HttpClientHandler { CookieContainer = new CookieContainer() };
                using var http = new HttpClient(handler)
                {
                    BaseAddress = new Uri("http://localhost:8080"),
                    Timeout = TimeSpan.FromSeconds(5),
                };

                var loginResponse = await http.PostAsJsonAsync("/api/auth/login", new { password });
                if (!loginResponse.IsSuccessStatusCode)
                {
                    output.WriteLine(
                        $"FALLBACK: POST /api/auth/login returned {(int)loginResponse.StatusCode} — using constant TargetLufs.");
                    return TargetLufs;
                }

                var settingsResponse = await http.GetAsync("/api/settings");
                if (!settingsResponse.IsSuccessStatusCode)
                {
                    output.WriteLine(
                        $"FALLBACK: GET /api/settings returned {(int)settingsResponse.StatusCode} — using constant TargetLufs.");
                    return TargetLufs;
                }

                var json = await settingsResponse.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    if (!element.TryGetProperty("key", out var keyProp)
                        || !string.Equals(keyProp.GetString(), "Loudness:TargetLufs", StringComparison.Ordinal)
                        || !element.TryGetProperty("value", out var valueProp))
                    {
                        continue;
                    }

                    var raw = valueProp.GetString();
                    if (raw is not null
                        && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var effective))
                    {
                        return effective;
                    }
                }

                output.WriteLine(
                    "FALLBACK: Loudness:TargetLufs not present in GET /api/settings response — using constant TargetLufs.");
                return TargetLufs;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                output.WriteLine(
                    $"FALLBACK: effective-target resolution failed ({ex.GetType().Name}: {ex.Message}) — using constant TargetLufs.");
                return TargetLufs;
            }
        }

        /// <summary>
        /// Reads the admin password from the ADMIN_PASSWORD environment variable, falling back to
        /// the repo-root .env file (same base-directory-relative path pattern used elsewhere in this
        /// suite, e.g. Story062's ScriptPath).
        /// </summary>
        static string? ReadAdminPassword()
        {
            var fromEnv = Environment.GetEnvironmentVariable("ADMIN_PASSWORD");
            if (!string.IsNullOrEmpty(fromEnv)) return fromEnv;

            var envFilePath = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".env"));
            if (!File.Exists(envFilePath)) return null;

            try
            {
                foreach (var line in File.ReadAllLines(envFilePath))
                {
                    var trimmed = line.Trim();
                    if (!trimmed.StartsWith("ADMIN_PASSWORD=", StringComparison.Ordinal)) continue;

                    var value = trimmed["ADMIN_PASSWORD=".Length..];
                    return value.Length > 0 ? value : null;
                }
            }
            catch (IOException)
            {
                return null;
            }

            return null;
        }

        static async Task<bool> IsStreamReachableAsync()
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                using var response = await http.GetAsync(StreamUrl, HttpCompletionOption.ResponseHeadersRead);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        static double MeasureIntegratedLufs(string path)
        {
            var psi = new ProcessStartInfo("ffmpeg")
            {
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("-nostats");
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(path);
            psi.ArgumentList.Add("-filter_complex");
            psi.ArgumentList.Add("ebur128=peak=true");
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add("null");
            psi.ArgumentList.Add("-");

            using var p = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start ffmpeg.");
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit();

            var summary = err[Math.Max(0, err.LastIndexOf("Summary:", StringComparison.Ordinal))..];
            var match = Regex.Match(summary, @"I:\s*(-?[\d.]+)\s*LUFS");
            return match.Success
                ? double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
                : double.NegativeInfinity;
        }

        static void RecordStream(string outFile, int seconds)
        {
            var psi = new ProcessStartInfo("ffmpeg")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("-nostats");
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-loglevel");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(StreamUrl);
            psi.ArgumentList.Add("-t");
            psi.ArgumentList.Add(seconds.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add(outFile);

            using var p = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start ffmpeg.");
            p.WaitForExit();
            if (p.ExitCode != 0)
                throw new InvalidOperationException($"ffmpeg recording exited with code {p.ExitCode}.");
        }
    }

    // -----------------------------------------------------------------------
    // SAD PATH — unmeasurable clip not auto-amplified
    // Pure logic, no Kokoro, no I/O.
    // -----------------------------------------------------------------------

    public sealed class ScenarioShortClipGatedAsUnmeasurableIsNotAutoAmplified
    {
        [Fact]
        public void GainForUnmeasurableTtsSegmentIsZero()
        {
            var unmeasurable = new TrackLoudness(double.NegativeInfinity, double.NegativeInfinity, false);
            var dB = Gain.NormGainDb(unmeasurable, TargetLufs, CeilingDbtp);
            Assert.Equal(0.0, dB);
        }
    }
}
