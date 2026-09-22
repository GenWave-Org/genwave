// STORY-464 — Phone shape lives in Core (gh-#700 · SPEC F197.1 · PLAN T544)
//
// AC1 (the four NANP shapes) lives here. AC2 ("the Ads assembly keeps no \d{3} regex of its own")
// moved to GenWave.Architecture.Tests (Specs/Story464_AdsKeepsNoPhoneRegex.cs, PLAN T544 build note):
// referencing GenWave.Ads from THIS project pulls in GenWave.Loudness transitively (Ads -> Tts ->
// Loudness), and GenWave.Loudness is a namespace nested directly under the shared "GenWave" root —
// once referenced, every unqualified `Loudness` in this project's own specs (the readonly record
// struct declared in src/GenWave.Abstractions/Domain/Loudness.cs, namespace GenWave.Core.Domain,
// used unqualified throughout GainTests.cs and the feeder specs) stops binding to
// that type and binds to the GenWave.Loudness NAMESPACE instead (enclosing-namespace members outrank
// `using`-imported types in C#'s lookup order), a hard CS0118 across ~20 unrelated call sites. Adding
// the reference here is not a cycle, but it is exactly as forcing as one — GenWave.Architecture.Tests
// already references GenWave.Ads for its own reflection-based fitness laws with no such collision (it
// carries no colliding `Loudness` type of its own), so AC2's fact lives there instead.

namespace GenWave.Core.Tests.Specs;

public static class FeaturePhoneshapelivesinCore
{
    public sealed class ScenarioTheFourNanpShapes
    {
        // Given: GenWave.Core.PhoneShape against "555-0142", "812-555-0199", "(812) 555-0199", "812.555.0199"

        /// <summary>AC1 — seven-digit dash</summary>
        [Fact]
        public void MatchesSevenDigits() => Assert.Matches(PhoneShape.Regex, "555-0142");

        /// <summary>AC1 — ten-digit dash</summary>
        [Fact]
        public void MatchesTenDigits() => Assert.Matches(PhoneShape.Regex, "812-555-0199");

        /// <summary>AC1 — parenthesised area code</summary>
        [Fact]
        public void MatchesParenthesisedAreaCode() => Assert.Matches(PhoneShape.Regex, "(812) 555-0199");

        /// <summary>AC1 — dotted</summary>
        [Fact]
        public void MatchesDottedSeparators() => Assert.Matches(PhoneShape.Regex, "812.555.0199");
    }
}
