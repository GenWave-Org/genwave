// STORY-307 — Ceremony names the show (F116.2) — prompt-content half
//
// BDD specification — xUnit, PENDING scaffold (planned 2026-08-10). Comment-bodied on
// purpose: the show fields in ceremony prompts land at T248. Golden-string idiom follows
// Story243/Story303. The boundary/dedupe half lives in Orchestration.Tests.

namespace GenWave.Tts.Tests.Specs;

using GenWave.Core.Domain;
using Xunit;

public static class FeatureShowCeremonyCopy
{
    const string StationClockLine = "Current date/time (station-local): irrelevant";
    const string StationId = "test-station";

    // Same FixedLocalNow idiom Story243_DjsHandOffAudibly.cs established (STORY-214's fixed-clock
    // rule) — a stable "Local time" line so a golden-string assertion below has something fixed to
    // pin against.
    static readonly DateTimeOffset FixedLocalNow = new(2026, 7, 27, 9, 0, 0, TimeSpan.Zero);

    static SegmentRequest HandoffRequest(
        SegmentKind kind,
        string? counterpartName,
        string? showName = null,
        string? showFlavor = null,
        string? counterpartShowName = null) =>
        new(kind, "af_heart", "GenWave", Track: null, FixedLocalNow, StationId,
            PersonaName: null, CounterpartName: counterpartName,
            ShowName: showName, ShowFlavor: showFlavor, CounterpartShowName: counterpartShowName);

    // -----------------------------------------------------------------------
    // F116.2 — the sign-on prompt gains the incoming show's name + flavor; the sign-off prompt may
    // name the ending show and the next (F114.3).
    // -----------------------------------------------------------------------

    public sealed class ScenarioSignOnCarriesTheShow
    {
        [Fact]
        public void SignOnPromptCarriesIncomingShowNameAndFlavor()
        {
            // Given a boundary into a block with show "The Breakfast Show" (flavor set)
            var content = LlmPromptBuilder.BuildUserContent(
                HandoffRequest(
                    SegmentKind.SignOn,
                    counterpartName: null,
                    showName: "The Breakfast Show",
                    showFlavor: "upbeat, chatty, coffee-fueled mornings"),
                StationClockLine, previouslyVoicedTasteNotes: []);

            // When the sign-on prompt is built, Then it carries the incoming show's name AND flavor
            // (flavor reaches the prompt ONLY — never any public payload; F115.3 — this fact proves
            // the prompt-content half of that rule; nothing here exposes the flavor anywhere else).
            Assert.Contains("The Breakfast Show", content);
            Assert.Contains("upbeat, chatty, coffee-fueled mornings", content);
        }

        [Fact]
        public void SignOffMayNameTheEndingAndNextShows()
        {
            // Given a boundary between two named shows
            var content = LlmPromptBuilder.BuildUserContent(
                HandoffRequest(
                    SegmentKind.SignOff,
                    counterpartName: "Nite Owl",
                    showName: "Night Moves",
                    counterpartShowName: "The Breakfast Show"),
                StationClockLine, previouslyVoicedTasteNotes: []);

            // When the sign-off prompt is built, Then both show names are available to the
            // copywriter (F114.3's "may name" — the ending show it's closing out AND the next one).
            Assert.Contains("Night Moves", content);
            Assert.Contains("The Breakfast Show", content);
        }
    }

    // -----------------------------------------------------------------------
    // F116.1 — a showless station's ceremony prompts stay byte-identical to pre-F116.
    // -----------------------------------------------------------------------

    public sealed class ScenarioShowlessCeremonyUntouched
    {
        [Fact]
        public void ShowlessCeremonyPromptIsByteIdentical()
        {
            // Given a boundary between blocks with no shows (every show field this epic adds left at
            // its default null — the pre-F116 construction shape)
            var signOffContent = LlmPromptBuilder.BuildUserContent(
                HandoffRequest(SegmentKind.SignOff, "Nite Owl"), StationClockLine, previouslyVoicedTasteNotes: []);
            var signOnContent = LlmPromptBuilder.BuildUserContent(
                HandoffRequest(SegmentKind.SignOn, "Daybreak Dana"), StationClockLine, previouslyVoicedTasteNotes: []);

            // When sign-on/sign-off prompts are built, Then output matches the pre-F116 golden
            // byte-for-byte (the EXACT strings Story243_DjsHandOffAudibly.cs's own
            // SignOffPromptMatchesExpectedContentByteForByte pins, plus the SignOn counterpart of the
            // same shape) — no show line is added when there is no show to name.
            const string ExpectedSignOff =
                "Station: GenWave\n" +
                "Local time: 2026-07-27 09:00\n" +
                "Current date/time (station-local): irrelevant\n" +
                "Segment: sign-off as you close out your shift on air.\n" +
                "Handoff note: Nite Owl is up next - you may name them as you sign off (e.g. " +
                "\"stick around, Nite Owl is coming up\"). Only use the name given here; never " +
                "invent a show name, time, or event for them.";
            const string ExpectedSignOn =
                "Station: GenWave\n" +
                "Local time: 2026-07-27 09:00\n" +
                "Current date/time (station-local): irrelevant\n" +
                "Segment: sign-on as you open your shift on air.\n" +
                "Handoff note: Daybreak Dana had the chair before you - you may thank or name them as " +
                "you open your shift (e.g. \"thanks to Daybreak Dana for that set\"). Only use the " +
                "name given here; never invent a show name, time, or event for them.";

            Assert.Equal(ExpectedSignOff, signOffContent);
            Assert.Equal(ExpectedSignOn, signOnContent);
        }
    }
}
