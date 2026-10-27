using GenWave.VoiceOff.Render;

namespace GenWave.VoiceOff.Tests.Render;

public sealed class RunIdTests
{
    [Theory]
    [InlineData("first")]
    [InlineData("2026-10-26_kokoro.v2")]
    [InlineData("0")]
    public void AFolderSizedNameIsValid(string name) => Assert.True(RunId.IsValid(name));

    [Theory]
    [InlineData("")]
    [InlineData(".hidden")]
    [InlineData("-flag")]
    [InlineData("a/b")]
    [InlineData("..")]
    [InlineData("has space")]
    [InlineData("trailing\n")]
    public void ANameThatIsNotOneSafeSegmentIsInvalid(string name) => Assert.False(RunId.IsValid(name));

    [Fact]
    public void ANameOver64CharactersIsInvalid() => Assert.False(RunId.IsValid(new string('a', 65)));

    [Fact]
    public void ANullNameIsInvalid() => Assert.False(RunId.IsValid(null));
}
