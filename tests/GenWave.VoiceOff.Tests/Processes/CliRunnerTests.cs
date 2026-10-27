using GenWave.VoiceOff.Processes;

namespace GenWave.VoiceOff.Tests.Processes;

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

            // The SIGKILL to the grandchild lands asynchronously (the wait above covers only sh), so give it up to 5 s.
            string pid = (await File.ReadAllTextAsync(pidFile)).Trim();
            bool alive = IsAlive(pid);
            for (int i = 0; i < 500 && alive; i++)
            {
                await Task.Delay(10);
                alive = IsAlive(pid);
            }

            Assert.False(alive, "the child process is still running");
        }
        finally
        {
            File.Delete(pidFile);
        }
    }

    // Gone, or a zombie waiting to be reaped, both count as dead; stat can vanish between the two calls.
    private static bool IsAlive(string pid)
    {
        try
        {
            return !File.ReadAllText($"/proc/{pid}/stat").Contains(") Z ", StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }
}
