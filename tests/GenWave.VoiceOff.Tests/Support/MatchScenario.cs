using GenWave.Core.Abstractions;
using GenWave.VoiceOff.Audio;

namespace GenWave.VoiceOff.Tests.Support;

/// <summary>Arranges one loudness match in <see cref="InitializeAsync"/> so each fact asserts a single thing about its outcome.</summary>
public abstract class MatchScenario : IAsyncLifetime
{
    protected MatchScenario(ILoudnessAnalyzer? verifier = null) => Verifier = verifier;

    private ILoudnessAnalyzer? Verifier { get; }

    private protected ClipFixtureDirectory Clips { get; } = new();

    private protected LoudnessMatchResult Result { get; private set; } = LoudnessMatchResult.Success(0);

    private protected virtual string OutputPath => Clips.PathFor("matched.wav");

    protected abstract Task<string> InputAsync();

    protected virtual Task SeedAsync() => Task.CompletedTask;

    public async Task InitializeAsync()
    {
        string input = await InputAsync();
        await SeedAsync();
        LoudnessMatch match = Verifier is null ? new LoudnessMatch() : new LoudnessMatch(Verifier);
        Result = await match.MatchAsync(input, OutputPath, CancellationToken.None);
    }

    public Task DisposeAsync()
    {
        Clips.Dispose();
        return Task.CompletedTask;
    }

    private protected string[] FilesNamedMatched() => Directory.GetFiles(Path.GetDirectoryName(OutputPath) ?? "", "matched*");
}
