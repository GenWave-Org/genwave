using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Serving;

public static class ServeCommandTests
{
    private static (int Exit, string Error) Serve(params string[] args)
    {
        StringWriter error = new();
        int exit = Cli.Run(["serve", .. args], TextWriter.Null, error);
        return (exit, error.ToString());
    }

    private static string[] Standard(ServeScenario fixture, string run) =>
        ["--run", run, "--root", fixture.Options.Root, "--port", "0", "--bind", "127.0.0.1", "--engines", fixture.Options.EnginesDirectory, "--cards", fixture.Options.CardsDirectory];

    public sealed class ScenarioUsageErrors
    {
        [Fact]
        public void NoRunIsAUsageError() => Assert.Equal(2, Serve().Exit);

        [Fact]
        public void AnUnknownOptionIsAUsageError() => Assert.Equal(2, Serve("--run", "served", "--bogus", "1").Exit);

        [Fact]
        public void ABadPortIsAUsageError() => Assert.Equal(2, Serve("--run", "served", "--port", "70000").Exit);

        [Fact]
        public void ABadBindIsAUsageError() => Assert.Equal(2, Serve("--run", "served", "--bind", "nowhere").Exit);
    }

    public sealed class ScenarioARunThatWasNotRendered : IDisposable
    {
        private readonly ServeScenario fixture = new(bind: System.Net.IPAddress.Loopback);
        private readonly (int Exit, string Error) result;

        public ScenarioARunThatWasNotRendered() => result = Serve(Standard(fixture, "not-rendered"));

        [Fact]
        public void ItExits1() => Assert.Equal(1, result.Exit);

        [Fact]
        public void ItSaysToRenderFirst() => Assert.Contains("voiceoff render --run not-rendered", result.Error, StringComparison.Ordinal);

        public void Dispose() => fixture.Dispose();
    }

    public sealed class ScenarioABadJudgementFile : IDisposable
    {
        private readonly ServeScenario fixture = new(bind: System.Net.IPAddress.Loopback);
        private readonly (int Exit, string Error) result;

        public ScenarioABadJudgementFile()
        {
            File.WriteAllText(fixture.JudgementPath, "not json");
            result = Serve(Standard(fixture, "served"));
        }

        [Fact]
        public void ItExits1() => Assert.Equal(1, result.Exit);

        [Fact]
        public void TheFileIsNamed() => Assert.Contains("judgement.json", result.Error, StringComparison.Ordinal);

        public void Dispose() => fixture.Dispose();
    }

    public sealed class ScenarioAMissingCardsFolder : IDisposable
    {
        private readonly ServeScenario fixture = new(bind: System.Net.IPAddress.Loopback);
        private readonly int exit;

        public ScenarioAMissingCardsFolder() =>
            exit = Serve("--run", "served", "--root", fixture.Options.Root, "--port", "0", "--engines", fixture.Options.EnginesDirectory, "--cards", Path.Combine(fixture.Options.Root, "nope")).Exit;

        [Fact]
        public void ItExits1() => Assert.Equal(1, exit);

        public void Dispose() => fixture.Dispose();
    }

    public sealed class ScenarioACancelledRun : IAsyncLifetime, IDisposable
    {
        private readonly ServeScenario fixture = new(bind: System.Net.IPAddress.Loopback);
        private readonly StringWriter output = new();
        private int exit = -1;

        public async Task InitializeAsync()
        {
            using CancellationTokenSource cancel = new();
            Task<int> serving = Cli.RunAsync(["serve", .. Standard(fixture, "served")], output, new StringWriter(), GenWave.VoiceOff.Render.RenderServices.Default(), cancel.Token);
            if (!SpinWait.SpinUntil(() => output.ToString().Contains("Open http://127.0.0.1:", StringComparison.Ordinal), TimeSpan.FromSeconds(20)))
            {
                await cancel.CancelAsync();
                throw new TimeoutException("serve never printed its address.");
            }

            await cancel.CancelAsync();
            exit = await serving;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void PrintsItsAddress() => Assert.Contains("Open http://127.0.0.1:", output.ToString(), StringComparison.Ordinal);

        [Fact]
        public void ExitsWith0() => Assert.Equal(0, exit);

        public void Dispose() => fixture.Dispose();
    }

    public sealed class ScenarioHelp
    {
        private readonly string help;

        public ScenarioHelp()
        {
            StringWriter output = new();
            Cli.Run(["serve", "--help"], output, TextWriter.Null);
            help = output.ToString();
        }

        [Theory]
        [InlineData("--run <run-id>")]
        [InlineData("--port 8765")]
        [InlineData("[--root <dir>]")]
        [InlineData("[--bind <ip>]")]
        [InlineData("[--engines <dir>]")]
        [InlineData("[--cards <dir>]")]
        public void ServeHelpListsTheFlag(string flag) => Assert.Contains(flag, help, StringComparison.Ordinal);
    }
}
