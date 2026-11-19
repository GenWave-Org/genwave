// T759 unit facts for SoundTags edge cases (SPEC F219.17/.18).

namespace GenWave.Tts.Tests.Specs;

public sealed class SoundTagsTests
{
    static readonly VoiceEngineProfile Chatterbox = VoiceEngineProfiles.Chatterbox;

    [Fact]
    public void CapZeroRemovesEveryTag() =>
        Assert.Equal("hi", SoundTags.Apply("[laugh] hi [sigh]", Chatterbox, cap: 0));

    [Fact]
    public void ADuplicatedTagCountsEachTimeAgainstTheCap() =>
        Assert.Equal("[laugh] hi", SoundTags.Apply("[laugh] hi [laugh]", Chatterbox, cap: 1));

    [Fact]
    public void AdjacentTagsKeepUpToTheCapInOrder() =>
        Assert.Equal("[sigh] [laugh] hi", SoundTags.Apply("[sigh] [laugh] [cough] hi", Chatterbox, cap: 2));

    [Fact]
    public void AnUnknownTagIsRemovedEvenUnderTheCap() =>
        Assert.Equal("[sigh] hi", SoundTags.Apply("[dance] [sigh] hi", Chatterbox, cap: 3));

    [Fact]
    public void AKeptTagIsWrittenInTheProfilesSpelling() =>
        Assert.Equal("[laugh] hi", SoundTags.Apply("[Laugh] hi", Chatterbox, cap: 1));

    [Fact]
    public void SpacingAwayFromARemovedTagIsLeftAlone() =>
        Assert.Equal("Wait ... ok", SoundTags.Strip("Wait ... ok [sigh]"));

    [Fact]
    public void ATagAtTheStartOfALineLeavesNoLeadingSpace() =>
        Assert.Equal("one\ntwo", SoundTags.Strip("one\n[sigh] two"));

    [Fact]
    public void ATagBetweenWordsLeavesOneSpace() =>
        Assert.Equal("a b", SoundTags.Strip("a  [sigh]  b"));

    [Fact]
    public void ARemovedTagLeavesNoSpaceBeforePunctuation() =>
        Assert.Equal("hi there.", SoundTags.Strip("hi there [sigh]."));

    [Fact]
    public void TextWithoutTagsComesBackUnchanged() =>
        Assert.Equal("  hi  there ", SoundTags.Apply("  hi  there ", Chatterbox, cap: 1));

    [Theory]
    [InlineData("say [pause:0.6s] now")]
    [InlineData("say [MacLeod](/məˈklaʊd/) now")]
    [InlineData("say [MacLeod] (/məˈklaʊd/) now")]
    public void OtherBracketMarkupIsNeverTouched(string text) =>
        Assert.Equal(text, SoundTags.Strip(text));

    [Theory]
    [InlineData("hi[sigh]there", "hi there")]
    [InlineData("nine[sigh]o'clock", "nine o'clock")]
    [InlineData("\"[sigh] hi\"", "\"hi\"")]
    [InlineData("(hi [sigh])", "(hi)")]
    [InlineData("( [sigh] hi)", "(hi)")]
    [InlineData("say \"hi\" [sigh] now", "say \"hi\" now")]
    [InlineData("say [sigh]\"hi\"", "say \"hi\"")]
    [InlineData("it's [sigh] fine", "it's fine")]
    [InlineData("hi [sigh].", "hi.")]
    [InlineData("hi [sigh], ok", "hi, ok")]
    [InlineData("hi,[sigh] ok", "hi, ok")]
    [InlineData("[sigh] hi", "hi")]
    [InlineData("hi [sigh]", "hi")]
    [InlineData("one\n[sigh] two", "one\ntwo")]
    [InlineData("one [sigh]\ntwo", "one\ntwo")]
    [InlineData("one [sigh]\r\ntwo", "one\r\ntwo")]
    [InlineData("one\r\n[sigh] two", "one\r\ntwo")]
    [InlineData("a\t[sigh]\tb", "a b")]
    [InlineData("a [sigh][laugh] b", "a b")]
    [InlineData("a [sigh] [laugh]", "a")]
    [InlineData("[sigh] [laugh]", "")]
    [InlineData("[sigh]", "")]
    [InlineData("Wait ... ok [sigh]", "Wait ... ok")]
    [InlineData("no tags  here ", "no tags  here ")]
    public void StripTidiesOnlyWhereATagWas(string text, string expected) =>
        Assert.Equal(expected, SoundTags.Strip(text));

    [Theory]
    [InlineData("hi[dance]there", 1, "hi there")]
    [InlineData("[dance] [laugh] [sigh] hi", 1, "[laugh] hi")]
    [InlineData("[sigh] a [laugh] b [cough] c", 2, "[sigh] a [laugh] b c")]
    [InlineData("a [laugh] b", 0, "a b")]
    [InlineData("no tags  here ", 1, "no tags  here ")]
    public void ApplyKeepsTheCapAndTidiesTheRest(string text, int cap, string expected) =>
        Assert.Equal(expected, SoundTags.Apply(text, Chatterbox, cap));

    [Fact]
    public void StripReturnsTheSameInstanceWhenNoTagMatched()
    {
        var text = new string("hi there".ToCharArray());

        Assert.Same(text, SoundTags.Strip(text));
    }
}
