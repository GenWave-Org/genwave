namespace GenWave.VoiceOff.Tests.Support;

/// <summary>What one <c>render</c> call left behind: its exit code, both streams and the single run folder.</summary>
internal sealed record RenderRun(int Exit, string Output, string Error, string? RunDirectory)
{
    internal string[] OutputLines => Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    internal string PathIn(string relative) =>
        Path.Combine(RunDirectory ?? throw new InvalidOperationException("The run made no single run folder."), relative);
}
