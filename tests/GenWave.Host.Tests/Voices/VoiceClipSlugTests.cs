using GenWave.Host.Voices;

namespace GenWave.Host.Tests.Voices;

public sealed class VoiceClipSlugTests
{
    [Theory]
    [InlineData("My Reader", "my-reader")]
    [InlineData("../x.wav", "x-wav")]
    [InlineData("  --Hello,   World!!  ", "hello-world")]
    [InlineData("!!!", "clip")]
    [InlineData("", "clip")]
    public void TheSlugIsLowercaseHyphenatedAndTrimmed(string label, string expected) =>
        Assert.Equal(expected, VoiceClipSlug.FromLabel(label));

    [Theory]
    [InlineData(1, "clip")]
    [InlineData(2, "clip-2")]
    [InlineData(3, "clip-3")]
    public void ACollisionAddsANumberSuffix(int attempt, string expected) =>
        Assert.Equal(expected, VoiceClipSlug.Candidate("clip", attempt));
}
