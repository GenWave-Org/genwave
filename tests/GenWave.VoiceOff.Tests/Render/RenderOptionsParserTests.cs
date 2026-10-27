using GenWave.VoiceOff.Render;

namespace GenWave.VoiceOff.Tests.Render;

public sealed class RenderOptionsParserTests
{
    // The helpers return what the parser answered and fail with a plain exception on the wrong shape; the fact does the asserting.
    private static string ErrorOf(params string[] args) =>
        !RenderOptionsParser.TryParse(args, out _, out string? error) && error is not null
            ? error
            : throw new InvalidOperationException("The arguments were expected to be refused with a message.");

    private static RenderOptions Parse(params string[] args) =>
        RenderOptionsParser.TryParse(args, out RenderOptions? options, out string? error) && options is not null
            ? options
            : throw new InvalidOperationException($"The arguments were expected to parse: {error}");

    [Fact]
    public void TheRunIsRead() => Assert.Equal("first", Parse("--run", "first").RunId);

    [Fact]
    public void NoCardOptionMeansEveryCard() => Assert.Null(Parse("--run", "first").CardId);

    [Fact]
    public void ACardIsRead() => Assert.Equal("kokoro", Parse("--run", "first", "--card", "kokoro").CardId);

    [Fact]
    public void DryRunIsOffByDefault() => Assert.False(Parse("--run", "first").DryRun);

    [Fact]
    public void DryRunIsRead() => Assert.True(Parse("--run", "first", "--dry-run").DryRun);

    [Fact]
    public void TheRootDefaultsToTheHomeFolder() =>
        Assert.EndsWith("genwave-voiceoff", Parse("--run", "first").Root, StringComparison.Ordinal);

    [Fact]
    public void EnginesDefaultNextToTheTool() =>
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "engines"), Parse("--run", "first").EnginesDirectory);

    [Fact]
    public void CardsDefaultNextToTheTool() =>
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "cards"), Parse("--run", "first").CardsDirectory);

    [Fact]
    public void TheScriptDefaultsNextToTheTool() =>
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "script", "script.json"), Parse("--run", "first").ScriptPath);

    [Fact]
    public void ARootIsMadeAbsolute() =>
        Assert.Equal(Path.GetFullPath("relative-root"), Parse("--run", "first", "--root", "relative-root").Root);

    [Fact]
    public void ARunIsRequired() => Assert.Contains("--run", ErrorOf(), StringComparison.Ordinal);

    [Fact]
    public void ARunThatIsNotAFolderNameIsRefused() => Assert.Contains("'a/b'", ErrorOf("--run", "a/b"), StringComparison.Ordinal);

    [Fact]
    public void TheRetiredTierOptionIsRefused() => Assert.Contains("--tier", ErrorOf("--run", "first", "--tier", "cpu"), StringComparison.Ordinal);

    [Fact]
    public void AnUnknownOptionIsRefused() => Assert.Contains("--bogus", ErrorOf("--run", "first", "--bogus", "x"), StringComparison.Ordinal);

    [Fact]
    public void AStrayWordIsRefused() => Assert.Contains("'first'", ErrorOf("first"), StringComparison.Ordinal);

    [Fact]
    public void AMissingValueAtTheEndIsRefused() => Assert.Contains("needs a value", ErrorOf("--run"), StringComparison.Ordinal);

    [Fact]
    public void AMissingValueBeforeTheNextOptionIsRefused() =>
        Assert.Contains("'--root' needs a value", ErrorOf("--run", "first", "--root", "--card", "x"), StringComparison.Ordinal);

    [Fact]
    public void ADuplicateOptionIsRefused() =>
        Assert.Contains("more than once", ErrorOf("--run", "first", "--run", "second"), StringComparison.Ordinal);

    [Fact]
    public void ADuplicateDryRunIsRefused() =>
        Assert.Contains("more than once", ErrorOf("--run", "first", "--dry-run", "--dry-run"), StringComparison.Ordinal);
}
