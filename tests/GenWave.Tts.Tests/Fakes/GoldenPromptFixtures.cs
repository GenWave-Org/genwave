namespace GenWave.Tts.Tests.Fakes;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

/// <summary>
/// STORY-567 fixture set: one DJ line per LLM-authored segment kind, one chat exchange and one ad, each run
/// through the real writer with fakes only at the I/O edges (HTTP, stores, clock, randomness). The first
/// completion request each writer posts is the captured prompt pair.
/// </summary>
static class GoldenPromptFixtures
{
    const long HostId = 7;
    const long NeighborId = 8;
    const string Reply = "Coming up next a classic from the vault.";
    const string SecondReply = "Here is something warm to keep the evening moving along.";
    static readonly DateTimeOffset Now = new(2026, 11, 23, 18, 30, 0, TimeSpan.FromHours(-6));
    static readonly LlmOptions Llm = new() { Endpoint = "http://fake-llm.local", Model = "m", TimeoutSeconds = 5, MaxCopyChars = 450 };

    static readonly MediaItem Track = new(
        "m1", "/media/x.mp3", "Astral Plane", new Loudness(-14.0, -1.0, true), "Valerie June",
        Album: "The Moon and Stars", Genre: "Folk", Year: 2013, DurationMs: 215000);

    static readonly SoundTagOffer SoundTags = new(["[laughs]", "[sighs]"], 2);

    static readonly string[] Quirks =
    [
        "Sign off now and then with \"stay golden, friends\" in a warm voice",
        "Compare every record to a sandwich",
        "Say \"you're welcome\" after any long ramble",
    ];

    public static async Task<IReadOnlyList<GoldenPrompt>> DjLinesAsync()
    {
        var prompts = new List<GoldenPrompt>();
        foreach (var fixture in DjLineFixtures())
            prompts.Add(await CaptureDjLineAsync(fixture));
        return prompts;
    }

    public static async Task<GoldenPrompt> ChatAsync()
    {
        var users = new List<string>();
        var systems = new List<string>();
        var store = SeededStore();
        var writer = new CrosstalkScriptWriter(
            QuirkSpecHarness.ScriptedHttp(users, [Reply], systems),
            new TestOptionsMonitor<LlmOptions>(Llm),
            new TestOptionsMonitor<CrosstalkOptions>(new CrosstalkOptions()),
            QuirkSpecHarness.NewRecorder(), new FakeDegradationModeReader(),
            NullLogger<CrosstalkScriptWriter>.Instance, TimeProvider.System,
            quirkSelector: new FixedQuirks(), repetitionCheck: new RepetitionCheck(), utteranceStore: store,
            soundTagOffers: new FixedSoundTags());

        await writer.WriteExchangeAsync(
            new CrosstalkExchangeRequest(
                Card("Nova"), Card("Rex"), "GenWave", ShowName: "Night Shift", Daypart: "late night",
                StationLocalNow: Now, HostPersonaId: HostId, NeighborPersonaId: NeighborId, ShowId: 3),
            CancellationToken.None);

        return new GoldenPrompt("chat", systems[0], users[0]);
    }

    public static async Task<GoldenPrompt> AdAsync()
    {
        var users = new List<string>();
        var systems = new List<string>();
        var writer = new AdScriptWriter(
            QuirkSpecHarness.ScriptedHttp(users, [Reply], systems),
            new TestOptionsMonitor<LlmOptions>(Llm),
            QuirkSpecHarness.NewRecorder(), new FakeDegradationModeReader(),
            NullLogger<AdScriptWriter>.Instance, TimeProvider.System);

        await writer.WriteAsync(
            new AdScriptWriteRequest(
                "Cravin's Diner", "A retro diner with a twist", "warm and playful", 30, AudiencePosture.Everyone, 200, 0.4,
                Tagline: "Pie at midnight", About: "Family run since 1974, open all night.",
                Phone: "555-0142", Address: "12 Main Street", Website: "cravins.example", HouseTone: "dry and friendly"),
            static _ => new AdScriptValidationOutcome.Accepted(), CancellationToken.None);

        return new GoldenPrompt("ad", systems[0], users[0]);
    }

    sealed record DjLineFixture(string Name, SegmentRequest Request)
    {
        public ContextPatterFact? Fact { get; init; }
        public ShowFlavorFact? Flavor { get; init; }
        public string? Announcement { get; init; }

        /// <summary>True when the writer first airs an earlier break, so the captured one is its second (writer state shows).</summary>
        public bool CapturesSecondBreak { get; init; }
    }

    static IEnumerable<DjLineFixture> DjLineFixtures()
    {
        var likedRule = new TasteRule(new TastePredicate("Valerie June", null, null), new TasteContext([], null, null), 0.8);
        yield return new("djline-leadin", Request(SegmentKind.LeadIn))
        {
            Fact = new ContextPatterFact("weather", "It is minus four and snowing in town."),
        };
        yield return new("djline-backannounce", Request(SegmentKind.BackAnnounce))
        {
            Flavor = new ShowFlavorFact("Night Shift", "The show where the coffee is always cold."),
        };
        yield return new("djline-signoff", Request(SegmentKind.SignOff) with
        {
            CounterpartName = "Rusty Strings",
            CounterpartShowName = "Early Birds",
            Handoff = new HandoffParticipants(
                new CrosstalkParticipant(HostId, "DJ Nova"), new CrosstalkParticipant(NeighborId, "Rusty Strings"), Now),
        });
        yield return new("djline-signon", Request(SegmentKind.SignOn) with { CounterpartName = "Rusty Strings" });
        yield return new("djline-contextsegment", Request(SegmentKind.ContextSegment) with
        {
            ContextFacts = "Forecast: minus four, light snow, wind 12 km/h from the northwest.",
            ContextProviderKey = "weather",
        });
        yield return new("djline-announcement", Request(SegmentKind.Announcement))
        {
            Announcement = "The garage sale is on Saturday at nine.",
        };
        yield return new("djline-leadin-taste", Request(SegmentKind.LeadIn, PickedFor(likedRule)));
        yield return new("djline-leadin-taste-repeat", Request(SegmentKind.LeadIn, PickedFor(likedRule)))
        {
            CapturesSecondBreak = true,
        };
        yield return new("djline-leadin-exploration", Request(SegmentKind.LeadIn, PickedFor(likedRule) with { IsExploration = true }));
    }

    static PersonaPickDiagnostics PickedFor(TasteRule rule) => new(12, [0.9, 0.7, 0.6], [rule], IsExploration: false);

    static SegmentRequest Request(SegmentKind kind, PersonaPickDiagnostics? pick = null) =>
        new(kind, "af_heart", "GenWave", pick is null ? Track : Track with { PersonaPick = pick }, Now, "test-station")
        {
            PersonaId = HostId,
            ShiftKeyText = ShiftKey.Unscheduled(HostId, new DateOnly(2026, 11, 23)).ToString(),
            ShowName = "Night Shift",
        };

    static async Task<GoldenPrompt> CaptureDjLineAsync(DjLineFixture fixture)
    {
        var users = new List<string>();
        var systems = new List<string>();
        var facts = new FakeContextPatterFactSource();
        if (fixture.Fact is { } fact)
            facts.Enqueue(fact);
        var flavors = new FakeShowFlavorLineSource();
        if (fixture.Flavor is { } flavor)
            flavors.Enqueue(flavor);

        var writer = new LlmCopyWriter(
            new TemplateCopyWriter(new PatterTemplateRenderer()),
            QuirkSpecHarness.ScriptedHttp(users, [Reply, SecondReply], systems),
            new TestOptionsMonitor<LlmOptions>(Llm),
            new LlmCopyStatusHolder(), new FakeActivePersonaAccessor { Card = Card("DJ Nova") },
            NullLogger<LlmCopyWriter>.Instance, new FakeTimeProvider(Now),
            QuirkSpecHarness.NewRecorder(), new FakeDegradationModeReader(),
            stationClock: new FakeStationClockProvider(Now),
            patterFactSource: facts, showFlavorLineSource: flavors,
            utteranceStore: SeededStore(), quirkSelector: new FixedQuirks(),
            repetitionCheck: new RepetitionCheck(), taglineLedger: new TaglineLedger(),
            contextWindow: new FixedContextWindow(MemoryBudget.Standard), memoryRecall: new FixedMemoryRecall(),
            callbacks: new BreakCallbacks(
                new CallbackPlanner(new RecordingCallbackStore("has a dog named Daisy"), new ZeroRandom()),
                new FixedRate(6), new FakePersonaRoster("DJ Nova", "Rusty Strings"), NullLogger<BreakCallbacks>.Instance),
            soundTagOffers: new FixedSoundTags(), selfNameRoll: new ZeroRandom());

        var captureFrom = 0;
        if (fixture.CapturesSecondBreak)
        {
            await writer.WriteAsync(fixture.Request, CancellationToken.None);
            captureFrom = users.Count;
        }

        if (fixture.Announcement is { } message)
            await writer.WriteAnnouncementAsync(fixture.Request, message, CancellationToken.None);
        else
            await writer.WriteAsync(fixture.Request, CancellationToken.None);

        return new GoldenPrompt(fixture.Name, systems[captureFrom], users[captureFrom]);
    }

    static PersonaCard Card(string name) =>
        new(PersonaCard.CurrentSchemaVersion, name, "Radio for the long way home",
            "Warm, unhurried and a little dry. Loves a slow fade and a good story.", Quirks,
            new VoiceSpec("kokoro", "af_heart", 1.0, "en"), 0.4,
            ["Once rebuilt the transmitter out of a toaster"], [])
        {
            QuirkRules = [new QuirkRule(Quirks[1], MaxPerShift: 1), new QuirkRule(Quirks[0], PerBreakChance: 0.5, MaxPerHour: 1)],
        };

    static RecordingUtteranceStore SeededStore()
    {
        var store = new RecordingUtteranceStore();
        var shift = ShiftKey.Unscheduled(HostId, new DateOnly(2026, 11, 23));
        store.Seeded.Add(Seed(3, HostId, "That one goes out to the night owls, and stay golden, friends.", shift, TimeSpan.FromMinutes(5)));
        store.Seeded.Add(Seed(2, HostId, "Here is a slow one that tastes a lot like a grilled cheese.", shift, TimeSpan.FromMinutes(20)));
        store.Seeded.Add(Seed(1, NeighborId, "Welcome back everybody, the lights are low and the records are warm.", shift, TimeSpan.FromMinutes(12)));
        return store;
    }

    static UtteranceEntry Seed(long id, long personaId, string text, ShiftKey shift, TimeSpan age) =>
        new(id, personaId, null, UtteranceKind.Patter, "LeadIn", shift, text, false, Now - age, null);

    sealed class ZeroRandom : Random
    {
        public override double NextDouble() => 0.0;
    }

    sealed class FixedRate(int perHour) : ICallbackRate
    {
        public int CallbacksPerHour => perHour;
    }

    sealed class FixedSoundTags : ISoundTagOfferProvider
    {
        public SoundTagOffer? Current => SoundTags;
    }

    sealed class FixedQuirks : IQuirkSelector
    {
        public Task<QuirkSelection> SelectAsync(
            long? personaId, PersonaCard? card, ShiftKey? shiftKey, bool spend, CancellationToken ct) =>
            Task.FromResult(new QuirkSelection([Quirks[0], Quirks[1]], [Quirks[2]]));
    }

    sealed class FixedMemoryRecall : IMemoryRecallSource
    {
        public Task<MemoryPacket> NeverMentionAsync(long personaId, MemoryBudget budget, CancellationToken ct) =>
            Task.FromResult(new MemoryPacket(["the night the transmitter failed", "Daisy the dog"]));
    }
}
