// STORY-487 — The engine reads the station's settings on a fresh up (gh-#879 · SPEC F213.1–F213.3 · PLAN T598, T599)
//
// BDD specification — xUnit. AC1–AC3 (ScenarioComposeRender) went green at T598. This revision (T599)
// un-skips AC4–AC8: engine/entrypoint.sh's fetch loop is a 30 s wall-clock budget tracked in
// MILLISECONDS (a whole-second clock under-counts elapsed time by up to 999 ms). Driven through the
// REAL script via ScriptProcess.RunWithEmptyEnvironment, with curl/date/sleep/liquidsoap stubs
// (EntrypointBudgetArc, below) sharing ONE millisecond clock file:
//   • `curl` — fails a fixed number of times then answers (or never answers); every call logs the
//     clock reading at its own invocation (AC6's offset facts), then advances the clock — a small
//     fixed cost on success, and on failure either its own --max-time argument (a try that burns its
//     full timeout) or a small fixed cost (EntrypointBudgetArc's failCostMs — a connection refused
//     instantly, e.g. the api not yet listening).
//   • `sleep` — advances the clock by its own argument (fractional seconds) and returns at once.
//   • `date` — only reads the clock (milliseconds for `+%s%3N`, truncated whole seconds for `+%s`);
//     it never advances it.
//   • `liquidsoap` — records its own argv+env (EntrypointHarness), plus the clock reading at exec.
// The shared clock starts at a non-round millisecond, so a `date +%s` truncation bug shows up as a
// wrong offset rather than hiding behind a lucky round number. No real 30 s wait ever happens.

using System.Globalization;
using System.Text.Json;

using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureEngineSettingsOnFreshUp
{
    public sealed class ScenarioComposeRender
    {
        // Given: `docker compose config` of compose.yaml, and of compose.yaml + compose.piper-only.yaml
        // — rendered once per scenario (static Lazy, the Gh242 idiom) since every fact below reads
        // the same two renders.
        static readonly Lazy<JsonDocument> Base = new(() => ComposeConfigRender.Render());
        static readonly Lazy<JsonDocument> PiperOnly = new(() => ComposeConfigRender.Render("compose.piper-only.yaml"));

        /// <summary>AC1 — the api's depends_on has no engine (compose.yaml)</summary>
        [Fact]
        [Trait("Category", "Integration")]
        public void ApiDoesNotWaitOnTheEngine() =>
            Assert.DoesNotContain("engine", ComposeConfigRender.DependsOnNames(Base.Value, "api"));

        /// <summary>AC1 — the api's depends_on has no engine (piper-only overlay)</summary>
        [Fact]
        [Trait("Category", "Integration")]
        public void PiperOnlyApiDoesNotWaitOnTheEngine() =>
            Assert.DoesNotContain("engine", ComposeConfigRender.DependsOnNames(PiperOnly.Value, "api"));

        /// <summary>AC2 — the engine still waits on icecast service_healthy</summary>
        [Fact]
        [Trait("Category", "Integration")]
        public void EngineWaitsOnIcecast() =>
            Assert.Equal(
                "service_healthy",
                Base.Value.RootElement.GetProperty("services").GetProperty("engine")
                    .GetProperty("depends_on").GetProperty("icecast").GetProperty("condition").GetString());

        /// <summary>AC3 — the engine healthcheck start_period is at least 45 s</summary>
        [Fact]
        [Trait("Category", "Integration")]
        public void HealthcheckCoversTheWait() =>
            Assert.True(45 <= ComposeConfigRender.ParseSecondsDuration(
                Base.Value.RootElement.GetProperty("services").GetProperty("engine")
                    .GetProperty("healthcheck").GetProperty("start_period").GetString()
                    ?? throw new InvalidOperationException("engine healthcheck has no start_period")));
    }

    public sealed class ScenarioASlowApi(SlowApiEntrypointArc arc) : IClassFixture<SlowApiEntrypointArc>
    {
        // Given: stub curl fails 4 times, answers GW_XFADE_MIN=3 GW_XFADE_MAX=9 GW_SAFE_GAP_SECONDS=4 on the 5th;
        //        compose fallbacks 2 / 8 / 7.0 in the env; clock advances by each try's own timeout + sleep

        /// <summary>AC4 — liquidsoap sees GW_XFADE_MIN=3 (fetched, not the fallback)</summary>
        [Fact]
        public void LiquidsoapGetsTheFetchedValues() =>
            Assert.Contains("GW_XFADE_MIN=3", arc.LiquidsoapArgsAndEnv, StringComparison.Ordinal);

        /// <summary>AC4 — no fallback WARN line</summary>
        [Fact]
        public void NoFallbackLine() =>
            Assert.DoesNotContain(arc.StdErrLines, l => l.Contains("api unreachable", StringComparison.Ordinal));
    }

    public sealed class ScenarioAnImmediateAnswer(ImmediateAnswerEntrypointArc arc)
        : IClassFixture<ImmediateAnswerEntrypointArc>
    {
        // Given: stub curl answers on the 1st try

        /// <summary>AC5 — exactly one fetch</summary>
        [Fact]
        public void OneFetch() => Assert.Single(arc.CurlStartOffsetsMs);

        /// <summary>AC5 — sleep never called</summary>
        [Fact]
        public void NeverSleeps() => Assert.Empty(arc.SleepCalls);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheApiNeverAnswers(ApiNeverAnswersEntrypointArc arc)
        : IClassFixture<ApiNeverAnswersEntrypointArc>
    {
        // Given: stub curl always fails, consuming its own full --max-time on every try; clock
        //        advances by each try's own timeout + 1 s sleep; compose fallbacks 2 / 8 / 7.0 in env

        /// <summary>AC6 — every try starts before the 30 s (30 000 ms) budget is spent</summary>
        [Fact]
        public void EveryTryStartsBeforeTheBudget() =>
            Assert.All(arc.CurlStartOffsetsMs, offset => Assert.True(offset < 30_000));

        /// <summary>AC6 — "not before": the engine never boots before the 30 000 ms budget is spent.</summary>
        [Fact]
        public void DoesNotBootBeforeTheBudget() => Assert.True(arc.LiquidsoapExecOffsetMs >= 30_000);

        /// <summary>AC6 — "not after": the engine never boots once the 30 000 ms budget has been
        /// overrun.</summary>
        [Fact]
        public void DoesNotBootAfterTheBudget() => Assert.True(arc.LiquidsoapExecOffsetMs <= 30_000);

        /// <summary>AC7 — today's "api unreachable … using fallback env" line prints once</summary>
        [Fact]
        public void FallbackLinePrints() =>
            Assert.Single(arc.StdErrLines, l => l.Contains("api unreachable", StringComparison.Ordinal));

        /// <summary>AC7 — liquidsoap sees the compose fallback GW_XFADE_MIN=2</summary>
        [Fact]
        public void LiquidsoapGetsTheFallbacks() =>
            Assert.Contains("GW_XFADE_MIN=2", arc.LiquidsoapArgsAndEnv, StringComparison.Ordinal);

        /// <summary>AC8 — liquidsoap is exec'd (entrypoint exits 0 via the stub)</summary>
        [Fact]
        public void TheEngineAlwaysBoots() => Assert.Equal(0, arc.ExitCode);
    }

    public sealed class ScenarioTheApiRefusesInstantly(ApiRefusesInstantlyEntrypointArc arc)
        : IClassFixture<ApiRefusesInstantlyEntrypointArc>
    {
        // Given: stub curl always fails, but each failing call costs a fixed 7 ms (connection
        //        refused) rather than burning its own --max-time like ScenarioTheApiNeverAnswers
        //        above — the real gh-#879 case where the api isn't listening yet; clock advances
        //        by 7 ms + 1 s sleep per try; compose fallbacks 2 / 8 / 7.0 in env

        /// <summary>AC6 — every try starts before the 30 s (30 000 ms) budget is spent</summary>
        [Fact]
        public void EveryTryStartsBeforeTheBudget() =>
            Assert.All(arc.CurlStartOffsetsMs, offset => Assert.True(offset < 30_000));

        /// <summary>AC6 — "not before": the engine never boots before the 30 000 ms budget is spent.
        /// Unlike ScenarioTheApiNeverAnswers's full --max-time failures, a whole-second `date +%s`
        /// clock loop under-reads elapsed time enough here to exec early — this fact is what
        /// catches that regression.</summary>
        [Fact]
        public void DoesNotBootBeforeTheBudget() => Assert.True(arc.LiquidsoapExecOffsetMs >= 30_000);

        /// <summary>AC6 — "not after": the engine never boots once the 30 000 ms budget has been
        /// overrun.</summary>
        [Fact]
        public void DoesNotBootAfterTheBudget() => Assert.True(arc.LiquidsoapExecOffsetMs <= 30_000);

        /// <summary>AC8 — liquidsoap is exec'd (entrypoint exits 0 via the stub)</summary>
        [Fact]
        public void TheEngineAlwaysBoots() => Assert.Equal(0, arc.ExitCode);
    }
}

// ── Arc: engine/entrypoint.sh's 30 s fetch-retry budget (STORY-487 AC4–AC8) ─────────────────────

/// <summary>
/// Runs the REAL <c>engine/entrypoint.sh</c> once (<see cref="EntrypointHarness"/>'s shared PATH +
/// liquidsoap stub, plus this class's own millisecond-precision curl/date/sleep/liquidsoap stubs)
/// against a <c>curl</c> that fails <paramref name="failCount"/> times before answering with a fixed
/// GW_XFADE_MIN=3/GW_XFADE_MAX=9/GW_SAFE_GAP_SECONDS=4 body, or never answers at all when <paramref
/// name="failCount"/> is <c>null</c> (the exhausted-budget scenario). <paramref name="fallbackEnv"/>
/// seeds the compose fallback env a real `docker compose up` would already have set before
/// entrypoint.sh ever runs. <paramref name="failCostMs"/> is the fixed clock cost of a failing curl
/// call; <c>null</c> (the default) instead charges the call its own effective --max-time argument —
/// see the two never-answers arcs below for when each applies.
/// </summary>
public abstract class EntrypointBudgetArc(
    int? failCount,
    IReadOnlyDictionary<string, string>? fallbackEnv = null,
    long? failCostMs = null)
    : IAsyncLifetime
{
    /// <summary>Compose's fallback env for the three tuning keys — shared by every scenario whose
    /// facts read the fallback path.</summary>
    public static readonly IReadOnlyDictionary<string, string> ComposeFallbackEnv = new Dictionary<string, string>
    {
        ["GW_XFADE_MIN"] = "2",
        ["GW_XFADE_MAX"] = "8",
        ["GW_SAFE_GAP_SECONDS"] = "7.0",
    };

    /// <summary>The shared fake clock's starting value — deliberately NOT on a second boundary, so a
    /// `date +%s` truncation bug shows up as a wrong offset rather than hiding behind a lucky round
    /// number.</summary>
    const long InitialClockMs = 1_000_000_900;

    static readonly string[] FetchedBody = ["GW_XFADE_MIN=3", "GW_XFADE_MAX=9", "GW_SAFE_GAP_SECONDS=4"];

    public int ExitCode { get; private set; }

    public IReadOnlyList<string> StdErrLines { get; private set; } = [];

    /// <summary>The liquidsoap stub's recorded argv+env (see <see cref="EntrypointHarness"/>).</summary>
    public string LiquidsoapArgsAndEnv { get; private set; } = "";

    /// <summary>Each <c>curl</c> invocation's clock reading at its OWN call, in call order, as an
    /// offset (milliseconds) from <see cref="InitialClockMs"/> — i.e. elapsed time since
    /// entrypoint.sh started.</summary>
    public IReadOnlyList<long> CurlStartOffsetsMs { get; private set; } = [];

    /// <summary>Each <c>sleep</c> stub invocation's own argument, in call order.</summary>
    public IReadOnlyList<string> SleepCalls { get; private set; } = [];

    /// <summary>The clock reading at the liquidsoap exec, as an offset (milliseconds) from <see
    /// cref="InitialClockMs"/> — AC6's "not before"/"not after" facts assert against this.</summary>
    public long LiquidsoapExecOffsetMs { get; private set; }

    /// <summary>Splits the seconds value in shell var <paramref name="varName"/> into `int_part`/
    /// `frac_part`. Assumes a 3-digit fraction ("1.234"), which entrypoint.sh always emits
    /// (`%03d`); "1.5" would read as 5 ms.</summary>
    static string SplitFractionalSeconds(string varName) => $"""
        case "${varName}" in
            *.*) int_part=$(echo "${varName}" | cut -d. -f1); frac_part=$(echo "${varName}" | cut -d. -f2) ;;
            *)   int_part="${varName}"; frac_part=0 ;;
        esac
        """;

    public Task InitializeAsync()
    {
        var bin = EntrypointHarness.MakeBinDirWithLiquidsoapStub(out var liquidsoapRecorded);
        // MakeBinDir's default toolset already symlinks the REAL `date` and `sleep` in (gh-#776's
        // shared superset); the symlink must go before AddStub can shadow it (Story405/Story345's
        // own "delete the default symlink first" idiom — the same reason EntrypointHarness deletes
        // `curl`'s symlink before its callers stub it).
        File.Delete(Path.Combine(bin, "date"));
        File.Delete(Path.Combine(bin, "sleep"));

        var scratch = TempDir.CreateForProcessLifetime();

        var clockFile = Path.Combine(scratch, "clock");
        var sleepLogFile = Path.Combine(scratch, "sleep.log");
        var curlClockLogFile = Path.Combine(scratch, "curl.clock.log");
        var curlCountFile = Path.Combine(scratch, "curl.count");
        var liquidsoapClockLogFile = Path.Combine(scratch, "liquidsoap.clock.log");

        File.WriteAllText(clockFile, InitialClockMs.ToString(CultureInfo.InvariantCulture));

        // Fake clock: `date` only ever READS the shared clock file — it never advances it, so the
        // clock only moves when the script's own curl/sleep calls move it. `+%s%3N` reads
        // milliseconds; `+%s` reads whole seconds, truncating exactly as the real GNU date would
        // (the very truncation SPEC F213.2 requires the script to avoid).
        ScriptProcess.AddStub(bin, "date", $"""
            clock=$(cat "{clockFile}")
            case "$1" in
                +%s%3N) echo "$clock" ;;
                *) echo $(( clock / 1000 )) ;;
            esac
            """);

        // Advances the clock by its OWN argument (fractional seconds, e.g. "1.000") before returning
        // at once — never a real wait.
        ScriptProcess.AddStub(bin, "sleep", $"""
            echo "$1" >> "{sleepLogFile}"
            clock=$(cat "{clockFile}")
            {SplitFractionalSeconds("1")}
            echo $(( clock + int_part * 1000 + 10#$frac_part )) > "{clockFile}"
            exit 0
            """);

        // Fails until the (failCount + 1)-th call, then answers; never answers at all when
        // failCount is null. Every call — pass or fail — first logs the clock reading in effect at
        // its own invocation (AC6's offset facts read this), then advances the clock: a small fixed
        // cost on success (a real connect that gets a fast reply doesn't burn its full timeout), and
        // on failure either its own effective --max-time argument (parsed off "$@" — a try that
        // burns its full timeout) or the fixed failCostMs (a connection refused instantly).
        var willSucceedCheck = failCount is int n
            ? $"""
              will_succeed=0
              if [ "$n" -gt {n} ]; then
                  will_succeed=1
              fi
              """
            : "will_succeed=0";
        var failureDeltaMs = failCostMs is long fixedFailCostMs
            ? $"delta_ms={fixedFailCostMs.ToString(CultureInfo.InvariantCulture)}"
            : $"""
              {SplitFractionalSeconds("max_time")}
                  delta_ms=$(( int_part * 1000 + 10#$frac_part ))
              """;
        ScriptProcess.AddStub(bin, "curl", $"""
            n=$(( $(cat "{curlCountFile}" 2>/dev/null || echo 0) + 1 ))
            echo "$n" > "{curlCountFile}"

            max_time=2
            prev=""
            for arg in "$@"; do
                if [ "$prev" = "--max-time" ]; then
                    max_time="$arg"
                fi
                prev="$arg"
            done

            clock=$(cat "{clockFile}")
            echo "$clock" >> "{curlClockLogFile}"

            {willSucceedCheck}

            if [ "$will_succeed" = "1" ]; then
                delta_ms=50
            else
                {failureDeltaMs}
            fi
            echo $(( clock + delta_ms )) > "{clockFile}"

            if [ "$will_succeed" = "1" ]; then
                printf '%s\n' {string.Join(' ', FetchedBody.Select(line => $"'{line}'"))}
                exit 0
            fi
            exit 7
            """);

        // Extends EntrypointHarness's plain argv+env recorder with the clock reading AT exec, read
        // directly off the shared clock file — AC6's "not before"/"not after" facts assert against it.
        ScriptProcess.AddStub(bin, "liquidsoap", $"""
            cat "{clockFile}" > "{liquidsoapClockLogFile}"
            printf '%s\n' "$@" > "{liquidsoapRecorded}"
            env >> "{liquidsoapRecorded}"
            exit 0
            """);

        var run = ScriptProcess.RunWithEmptyEnvironment("engine/entrypoint.sh", bin, fallbackEnv);

        ExitCode = run.ExitCode;
        StdErrLines = run.StdErr.Split('\n');
        LiquidsoapArgsAndEnv = File.Exists(liquidsoapRecorded) ? File.ReadAllText(liquidsoapRecorded) : "";
        CurlStartOffsetsMs = File.Exists(curlClockLogFile)
            ? File.ReadAllLines(curlClockLogFile)
                .Select(line => long.Parse(line, CultureInfo.InvariantCulture) - InitialClockMs)
                .ToArray()
            : [];
        SleepCalls = File.Exists(sleepLogFile) ? File.ReadAllLines(sleepLogFile) : [];
        LiquidsoapExecOffsetMs = File.Exists(liquidsoapClockLogFile)
            ? long.Parse(File.ReadAllText(liquidsoapClockLogFile).Trim(), CultureInfo.InvariantCulture) - InitialClockMs
            : throw new InvalidOperationException("liquidsoap stub never exec'd; no clock reading captured");

        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>AC4 — curl fails 4 times, then answers.</summary>
public sealed class SlowApiEntrypointArc()
    : EntrypointBudgetArc(failCount: 4, fallbackEnv: EntrypointBudgetArc.ComposeFallbackEnv)
{
}

/// <summary>AC5 — curl answers on the very first call.</summary>
public sealed class ImmediateAnswerEntrypointArc() : EntrypointBudgetArc(failCount: 0)
{
}

/// <summary>AC6–AC8 — curl never answers; every failing call burns its own full --max-time; the
/// loop runs out its 30 s budget.</summary>
public sealed class ApiNeverAnswersEntrypointArc()
    : EntrypointBudgetArc(failCount: null, fallbackEnv: EntrypointBudgetArc.ComposeFallbackEnv)
{
}

/// <summary>AC6, AC8 — curl never answers, but every failing call costs a fixed 7 ms (connection
/// refused) rather than its own --max-time — gh-#879's actual failure mode, where the api isn't
/// listening yet. Proves the budget is tracked from real elapsed wall-clock time, not from summing
/// each try's own timeout, which a whole-second `date +%s` clock loop would still get away with in
/// <see cref="ApiNeverAnswersEntrypointArc"/> alone.</summary>
public sealed class ApiRefusesInstantlyEntrypointArc()
    : EntrypointBudgetArc(failCount: null, fallbackEnv: EntrypointBudgetArc.ComposeFallbackEnv, failCostMs: 7)
{
}
