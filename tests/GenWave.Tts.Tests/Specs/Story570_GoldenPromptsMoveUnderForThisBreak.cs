// STORY-570 — One flow for flagged talk (SPEC F220 · PLAN F220)
//
// AC7 · F220.21 · T796. The DJ-line prompts captured before any shaper moved (Golden/, STORY-567), re-rendered in
// rules form with the real catchphrase-limits and repeat-phrases shapers. The one allowed difference is the same
// instruction text moving into the "For this break" section, which follows the persona section. Wiring the writer to
// this form is T800, which activates ScenarioTheGoldenPromptsAfterTheMove in Story570_OneFlowForFlaggedTalk.

namespace GenWave.Tts.Tests.Specs;

using GenWave.Core.Domain;
using GenWave.Core.TalkRules;
using GenWave.Tts.Tests.Fakes;

public static class FeatureGoldenPromptsMoveUnderForThisBreak
{
    public sealed class ScenarioTheDjLinePromptsInRulesForm : IAsyncLifetime
    {
        IReadOnlyList<GoldenPrompt> legacy = [];
        IReadOnlyList<GoldenPrompt> rules = [];

        public async Task InitializeAsync()
        {
            legacy = await GoldenPromptFixtures.DjLinesAsync();
            rules = await GoldenPromptFixtures.DjLineSystemsInRulesFormAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        /// <summary>Each fixture's rules-form system lines, split at the heading: what stayed, and what sits under it.</summary>
        IEnumerable<(string Name, List<string> Kept, List<string> Moved, List<string> Legacy)> Parts() =>
            rules.Select(prompt =>
            {
                var lines = prompt.System.Split('\n').ToList();
                var heading = lines.IndexOf(TalkShaping.ForThisBreakHeading);
                var kept = heading < 0 ? lines : lines[..heading];
                var moved = heading < 0 ? [] : lines[(heading + 1)..];
                return (prompt.Name, kept, moved, legacy.Single(g => g.Name == prompt.Name).System.Split('\n').ToList());
            });

        static List<string> Without(List<string> lines, List<string> removed)
        {
            var rest = lines.ToList();
            foreach (var line in removed)
                rest.Remove(line);
            return rest;
        }

        [Fact]
        public void EveryDjLineFixtureIsRendered() => Assert.Equal(legacy.Select(p => p.Name), rules.Select(p => p.Name));

        [Fact]
        public void OnlyTheMovedTextIsMissingFromWhatStayed() =>
            Assert.Equal("", string.Join(", ", Parts().Where(p => !Without(p.Legacy, p.Moved).SequenceEqual(p.Kept)).Select(p => p.Name)));

        [Fact]
        public void TheSectionIsExactlyTheLegacyLinesThatDidNotStayInTheirOrder() =>
            Assert.Equal("", string.Join(", ", Parts().Where(p => !Without(p.Legacy, p.Kept).SequenceEqual(p.Moved)).Select(p => p.Name)));

        [Fact]
        public void EachFixtureHasOneForThisBreakSectionAtTheEnd() =>
            Assert.Equal("", string.Join(", ", rules
                .Where(p => p.System.Split('\n').Count(l => l == TalkShaping.ForThisBreakHeading) != 1
                    || !p.System.Split('\n').SkipWhile(l => l != TalkShaping.ForThisBreakHeading).Skip(1).Any())
                .Select(p => p.Name)));

        [Fact]
        public void TheSectionCarriesTheQuirksTheSkipListAndTheRecentLines() =>
            Assert.Equal("", string.Join(", ", Parts()
                .Where(p => !p.Moved.Any(l => l.StartsWith("Quirks: ", StringComparison.Ordinal))
                    || !p.Moved.Any(l => l.StartsWith(LlmPromptBuilder.SaidLatelyPrefix, StringComparison.Ordinal))
                    || !p.Moved.Contains(LlmPromptBuilder.RecentLinesHeading))
                .Select(p => p.Name)));

        [Fact]
        public void TheQuirksComeBeforeTheRecentLines() =>
            Assert.Equal("", string.Join(", ", Parts()
                .Where(p => p.Moved.FindIndex(l => l.StartsWith("Quirks: ", StringComparison.Ordinal)) > p.Moved.IndexOf(LlmPromptBuilder.RecentLinesHeading))
                .Select(p => p.Name)));

        [Fact]
        public void NothingStaysBehindInThePersonaSection() =>
            Assert.Equal("", string.Join(", ", Parts()
                .Where(p => p.Kept.Any(l => l.StartsWith("Quirks: ", StringComparison.Ordinal) || l == LlmPromptBuilder.RecentLinesHeading))
                .Select(p => p.Name)));
    }

    // A card with quirks has a persona section even when every line of it moved under "For this break" (gh-#152)
    public sealed class ScenarioAPersonaWithQuirksButNoSoulMemoryOrName : IAsyncLifetime
    {
        const string Note = "Quirks: says \"stay golden\"";
        string system = "";

        public Task InitializeAsync()
        {
            var card = new PersonaCard(
                PersonaCard.CurrentSchemaVersion, "Flip", "", "", ["says \"stay golden\""], new VoiceSpec("kokoro", "af_heart", 1.0, "en"), 0, [], []);
            var now = new DateTimeOffset(2026, 11, 23, 18, 30, 0, TimeSpan.FromHours(-6));
            var request = new SegmentRequest(
                SegmentKind.LeadIn, "af_heart", "GenWave", new MediaItem("m1", "/media/x.mp3", "Astral Plane", new Loudness(-14.0, -1.0, true), "Valerie June"), now, "gw");
            var shaping = new TalkShaping(new TalkList<string>([Note]), TalkMemos.Empty, TalkList<string>.Empty);
            system = BreakPromptComposer.Compose(new BreakPromptInputs(
                request, null, card, false, null, [], 450, now, [])
            {
                Shaping = shaping,
            }).System;
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ThePersonaOpeningIsUsed() =>
            Assert.StartsWith("You are a radio DJ writing live station patter - write every word in the voice", system, StringComparison.Ordinal);

        [Fact]
        public void ForThisBreakFollowsTheScaffoldWithTheQuirksNote() =>
            Assert.EndsWith("\n\n" + TalkShaping.ForThisBreakHeading + "\n" + Note, system, StringComparison.Ordinal);
    }

    // A shaper note never creates the persona section (F220.10): no soul and no quirks stays neutral
    public sealed class ScenarioAPersonaWithNoSoulAndNoQuirks : IAsyncLifetime
    {
        const string Note = "Said lately, skip this break: \"stay golden\"";
        string[] lines = [];

        public Task InitializeAsync()
        {
            var card = new PersonaCard(
                PersonaCard.CurrentSchemaVersion, "Flip", "", "", [], new VoiceSpec("kokoro", "af_heart", 1.0, "en"), 0, [], []);
            var now = new DateTimeOffset(2026, 11, 23, 18, 30, 0, TimeSpan.FromHours(-6));
            var request = new SegmentRequest(
                SegmentKind.LeadIn, "af_heart", "GenWave", new MediaItem("m1", "/media/x.mp3", "Astral Plane", new Loudness(-14.0, -1.0, true), "Valerie June"), now, "gw");
            var shaping = new TalkShaping(new TalkList<string>([Note]), TalkMemos.Empty, TalkList<string>.Empty);
            lines = BreakPromptComposer.Compose(new BreakPromptInputs(
                request, null, card, true, null, [], 450, now, [])
            {
                Shaping = shaping,
                Memory = new MemoryPacket(["Daisy the dog"]),
            }).System.Split('\n');
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ThereIsNoNameLineAndNoNeverMentionList() =>
            Assert.Equal(
                0,
                lines.Count(l => l == LlmPromptBuilder.NeverMentionHeading || l.Contains("Flip", StringComparison.Ordinal)));

        [Fact]
        public void TheNoteStillAppearsUnderForThisBreak() =>
            Assert.Equal([TalkShaping.ForThisBreakHeading, Note], lines.SkipWhile(l => l != TalkShaping.ForThisBreakHeading).ToArray());

        [Fact]
        public void TheOpeningStaysNeutral() =>
            Assert.StartsWith("You are a personality-neutral radio DJ", lines[0], StringComparison.Ordinal);
    }
}
