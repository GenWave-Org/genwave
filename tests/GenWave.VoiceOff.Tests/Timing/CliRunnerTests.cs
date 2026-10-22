using GenWave.VoiceOff.Processes;

namespace GenWave.VoiceOff.Tests.Timing;

public sealed class CliRunnerTests
{
    [Fact]
    public async Task AMissingProgramIsNull() =>
        Assert.Null(await CliRunner.RunAsync("genwave-no-such-program-t610", [], CancellationToken.None));

    [Fact]
    public async Task ExitCodeAndBothStreamsComeBack() =>
        Assert.Equal((3, "out\n", "err\n"),
            await CliRunner.RunAsync("sh", ["-c", "echo out; echo err >&2; exit 3"], CancellationToken.None));

    [Fact]
    public async Task CancellingThrows()
    {
        using CancellationTokenSource cts = new();
        Task<(int, string, string)?> run = CliRunner.RunAsync("sh", ["-c", "sleep 60"], cts.Token);
        await Task.Delay(200);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task CancellingLeavesNoLiveChild()
    {
        string pidFile = Path.Combine(Path.GetTempPath(), "voiceoff-pid-" + Guid.NewGuid().ToString("N"));
        try
        {
            using CancellationTokenSource cts = new();
            Task<(int, string, string)?> run = CliRunner.RunAsync("sh", ["-c", $"sleep 60 & echo $! > {pidFile}; wait"], cts.Token);
            for (int i = 0; i < 500 && !(File.Exists(pidFile) && new FileInfo(pidFile).Length > 0); i++)
            {
                await Task.Delay(10);
            }

            await cts.CancelAsync();
            try
            {
                await run.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (OperationCanceledException)
            {
                // Expected: the cancel is what this fact arranges; the assertion is about the child.
            }

            string pid = (await File.ReadAllTextAsync(pidFile)).Trim();
            Assert.False(File.Exists($"/proc/{pid}/stat") && !(await File.ReadAllTextAsync($"/proc/{pid}/stat")).Contains(") Z ", StringComparison.Ordinal),
                "the child process is still running");
        }
        finally
        {
            File.Delete(pidFile);
        }
    }
}
