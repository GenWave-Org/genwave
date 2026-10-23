using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Speech;

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
    public void TheTierIsRead() => Assert.Equal(EngineTier.Gpu, Parse("--tier", "gpu").Tier);

    [Fact]
    public void NoEngineOptionMeansNoEngineNamed() => Assert.Empty(Parse("--tier", "gpu").Engines);

    [Fact]
    public void TheRootDefaultsToTheHomeFolder() =>
        Assert.EndsWith(Path.Combine("genwave-voiceoff"), Parse("--tier", "cpu").Root, StringComparison.Ordinal);

    [Fact]
    public void EnginesDefaultNextToTheTool() =>
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "engines"), Parse("--tier", "cpu").EnginesDirectory);

    [Fact]
    public void GauntletDefaultsNextToTheTool() =>
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "gauntlet"), Parse("--tier", "cpu").GauntletDirectory);

    [Fact]
    public void EngineRepeatsInOrder() =>
        Assert.Equal(["b", "a"], Parse("--tier", "cpu", "--engine", "b", "--engine", "a").Engines);

    [Fact]
    public void ARootIsMadeAbsolute() =>
        Assert.Equal(Path.GetFullPath("relative-root"), Parse("--tier", "cpu", "--root", "relative-root").Root);

    [Fact]
    public void ATierIsRequired() => Assert.Contains("--tier", ErrorOf(), StringComparison.Ordinal);

    [Fact]
    public void AnUnknownTierIsRefused() => Assert.Contains("'tpu'", ErrorOf("--tier", "tpu"), StringComparison.Ordinal);

    [Fact]
    public void AnUnknownOptionIsRefused() => Assert.Contains("--bogus", ErrorOf("--tier", "cpu", "--bogus", "x"), StringComparison.Ordinal);

    [Fact]
    public void AStrayWordIsRefused() => Assert.Contains("'cpu'", ErrorOf("cpu"), StringComparison.Ordinal);

    [Fact]
    public void AMissingValueAtTheEndIsRefused() => Assert.Contains("needs a value", ErrorOf("--tier"), StringComparison.Ordinal);

    [Fact]
    public void AMissingValueBeforeTheNextOptionIsRefused() =>
        Assert.Contains("'--root' needs a value", ErrorOf("--tier", "cpu", "--root", "--engine", "x"), StringComparison.Ordinal);

    [Fact]
    public void ADuplicateOptionIsRefused() =>
        Assert.Contains("more than once", ErrorOf("--tier", "cpu", "--tier", "gpu"), StringComparison.Ordinal);

    [Fact]
    public void ADuplicateEngineIsRefused() =>
        Assert.Contains("'a'", ErrorOf("--tier", "cpu", "--engine", "a", "--engine", "a"), StringComparison.Ordinal);
}
