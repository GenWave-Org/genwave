// gh-#950 — "1-800, 555, 0199", the same number read nice and slow, reached the voice as raw digits,
// and the leading "1" of "1-800-555-0199" stayed a digit while the rest were spelled. Both now air as
// digits read one by one, and ad-script hygiene rewrites the comma form like any other phone run.
//
// BDD specification — xUnit. Speech scenarios drive SpeechText.Normalize with an empty correction set.

using GenWave.Core.Domain;

namespace GenWave.Tts.Tests.Specs;

public static class FeatureCommaGroupedPhoneIsSpoken
{
    static string Speak(string text) => SpeechText.Normalize(text, SpeechCorrectionSet.Empty);

    public static class ScenarioTheIssueExhibit
    {
        [Fact]
        public static void BothReadingsAreSpokenTheSameWay() =>
            Assert.Equal(
                "that number is one, eight zero zero, five five five, zero one nine nine. "
                + "one more time, nice and slow, one, eight zero zero, five five five, zero one nine nine.",
                Speak("That number is 1-800-555-0199. One more time, nice and slow: 1-800, 555, 0199."));
    }

    public static class ScenarioALoneOneNotLeadingAPhoneStays
    {
        [Fact]
        public static void ItIsStillADigit() => Assert.Equal("track 1, 555 hits", Speak("Track 1 - 555 hits"));
    }

    public static class ScenarioHygieneRewritesTheCommaForm
    {
        [Fact]
        public static void TheStrayNumberBecomesTheSponsorsOwn() =>
            Assert.Equal(
                "HOST: Call 812-555-0100 today.",
                AdScriptWriter.ApplyPhoneHygiene("HOST: Call 800, 555, 0199 today.", "812-555-0100"));
    }
}
