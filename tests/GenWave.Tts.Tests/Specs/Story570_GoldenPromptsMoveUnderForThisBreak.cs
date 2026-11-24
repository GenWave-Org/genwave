// STORY-570 — One flow for flagged talk (SPEC F220 · PLAN F220)
//
// AC7 · F220.21 · T796, T797, T798. The DJ-line prompts captured before any shaper moved (Golden/, STORY-567), re-rendered in
// rules form with all eight real shapers (T796-T798). The one allowed difference is the same
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
        Dictionary<string, List<string>> legacyUsers = [];
        IReadOnlyList<(string Name, string LegacyUser, string RulesUser)> userPrompts = [];

        public async Task InitializeAsync()
        {
            legacy = await GoldenPromptFixtures.DjLinesAsync();
            legacyUsers = legacy.ToDictionary(p => p.Name, p => p.User.Split('\n').ToList());
            rules = await GoldenPromptFixtures.DjLinesInRulesFormAsync();
            userPrompts = await GoldenPromptFixtures.DjLineUserPromptsInBothFormsAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        static readonly string ClockGuard = LlmPromptBuilder.BuildClockGuardLine(GoldenPromptFixtures.Now);

        /// <summary>Each fixture's rules-form system lines, split at the heading: what stayed, and what sits under it.</summary>
        IEnumerable<(string Name, List<string> Kept, List<string> Moved, List<string> Legacy)> Parts() =>
            rules.Select(prompt =>
            {
                var lines = prompt.System.Split('\n').ToList();
                var heading = lines.IndexOf(TalkShaping.ForThisBreakHeading);
                var kept = heading < 0 ? lines : lines[..heading];
                var moved = heading < 0 ? [] : lines[(heading + 1)..];
                // The clock guard rides the end of the legacy scaffold line; in rules form it is its own line under the heading
                var legacySystem = legacy.Single(g => g.Name == prompt.Name).System.Replace(" " + ClockGuard, "", StringComparison.Ordinal);
                return (prompt.Name, kept, moved, legacySystem.Split('\n').ToList());
            });

        static bool IsShowNote(string line) => line.StartsWith("Show note: this break is airing during", StringComparison.Ordinal);

        /// <summary>Legacy user lines with the taste repeat sentence cut off the end of the taste line (it is its own note in rules form).</summary>
        static List<string> WithoutRepeatSuffix(IEnumerable<string> lines) =>
            lines.Select(l => l.EndsWith(" " + LlmPromptBuilder.TasteRepeatNote, StringComparison.Ordinal)
                ? l[..^(LlmPromptBuilder.TasteRepeatNote.Length + 1)]
                : l).ToList();

        /// <summary>The lines the legacy user prompt carries for the callback ask, and what rules form's user prompt holds of them.</summary>
        List<string> AskLinesOf(string name) => AskLinesIn(legacyUsers[name]);

        static List<string> AskLinesIn(IEnumerable<string> userLines) =>
            userLines.Where(l => l.StartsWith(LlmPromptBuilder.CallbackAskLabel, StringComparison.Ordinal)
                || l == LlmPromptBuilder.CallbackAskInstructionLine).ToList();

        IEnumerable<(string Name, List<string> Ask, List<string> Moved, List<string> RulesUser)> Asks() =>
            Parts().Select(p => (
                p.Name,
                AskLinesOf(p.Name),
                p.Moved,
                rules.Single(r => r.Name == p.Name).User.Split('\n').ToList()));

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

        /// <summary>
        /// The section the moved text must make, in rule-id order (the rule book's order): the callbacks ask, the catchphrase-limits
        /// quirks block, the clock guard, the never-mention list, the repeat-phrases recent lines, the name line, the show note and
        /// the taste repeat sentence. Each block keeps its legacy line order; the legacy system lines that did not stay are cut into
        /// blocks at the shared headings, and the user-prompt texts come from the legacy user prompt.
        /// </summary>
        List<string> ExpectedSection((string Name, List<string> Kept, List<string> Moved, List<string> Legacy) p)
        {
            var left = Without(p.Legacy, p.Kept);
            var nameLines = left.Where(l => l.StartsWith("Name note: ", StringComparison.Ordinal)).ToList();
            left = Without(left, nameLines);
            var recent = left.IndexOf(LlmPromptBuilder.RecentLinesHeading);
            var never = left.IndexOf(LlmPromptBuilder.NeverMentionHeading);
            var recentEnd = never < 0 ? left.Count : never;
            var repeatNote = legacyUsers[p.Name].Any(l => l.EndsWith(" " + LlmPromptBuilder.TasteRepeatNote, StringComparison.Ordinal));
            var blocks = new (string RuleId, IEnumerable<string> Lines)[]
            {
                ("callbacks", AskLinesOf(p.Name)),
                ("catchphrase-limits", left.Take(recent < 0 ? recentEnd : recent)),
                ("day-and-time", [ClockGuard]),
                ("never-mention", never < 0 ? [] : left.Skip(never)),
                ("repeat-phrases", recent < 0 ? [] : left.Skip(recent).Take(recentEnd - recent)),
                ("self-name", nameLines),
                ("show-flavour", legacyUsers[p.Name].Where(IsShowNote)),
                ("vary-taste", repeatNote ? [LlmPromptBuilder.TasteRepeatNote] : []),
            };
            return blocks.OrderBy(b => b.RuleId, StringComparer.Ordinal).SelectMany(b => b.Lines).ToList();
        }

        [Fact]
        public void TheSectionIsExactlyTheMovedTextInRuleIdOrder() =>
            Assert.Equal("", string.Join(", ", Parts().Where(p => !ExpectedSection(p).SequenceEqual(p.Moved)).Select(p => p.Name)));

        [Fact]
        public void TheRulesFormUserPromptIsTheLegacyOneLessEveryMovedLine() =>
            Assert.Equal("", string.Join(", ", userPrompts
                .Where(u => !Without(WithoutRepeatSuffix(u.LegacyUser.Split('\n')), AskLinesIn(u.LegacyUser.Split('\n')).Concat(u.LegacyUser.Split('\n').Where(IsShowNote)).ToList())
                    .SequenceEqual(u.RulesUser.Split('\n')))
                .Select(u => u.Name)));

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
        public void TheNotesFollowTheRuleIdsInOrder() =>
            Assert.Equal("", string.Join(", ", Parts()
                .Where(p => p.Moved.Contains(LlmPromptBuilder.NeverMentionHeading)
                    && !(p.Moved.FindIndex(l => l.StartsWith("Quirks: ", StringComparison.Ordinal)) < p.Moved.IndexOf(LlmPromptBuilder.NeverMentionHeading)
                        && p.Moved.IndexOf(LlmPromptBuilder.NeverMentionHeading) < p.Moved.IndexOf(LlmPromptBuilder.RecentLinesHeading)))
                .Select(p => p.Name)));

        [Fact]
        public void NothingStaysBehindInThePersonaSection() =>
            Assert.Equal("", string.Join(", ", Parts()
                .Where(p => p.Kept.Any(l => l.StartsWith("Quirks: ", StringComparison.Ordinal) || l == LlmPromptBuilder.RecentLinesHeading
                    || l == LlmPromptBuilder.NeverMentionHeading))
                .Select(p => p.Name)));

        [Fact]
        public void TheNeverMentionListIsUnderForThisBreakExactlyWhereLegacyHadIt() =>
            Assert.Equal("", string.Join(", ", Parts()
                .Where(p => p.Legacy.Contains(LlmPromptBuilder.NeverMentionHeading) != p.Moved.Contains(LlmPromptBuilder.NeverMentionHeading))
                .Select(p => p.Name)));

        [Fact]
        public void SomeFixtureCarriesTheNeverMentionList() =>
            Assert.Contains(Parts(), p => p.Moved.Contains(LlmPromptBuilder.NeverMentionHeading));

        [Fact]
        public void TheCallbackAskIsUnderForThisBreakExactlyAsLegacyAskedForIt() =>
            Assert.Equal("", string.Join(", ", Asks().Where(a => !a.Ask.SequenceEqual(a.Moved.Where(a.Ask.Contains))).Select(a => a.Name)));

        [Fact]
        public void EveryFixtureCarriesTheClockGuardUnderForThisBreak() =>
            Assert.Equal("", string.Join(", ", Parts().Where(p => !p.Moved.Contains(ClockGuard)).Select(p => p.Name)));

        [Fact]
        public void SomeFixtureCarriesTheNameLine() =>
            Assert.Contains(Parts(), p => p.Moved.Any(l => l.StartsWith("Name note: ", StringComparison.Ordinal)));

        [Fact]
        public void SomeFixtureCarriesTheShowNote() =>
            Assert.Contains(Parts(), p => p.Moved.Any(IsShowNote));

        [Fact]
        public void SomeFixtureCarriesTheTasteRepeatNote() =>
            Assert.Contains(Parts(), p => p.Moved.Contains(LlmPromptBuilder.TasteRepeatNote));

        [Fact]
        public void SomeFixtureCarriesTheCallbackAsk() => Assert.Contains(Asks(), a => a.Ask.Count == 2);
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
