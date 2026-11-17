// gh-#949 — the speech normaliser dropped the dollar sign: the gh-#541 flatten discards "$", so
// "$2.50" reached the voice as "2.50" and aired as "two point five zero". A dollar amount now
// expands to words before the flatten, the same way 76°F and 50% already do.
//
// BDD specification — xUnit. Every scenario drives SpeechText.Normalize with an empty correction set.

using GenWave.Core.Domain;

namespace GenWave.Tts.Tests.Specs;

public static class FeatureDollarAmountsAreSpoken
{
    static string Speak(string text) => SpeechText.Normalize(text, SpeechCorrectionSet.Empty);

    public static class ScenarioTheIssueExhibit
    {
        [Fact]
        public static void EveryPriceKeepsItsDollars() =>
            Assert.Equal(
                "coffee's 2 dollars and 50 cents, a muffin's 3 dollars, and the big breakfast is 19 dollars and 99 cents.",
                Speak("Coffee's $2.50, a muffin's $3, and the big breakfast is $19.99."));
    }

    public static class ScenarioShapes
    {
        [Theory]
        [InlineData("It's $1.", "it's 1 dollar.")]
        [InlineData("Only $5.00 today", "only 5 dollars today")]
        [InlineData("Just $0.99", "just 99 cents")]
        [InlineData("Add $1.01", "add 1 dollar and 1 cent")]
        [InlineData("A $1,200 TV", "a 1200 dollars tv")]
        [InlineData("A $2 million budget", "a 2 million dollars budget")]
        [InlineData("About $2.5 billion", "about 2.5 billion dollars")]
        [InlineData("Gas is $1.459 a litre", "gas is 1.459 dollars a litre")]
        public static void TheAmountIsSpokenWithItsUnit(string input, string expected) =>
            Assert.Equal(expected, Speak(input));

        [Fact]
        public static void AStylizedNameIsNotMoney() => Assert.Equal("ke$ha", Speak("Ke$ha"));

        [Fact]
        public static void ADollarSignWithNoDigitsIsUntouchedByThisRule() =>
            Assert.Equal("cash money", Speak("Cash $ money"));
    }
}
