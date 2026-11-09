// STORY-541 — Pick the model by how varied it sounds (gh-#293 gh-#920 · SPEC F218 · PLAN T695)
//
// BDD specification — xUnit. AC1/AC3/AC5 (T695, bench process + scoring) here; AC2 (T695, real prompt) in
// tests/GenWave.Tts.Tests/Specs/Story541_PickTheModelByHowVariedItSounds_Prompt.cs; AC4 (T697, defaults) in
// tests/GenWave.Host.Tests/Specs/Story541_PickTheModelByHowVariedItSounds_Defaults.cs.

using System.Net;
using System.Net.Sockets;
using GenWave.Core.Domain;
using GenWave.Memory.Tests.Fakes;
using GenWave.PatterBench;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Memory.Tests.Specs;

public static class FeaturePickTheModelByHowVariedItSoundsBench
{
    const string SameCopy = "Here comes a real classic, turn it up loud.";

    static readonly DateTimeOffset RunStart = new(2026, 11, 8, 9, 30, 0, TimeSpan.Zero);

    sealed record BenchRun(int ExitCode, string Stdout, string Stderr, string Directory, string ReportPath)
    {
        public string Report => File.Exists(ReportPath) ? File.ReadAllText(ReportPath) : string.Empty;

        public IReadOnlyList<string[]> Rows =>
            Report.Split('\n')
                .Where(line => line.StartsWith("| ", StringComparison.Ordinal) && !line.StartsWith("| Model", StringComparison.Ordinal))
                .Select(line => line.Trim('|', ' ').Split(" | "))
                .ToList();
    }

    static async Task<BenchRun> RunBenchAsync(string[] args, CannedChatHandler? handler)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"patterbench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var reportPath = Path.Combine(directory, "report.md");
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var exit = await Cli.RunAsync(
            [.. args, "--out", reportPath], stdout, stderr, handler, new FakeTimeProvider(RunStart));
        return new BenchRun(exit, stdout.ToString(), stderr.ToString(), directory, reportPath);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — the report
    // entry point: Cli.RunAsync, what `dotnet run --project tools/PatterBench` calls, against a fake endpoint
    public sealed class ScenarioTheReport : IAsyncLifetime
    {
        // Given a fake endpoint returning canned copies and --models a,b --personas flip,sal -n 5
        // When  the bench runs
        BenchRun run = new(-1, "", "", "", "");

        public async Task InitializeAsync() =>
            run = await RunBenchAsync(
                ["--endpoint", "http://fake.test:11434", "--models", "a,b", "--personas", "flip,sal", "-n", "5"],
                new CannedChatHandler(_ => SameCopy));

        public Task DisposeAsync()
        {
            Directory.Delete(run.Directory, recursive: true);
            return Task.CompletedTask;
        }

        [Fact]
        public void ItWritesAMarkdownReportWithOneRowPerModelPersona() =>
            Assert.Equal(
                ["a|flip", "a|sal", "b|flip", "b|sal"],
                run.Rows.Select(cells => $"{cells[0]}|{cells[1]}").ToArray());

        [Fact]
        public void EachRowHasTheSixMetrics() =>
            Assert.Equal(
                "a | flip | 0.20 | \"a real classic turn\" ×5; \"classic turn it up\" ×5; \"comes a real classic\" ×5; \"here comes a real\" ×5; \"real classic turn it\" ×5 | 0.00 | 1.00 | 0 | 0 | n/a",
                string.Join(" | ", run.Rows[0]));
    }

    // AC3 — the catchphrase rate
    public sealed class ScenarioTheCatchphraseRate
    {
        // Given canned copies where 3 of 5 contain "banger, log it"
        // When  scored for a persona whose rule names that phrase
        readonly double rate = BenchScore.CatchphraseRate(
            [
                "That one is a Banger, log it!",
                "Next up, something gentle for the evening.",
                "Honestly? banger log it, no notes.",
                "Here is a slow one from the archive.",
                "Another banger, log it, and we go again.",
            ],
            [new QuirkRule("Declares a song 'banger, log it' once a shift", MaxPerShift: 1)]) ?? double.NaN;

        [Fact]
        public void TheCatchphraseRateIs06() => Assert.Equal(0.6, rate, 3);
    }

    // The scores, hand-computed
    public sealed class ScenarioTheFourGramRatio
    {
        // Given two copies sharing one four-word run
        readonly double ratio = BenchScore.DistinctFourGramRatio(["one two three four five", "one two three four six"]) ?? double.NaN;

        [Fact]
        public void ThreeOfFourGramsAreDistinct() => Assert.Equal(0.75, ratio, 3);
    }

    public sealed class ScenarioTheTopRepeats
    {
        // Given b-c-d-e in three copies and a-b-c-d in two
        readonly IReadOnlyList<RepeatedPhrase> repeats = BenchScore.TopRepeats(["a b c d e", "a b c d e", "b c d e f"]);

        [Fact]
        public void TheMostRepeatedComesFirst() =>
            Assert.Equal<IEnumerable<RepeatedPhrase>>(
                [new RepeatedPhrase("b c d e", 3), new RepeatedPhrase("a b c d", 2)], repeats);
    }

    public sealed class ScenarioTheVideoMemory : IAsyncLifetime
    {
        // Given the endpoint's /api/ps lists model a at 4 GiB
        BenchRun run = new(-1, "", "", "", "");

        public async Task InitializeAsync() =>
            run = await RunBenchAsync(
                ["--endpoint", "http://fake.test:11434", "--models", "a", "--personas", "flip", "-n", "2"],
                new CannedChatHandler(_ => SameCopy, """{"models":[{"name":"a","model":"a","size_vram":4294967296}]}"""));

        public Task DisposeAsync()
        {
            Directory.Delete(run.Directory, recursive: true);
            return Task.CompletedTask;
        }

        [Fact]
        public void TheRowShowsFourGiB() => Assert.Equal("4.0", run.Rows[0][^1]);
    }

    public sealed class ScenarioNoProcessList : IAsyncLifetime
    {
        // Given the endpoint answers /api/ps with 404
        BenchRun run = new(-1, "", "", "", "");

        public async Task InitializeAsync() =>
            run = await RunBenchAsync(
                ["--endpoint", "http://fake.test:11434", "--models", "a", "--personas", "flip", "-n", "2"],
                new CannedChatHandler(_ => SameCopy));

        public Task DisposeAsync()
        {
            Directory.Delete(run.Directory, recursive: true);
            return Task.CompletedTask;
        }

        [Fact]
        public void TheRowShowsNotAvailable() => Assert.Equal("n/a", run.Rows[0][^1]);
    }

    // The gate runs on the text the writer would air
    public sealed class ScenarioAReplyHygieneRejects : IAsyncLifetime
    {
        // Given the endpoint answers with nothing but empty copy
        BenchRun run = new(-1, "", "", "", "");

        public async Task InitializeAsync() =>
            run = await RunBenchAsync(
                ["--endpoint", "http://fake.test:11434", "--models", "a", "--personas", "flip", "-n", "3"],
                new CannedChatHandler(_ => ""));

        public Task DisposeAsync()
        {
            Directory.Delete(run.Directory, recursive: true);
            return Task.CompletedTask;
        }

        [Fact]
        public void TheGatePassRateIsZero() => Assert.Equal("0.00", run.Rows[0][5]);
    }

    public sealed class ScenarioAnOverLengthReply : IAsyncLifetime
    {
        // Given every reply is one fitting sentence plus a second sentence far over the 450 character cap
        BenchRun run = new(-1, "", "", "", "");

        public async Task InitializeAsync() =>
            run = await RunBenchAsync(
                ["--endpoint", "http://fake.test:11434", "--models", "a", "--personas", "flip", "-n", "5"],
                new CannedChatHandler(_ => $"{SameCopy} {string.Join(' ', Enumerable.Repeat("again", 100))}."));

        public Task DisposeAsync()
        {
            Directory.Delete(run.Directory, recursive: true);
            return Task.CompletedTask;
        }

        [Fact]
        public void ItIsScoredAsTheTrimmedText() =>
            Assert.Equal(
                "\"a real classic turn\" ×5; \"classic turn it up\" ×5; \"comes a real classic\" ×5; \"here comes a real\" ×5; \"real classic turn it\" ×5",
                run.Rows[0][3]);
    }

    public sealed class ScenarioTheCatchphraseHasAnApostrophe
    {
        // Given the rule Says 'that's a wrap' once a shift, and two of four copies say it in other case and punctuation
        readonly double rate = BenchScore.CatchphraseRate(
            ["Well, THAT'S a wrap!", "Next up, a slow one.", "That's a wrap... see you tomorrow.", "Here is the weather."],
            [new QuirkRule("Says 'that's a wrap' once a shift", MaxPerShift: 1)]) ?? double.NaN;

        [Fact]
        public void TheRateIsHalf() => Assert.Equal(0.5, rate, 3);
    }

    public sealed class ScenarioTheCatchphraseKeyPhrase
    {
        [Fact]
        public void ItIsTheQuotedSpanWithItsApostrophesDropped() =>
            Assert.Equal("thats a wrap", BenchScore.KeyPhrase("Says 'that's a wrap' once a shift"));
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC5 — a dead endpoint is a clear failure
    // entry point: Cli.RunAsync against a loopback port nobody listens on
    public sealed class ScenarioADeadEndpointIsAClearFailure : IAsyncLifetime
    {
        // Given an endpoint that refuses connections
        // When  the bench runs
        BenchRun run = new(-1, "", "", "", "");
        int port;

        public async Task InitializeAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            run = await RunBenchAsync(
                ["--endpoint", $"http://127.0.0.1:{port}", "--models", "a", "--personas", "flip", "-n", "2"], null);
        }

        public Task DisposeAsync()
        {
            Directory.Delete(run.Directory, recursive: true);
            return Task.CompletedTask;
        }

        [Fact]
        public void ItExitsNonZero() => Assert.Equal(1, run.ExitCode);

        [Fact]
        public void ItNamesTheEndpoint() =>
            Assert.Equal(
                $"PatterBench: cannot reach http://127.0.0.1:{port}/: Connection refused (127.0.0.1:{port}){Environment.NewLine}",
                run.Stderr);

        [Fact]
        public void ItLeavesNoPartialReport() =>
            Assert.Equal(Array.Empty<string>(), Directory.GetFileSystemEntries(run.Directory));
    }

    public sealed class ScenarioAReplyThatIsNotJson : IAsyncLifetime
    {
        // Given the endpoint answers 200 with a body that is not JSON
        BenchRun run = new(-1, "", "", "", "");

        public async Task InitializeAsync() =>
            run = await RunBenchAsync(
                ["--endpoint", "http://fake.test:11434", "--models", "a", "--personas", "flip", "-n", "2"],
                new CannedChatHandler(_ => SameCopy, rawChatBody: "<html>Not an API</html>"));

        public Task DisposeAsync()
        {
            Directory.Delete(run.Directory, recursive: true);
            return Task.CompletedTask;
        }

        [Fact]
        public void ItFailsLikeADeadEndpoint() =>
            Assert.Equal(
                (1, $"PatterBench: cannot reach http://fake.test:11434/: '<' is an invalid start of a value. Path: $ | LineNumber: 0 | BytePositionInLine: 0.{Environment.NewLine}", ""),
                (run.ExitCode, run.Stderr, string.Join(',', Directory.GetFileSystemEntries(run.Directory))));
    }

    public sealed class ScenarioAnUnknownPersona : IAsyncLifetime
    {
        // Given --personas names someone who has no card
        BenchRun run = new(-1, "", "", "", "");

        public async Task InitializeAsync() =>
            run = await RunBenchAsync(
                ["--endpoint", "http://fake.test:11434", "--models", "a", "--personas", "flip,nobody", "-n", "2"],
                new CannedChatHandler(_ => SameCopy));

        public Task DisposeAsync()
        {
            Directory.Delete(run.Directory, recursive: true);
            return Task.CompletedTask;
        }

        [Fact]
        public void ItExitsTwo() => Assert.Equal(2, run.ExitCode);

        [Fact]
        public void ItNamesThePersona() =>
            Assert.Equal(
                $"PatterBench: unknown persona 'nobody'.{Environment.NewLine}{BenchArgs.Usage}{Environment.NewLine}",
                run.Stderr);
    }
}
