// gh-#950 — a ten-digit number read slowly with commas ("800, 555, 0199") is a phone shape too.
// Only the ten-digit shape takes a comma: a comma-grouped "100, 1980" is a list, not a phone.
//
// BDD specification — xUnit.

namespace GenWave.Core.Tests.Specs;

public static class FeatureCommaGroupedPhoneShape
{
    public sealed class ScenarioTheTenDigitShapeTakesCommas
    {
        [Theory]
        [InlineData("800, 555, 0199")]
        [InlineData("800,555,0199")]
        [InlineData("800-555, 0199")]
        public void TheWholeNumberMatches(string text) =>
            Assert.Equal(text, PhoneShape.Regex.Match(text).Value);
    }

    public sealed class ScenarioAShortCommaListIsNotAPhone
    {
        [Theory]
        [InlineData("the top 100, 1980 edition")]
        [InlineData("555, 0199")]
        public void NothingMatches(string text) => Assert.DoesNotMatch(PhoneShape.Regex, text);
    }
}
