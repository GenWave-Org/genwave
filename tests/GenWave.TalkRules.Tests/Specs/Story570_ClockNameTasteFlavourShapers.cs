// STORY-570 — One flow for flagged talk (SPEC F220 · PLAN F220)
//
// T798: the day-and-time clock guard, self-name, vary-taste and show-flavour shapers, run by the real rule book over the real
// rules with fakes at the edges (the name roll, the persona stores, the show-flavour source). The golden-prompt proof (F220.21)
// is in GenWave.Tts.Tests (Story570_GoldenPromptsMoveUnderForThisBreak).

using GenWave.Core.Domain;
using GenWave.Core.TalkRules;
using GenWave.TalkRules.Tests.Fixtures;
using GenWave.Tts;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenWave.TalkRules.Tests.Specs;

public static class FeatureClockNameTasteFlavourShapers
{
    private static readonly PersonaCard Card =
        new(PersonaCard.CurrentSchemaVersion, "Flip", "", "Warm and unhurried.", [], new VoiceSpec("", "af_nova", 1.0, "en"), 0, [], []);

    private static readonly PersonaCard CardWithNothingToSay =
        new(PersonaCard.CurrentSchemaVersion, "Flip", "", "", [], new VoiceSpec("", "af_nova", 1.0, "en"), 0, [], []);

    private static readonly ShowFlavorFact Flavor = new("Night Shift", "The show where the coffee is always cold.");

    private static readonly TalkList<string> ValerieJune = new(["Valerie June"]);

    private static TalkSnapshot Before(string segmentKind = "LeadIn", bool preview = false, TalkList<string>? tasteNotes = null, bool extraLineTaken = false, TalkList<string>? previousTasteNotes = null) =>
        TalkSnapshots.DjLine(string.Empty) with
        {
            SegmentKind = segmentKind,
            Lines = TalkList<TalkLine>.Empty,
            Preview = preview,
            TasteNotes = tasteNotes,
            PreviousTasteNotes = previousTasteNotes,
            ExtraLineTaken = extraLineTaken,
        };

    private static TalkRuleBook BookOf(ITalkRule rule, InMemoryTalkRuleSwitches? switches = null) =>
        new([rule], switches ?? new InMemoryTalkRuleSwitches(), NullLogger<TalkRuleBook>.Instance);

    private static async Task<InMemoryTalkRuleSwitches> OffAsync(string ruleId, TalkKinds kind = TalkKinds.DjLine)
    {
        var switches = new InMemoryTalkRuleSwitches();
        await switches.SetAsync(ruleId, kind, false, CancellationToken.None);
        return switches;
    }

    private static SelfNameRule NameRule(ISelfNameRoll roll, PersonaCard? card = null) =>
        new(roll, new RosterPersonaStore("Flip", "Rusty Strings"), new OneCardSource(card ?? Card));

    private static readonly TalkShaping Nothing = new(TalkList<string>.Empty, TalkMemos.Empty, TalkList<string>.Empty);

    // F138.5 — the clock guard line, from the one builder the legacy system prompt uses
    public sealed class ScenarioTheClockIsShapedForADjLine : IAsyncLifetime
    {
        private TalkShaping shaping = Nothing;

        public async Task InitializeAsync() =>
            shaping = await BookOf(new DayAndTimeRule()).ShapeAsync(Before(), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheNoteIsTheLegacyClockGuardLine() =>
            Assert.Equal(LlmPromptBuilder.BuildClockGuardLine(TalkSnapshots.DjLine(string.Empty).LocalNow), shaping.PromptNotes.Single());
    }

    public sealed class ScenarioTheClockIsShapedForAChat : IAsyncLifetime
    {
        private TalkShaping shaping = Nothing;

        public async Task InitializeAsync() =>
            shaping = await BookOf(new DayAndTimeRule()).ShapeAsync(TalkSnapshots.Chat(), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheNoteIsTheLegacyClockGuardLine() =>
            Assert.Equal(LlmPromptBuilder.BuildClockGuardLine(TalkSnapshots.Chat().LocalNow), shaping.PromptNotes.Single());
    }

    // F220.10 — a switched-off shaper adds nothing
    public sealed class ScenarioTheClockRuleIsSwitchedOff : IAsyncLifetime
    {
        private TalkShaping shaping = Nothing;

        public async Task InitializeAsync() =>
            shaping = await BookOf(new DayAndTimeRule(), await OffAsync("day-and-time")).ShapeAsync(Before(), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoNoteIsOffered() => Assert.Empty(shaping.PromptNotes);
    }

    // gh-#150 — one roll per break, the note is the legacy name line
    public sealed class ScenarioTheNameRollLands : IAsyncLifetime
    {
        private readonly CountingSelfNameRoll roll = new(lands: true);
        private TalkShaping shaping = Nothing;

        public async Task InitializeAsync() =>
            shaping = await BookOf(NameRule(roll)).ShapeAsync(Before(), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheNoteIsTheLegacyNameLine() => Assert.Equal(LlmPromptBuilder.BuildSelfNameMentionLine("Flip"), shaping.PromptNotes.Single());

        [Fact]
        public void TheBreakIsRolledOnce() => Assert.Equal(1, roll.Rolls);
    }

    public sealed class ScenarioTheNameRollMisses : IAsyncLifetime
    {
        private TalkShaping shaping = Nothing;

        public async Task InitializeAsync() =>
            shaping = await BookOf(NameRule(new CountingSelfNameRoll(lands: false))).ShapeAsync(Before(), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoNoteIsOffered() => Assert.Empty(shaping.PromptNotes);
    }

    // F220.10 — a switched-off shaper adds nothing and keeps no state: the roll is not even taken
    public sealed class ScenarioTheNameRuleIsSwitchedOff : IAsyncLifetime
    {
        private readonly CountingSelfNameRoll roll = new(lands: true);
        private TalkShaping shaping = Nothing;

        public async Task InitializeAsync() =>
            shaping = await BookOf(NameRule(roll), await OffAsync("self-name")).ShapeAsync(Before(), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoNoteIsOffered() => Assert.Empty(shaping.PromptNotes);

        [Fact]
        public void NothingIsRolled() => Assert.Equal(0, roll.Rolls);
    }

    // gh-#150 — the name rides only on a persona section
    public sealed class ScenarioThePersonaHasNothingToSay : IAsyncLifetime
    {
        private TalkShaping shaping = Nothing;

        public async Task InitializeAsync() =>
            shaping = await BookOf(NameRule(new CountingSelfNameRoll(lands: true), CardWithNothingToSay)).ShapeAsync(Before(), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoNoteIsOffered() => Assert.Empty(shaping.PromptNotes);
    }

    // F220.9 — a shaper that throws is reported, and the prompt goes without its note
    public sealed class ScenarioTheCardStoreThrowsWhenNaming : IAsyncLifetime
    {
        private TalkShaping shaping = Nothing;

        public async Task InitializeAsync() =>
            shaping = await BookOf(new SelfNameRule(new CountingSelfNameRoll(lands: true), new RosterPersonaStore("Flip"), new ThrowingCardSource()))
                .ShapeAsync(Before(), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheBookReportsCouldNotShape() => Assert.Equal("self-name", string.Join(", ", shaping.CouldNotShape));

        [Fact]
        public void NoNoteIsOffered() => Assert.Empty(shaping.PromptNotes);
    }

    private static readonly TalkList<string> Folk = new(["Folk"]);

    // F83.1 — the same taste notes as the last on-air break: the break is told to vary it
    public sealed class ScenarioThePreviousNotesOverlap : IAsyncLifetime
    {
        private TalkShaping shaping = Nothing;

        public async Task InitializeAsync() =>
            shaping = await BookOf(new VaryTasteRule()).ShapeAsync(Before(tasteNotes: ValerieJune, previousTasteNotes: ValerieJune), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheNoteIsTheLegacyRepeatSentence() => Assert.Equal(LlmPromptBuilder.TasteRepeatNote, shaping.PromptNotes.Single());
    }

    public sealed class ScenarioThePreviousNotesDoNotOverlap : IAsyncLifetime
    {
        private TalkShaping shaping = Nothing;

        public async Task InitializeAsync() =>
            shaping = await BookOf(new VaryTasteRule()).ShapeAsync(Before(tasteNotes: ValerieJune, previousTasteNotes: Folk), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoNoteIsOffered() => Assert.Empty(shaping.PromptNotes);
    }

    public sealed class ScenarioThereAreNoPreviousNotes : IAsyncLifetime
    {
        private TalkShaping shaping = Nothing;

        public async Task InitializeAsync() =>
            shaping = await BookOf(new VaryTasteRule()).ShapeAsync(Before(tasteNotes: ValerieJune), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoNoteIsOffered() => Assert.Empty(shaping.PromptNotes);
    }

    // A preview is left out whatever the writer remembers
    public sealed class ScenarioAPreviewHasOverlappingNotes : IAsyncLifetime
    {
        private TalkShaping shaping = Nothing;

        public async Task InitializeAsync() =>
            shaping = await BookOf(new VaryTasteRule()).ShapeAsync(Before(preview: true, tasteNotes: ValerieJune, previousTasteNotes: ValerieJune), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoNoteIsOffered() => Assert.Empty(shaping.PromptNotes);
    }

    // F220.10 — a switched-off shaper adds nothing (and the rule has no state to keep)
    public sealed class ScenarioTheTasteRuleIsSwitchedOff : IAsyncLifetime
    {
        private TalkShaping shaping = Nothing;

        public async Task InitializeAsync() =>
            shaping = await BookOf(new VaryTasteRule(), await OffAsync("vary-taste"))
                .ShapeAsync(Before(tasteNotes: ValerieJune, previousTasteNotes: ValerieJune), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoNoteIsOffered() => Assert.Empty(shaping.PromptNotes);
    }

    // F116.3 — the due line, from the one builder the legacy user prompt uses
    public sealed class ScenarioAShowLineIsDue : IAsyncLifetime
    {
        private readonly CountingShowFlavorSource source = new(Flavor);
        private TalkShaping shaping = Nothing;

        public async Task InitializeAsync() =>
            shaping = await BookOf(new ShowFlavourRule(source)).ShapeAsync(Before(), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheNoteIsTheLegacyShowLine() => Assert.Equal(LlmPromptBuilder.BuildShowFlavorPatterLine(Flavor), shaping.PromptNotes.Single());

        [Fact]
        public void TheGateIsAskedOnce() => Assert.Equal(1, source.Takes);
    }

    public sealed class ScenarioNoShowLineIsDue : IAsyncLifetime
    {
        private TalkShaping shaping = Nothing;

        public async Task InitializeAsync() =>
            shaping = await BookOf(new ShowFlavourRule(new CountingShowFlavorSource(null))).ShapeAsync(Before(), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoNoteIsOffered() => Assert.Empty(shaping.PromptNotes);
    }

    // F116.3 — context wins: a break whose extra line is taken never even asks, so the cadence window is not spent
    public sealed class ScenarioTheExtraLineIsTaken : IAsyncLifetime
    {
        private readonly CountingShowFlavorSource source = new(Flavor);

        public async Task InitializeAsync() =>
            await BookOf(new ShowFlavourRule(source)).ShapeAsync(Before(extraLineTaken: true), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheGateIsNotAsked() => Assert.Equal(0, source.Takes);
    }

    public sealed class ScenarioAPreviewIsShapedForTheShow : IAsyncLifetime
    {
        private readonly CountingShowFlavorSource source = new(Flavor);

        public async Task InitializeAsync() =>
            await BookOf(new ShowFlavourRule(source)).ShapeAsync(Before(preview: true), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheGateIsNotAsked() => Assert.Equal(0, source.Takes);
    }

    public sealed class ScenarioTheSegmentCarriesNoShowLine : IAsyncLifetime
    {
        private readonly CountingShowFlavorSource source = new(Flavor);

        public async Task InitializeAsync() =>
            await BookOf(new ShowFlavourRule(source)).ShapeAsync(Before("SignOn"), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheGateIsNotAsked() => Assert.Equal(0, source.Takes);
    }

    // F220.10 — a switched-off shaper adds nothing and keeps no state: the cadence window is not spent
    public sealed class ScenarioTheShowRuleIsSwitchedOff : IAsyncLifetime
    {
        private readonly CountingShowFlavorSource source = new(Flavor);
        private TalkShaping shaping = Nothing;

        public async Task InitializeAsync() =>
            shaping = await BookOf(new ShowFlavourRule(source), await OffAsync("show-flavour")).ShapeAsync(Before(), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoNoteIsOffered() => Assert.Empty(shaping.PromptNotes);

        [Fact]
        public void TheGateIsNotAsked() => Assert.Equal(0, source.Takes);
    }

    // F220.9 — a shaper that throws is reported, and the prompt goes without its note
    public sealed class ScenarioTheShowGateThrows : IAsyncLifetime
    {
        private TalkShaping shaping = Nothing;

        public async Task InitializeAsync() =>
            shaping = await BookOf(new ShowFlavourRule(new CountingShowFlavorSource(Flavor) { Throws = true })).ShapeAsync(Before(), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheBookReportsCouldNotShape() => Assert.Equal("show-flavour", string.Join(", ", shaping.CouldNotShape));

        [Fact]
        public void NoNoteIsOffered() => Assert.Empty(shaping.PromptNotes);
    }
}
