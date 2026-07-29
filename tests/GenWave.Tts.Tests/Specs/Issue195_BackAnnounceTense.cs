// gh-#195 — llm: back-announce spoke in lead-in tense — a break's two prompts differ by ONE line,
// and the old one-clause segment phrasing lost to a wall of identical track facts. Observed live
// (demo Booth log, 2026-07-28): a back-announce airing AFTER the song announced it as "just
// dropped!". The segment lines now state the tense/direction contract outright; these facts pin
// that contract so a rewording can't quietly soften it back into ambiguity.

using GenWave.Core.Domain;

namespace GenWave.Tts.Tests.Specs;

public static class FeatureBackAnnounceTense
{
    static readonly MediaItem Track = new(
        "m1", "/media/x.mp3", "The Very Opposite Of My Husband", default, Artist: "Dazie Mae");

    static SegmentRequest Request(SegmentKind kind) =>
        new(kind, "af_heart", "GenWave", Track, DateTimeOffset.UtcNow, "test-station");

    static string UserContent(SegmentKind kind) =>
        LlmPromptBuilder.BuildUserContent(Request(kind), "clock-line", []);

    public static class ScenarioSegmentLinesCarryTheTenseContract
    {
        [Fact]
        public static void The_back_announce_prompt_says_the_track_just_finished_and_demands_past_tense()
        {
            var content = UserContent(SegmentKind.BackAnnounce);

            Assert.Contains("JUST FINISHED playing", content);
            Assert.Contains("past tense", content);
            Assert.Contains("never announce it as upcoming", content);
        }

        [Fact]
        public static void The_lead_in_prompt_says_the_track_is_about_to_play()
        {
            var content = UserContent(SegmentKind.LeadIn);

            Assert.Contains("about to play next", content);
            Assert.Contains("Announce it as upcoming", content);
        }
    }
}
