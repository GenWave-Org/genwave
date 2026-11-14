// STORY-536 AC3 — the fact the copy actually read is the one the DJ remembers (SPEC F218.15 · PLAN T702)
//
// BDD specification — xUnit. The predicate (CopyClaims.UsesFact) shares the truth gate's own claim rules, and the
// producers carry its answer out: LlmCopyWriter stamps SegmentCopy.FactRead on ACCEPTED copy only,
// TtsSegmentSource copies it onto SegmentGenerated.

using System.Net;
using System.Text;
using System.Text.Json;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Tts;
using GenWave.Tts.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GenWave.Tts.Tests.Specs;

public static class FeatureTheGateMatchedFact
{
    const string Fact = "high of 4 °C with sleet";
    const string AcceptedCopy = "Watch out for sleet tonight, folks.";
    const string ContextFacts = "Edmonton: overcast, 15°C. Today's high 21°C, low 12°C.";
    const string ContextAcceptedCopy = "It's overcast today at 15 degrees with a high of 21 and a low of 12.";
    const string ContextPoisonedCopy =
        "It feels like 6 degrees below freezing with plenty of sunshine and today is saturday here in the studio.";

    static readonly DateTimeOffset StationLocalNow = new(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);

    // ── The predicate ───────────────────────────────────────────────────────────

    public sealed class ScenarioACopyRepeatsTheNumber
    {
        readonly bool used = CopyClaims.UsesFact("Four? Try 4 degrees out there.", Fact);

        [Fact]
        public void TheFactCountsAsRead() => Assert.True(used);
    }

    public sealed class ScenarioACopyNamesTheCondition
    {
        readonly bool used = CopyClaims.UsesFact("Watch out for sleet tonight.", Fact);

        [Fact]
        public void TheFactCountsAsRead() => Assert.True(used);
    }

    public sealed class ScenarioACopyIgnoresTheFact
    {
        readonly bool used = CopyClaims.UsesFact("Here is a song about the sea.", Fact);

        [Fact]
        public void TheFactDoesNotCountAsRead() => Assert.False(used);
    }

    public sealed class ScenarioACopyRoundsADecimalTheFactCarries
    {
        // The gate supports copy "4" from fact "4.5"; so must the predicate.
        readonly bool used = CopyClaims.UsesFact("Around 4 degrees tonight.", "high of 4.5 °C");

        [Fact]
        public void TheFactCountsAsRead() => Assert.True(used);
    }

    public sealed class ScenarioTheStationNameCarriesTheNumber
    {
        // "Channel 4" is the station naming itself, not the DJ reading a "4" out of the fact.
        const string Copy = "This is Channel 4, enjoy the evening.";
        const string FourFact = "high of 4 °C";

        readonly bool withTheName = CopyClaims.UsesFact(Copy, FourFact, "Channel 4");
        readonly bool withoutTheName = CopyClaims.UsesFact(Copy, FourFact);

        [Fact]
        public void TheNumberInsideTheNameDoesNotCountAsRead() => Assert.False(withTheName);

        [Fact]
        public void TheSameCopyWithoutAStationNameDoesCountAsRead() => Assert.True(withoutTheName);
    }

    // ── LlmCopyWriter stamps FactRead on accepted copy ──────────────────────────

    public sealed class ScenarioThePatterFactLaneStampsAcceptedCopy : IAsyncLifetime
    {
        FactReadOnAir? accepted;
        FactReadOnAir? fallback;

        public async Task InitializeAsync()
        {
            accepted = (await Write(LeadIn(), AcceptedCopy, new ContextPatterFact("weather", Fact))).FactRead;
            fallback = (await Write(LeadIn(), reply: null, new ContextPatterFact("weather", Fact))).FactRead;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void AcceptedCopyThatUsedTheFactNamesIt() => Assert.Equal(new FactReadOnAir("weather", Fact), accepted);

        [Fact]
        public void ATemplateFallbackNamesNoFact() => Assert.Null(fallback);
    }

    public sealed class ScenarioTheContextSegmentLaneStampsAcceptedCopy : IAsyncLifetime
    {
        FactReadOnAir? accepted;
        FactReadOnAir? fallback;

        public async Task InitializeAsync()
        {
            accepted = (await Write(ContextRequest(), ContextAcceptedCopy, patterFact: null)).FactRead;
            // Poisoned copy fails the truth gate on the ask and the re-ask, so the template airs.
            fallback = (await Write(ContextRequest(), ContextPoisonedCopy, patterFact: null)).FactRead;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void AcceptedCopyThatUsedTheFactsNamesThem() => Assert.Equal(ContextFacts, accepted?.Text);

        [Fact]
        public void ATemplateFallbackNamesNoFact() => Assert.Null(fallback);
    }

    public sealed class ScenarioATrackNamedLikeAConditionIsNotTheFactBeingRead : IAsyncLifetime
    {
        // The LeadIn lane never runs the truth gate's fact check, so UsesFact alone decides: a song called
        // "Snow Patrol" (or by an artist of that name) is not the DJ reading "snow" off the forecast.
        const string SnowFact = "Edmonton: snow, high of 2 °C";

        FactReadOnAir? titleOnly;
        FactReadOnAir? artistOnly;
        FactReadOnAir? realClaim;

        public async Task InitializeAsync()
        {
            titleOnly = (await Write(LeadInFor("Snow Patrol", "Someone"), "Up next, Snow Patrol, a good one.", Snow())).FactRead;
            artistOnly = (await Write(LeadInFor("Run", "Snow Patrol"), "Up next, Run by Snow Patrol.", Snow())).FactRead;
            realClaim = (await Write(LeadInFor("Snow Patrol", "Someone"), "Snow Patrol next, and snow all night.", Snow())).FactRead;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        static ContextPatterFact Snow() => new("weather", SnowFact);

        [Fact]
        public void ATitleCarryingTheConditionWordIsNotRead() => Assert.Null(titleOnly);

        [Fact]
        public void AnArtistCarryingTheConditionWordIsNotRead() => Assert.Null(artistOnly);

        [Fact]
        public void CopyThatReallyClaimsTheSnowOutsideTheTrackIsRead() => Assert.Equal(new FactReadOnAir("weather", SnowFact), realClaim);
    }

    public sealed class ScenarioAStationNameCarryingTheNumberIsNotTheFactBeingRead : IAsyncLifetime
    {
        const string FourFact = "high of 4 °C";

        FactReadOnAir? onlyTheName;
        FactReadOnAir? theNameAndARealClaim;

        public async Task InitializeAsync()
        {
            var channel4 = LeadInFor("Astral Plane", "Valerie June") with { StationName = "Channel 4" };
            onlyTheName = (await Write(channel4, "You're listening to Channel 4 tonight.", new ContextPatterFact("weather", FourFact))).FactRead;
            theNameAndARealClaim = (await Write(channel4, "Channel 4 here, and it's 4 degrees out.", new ContextPatterFact("weather", FourFact))).FactRead;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheNumberInsideTheStationNameIsNotRead() => Assert.Null(onlyTheName);

        [Fact]
        public void ANumberClaimedOutsideTheStationNameIsRead() => Assert.Equal(new FactReadOnAir("weather", FourFact), theNameAndARealClaim);
    }

    // ── TtsSegmentSource carries it onto SegmentGenerated ───────────────────────

    public sealed class ScenarioTheRenderEventCarriesTheFact : IAsyncLifetime
    {
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();
        FactReadOnAir? published;

        public async Task InitializeAsync()
        {
            var events = new RecordingStationEventSink();
            var source = new TtsSegmentSource(
                new FactStampingCopyWriter(new FactReadOnAir("weather", Fact)),
                synth,
                new FakeLoudnessAnalyzer(),
                new FakeCueAnalyzer(),
                NoCorrections.Provider(),
                NoCorrections.PersonaCache(),
                NoCorrections.PronunciationProvider(),
                NoCorrections.PersonaPronunciationCache(),
                NoCorrections.PersonaPaceCache(),
                new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" }),
                NullLogger<TtsSegmentSource>.Instance,
                events);

            await source.RenderAsync(LeadIn(), CancellationToken.None);
            published = events.Events.OfType<SegmentGenerated>().Single().FactRead;
        }

        public Task DisposeAsync()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
            return Task.CompletedTask;
        }

        [Fact]
        public void SegmentGeneratedNamesTheFact() => Assert.Equal(new FactReadOnAir("weather", Fact), published);
    }

    // ── Fixtures ────────────────────────────────────────────────────────────────

    sealed class FactStampingCopyWriter(FactReadOnAir fact) : ISegmentCopyWriter
    {
        public Task<SegmentCopy> WriteAsync(SegmentRequest request, CancellationToken ct) =>
            Task.FromResult(new SegmentCopy("Some patter copy", FreshPerAiring: false) { FactRead = fact });
    }

    static SegmentRequest LeadIn() =>
        new(SegmentKind.LeadIn, "af_heart", "GenWave",
            new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
            StationLocalNow, "test-station");

    static SegmentRequest LeadInFor(string title, string artist) =>
        new(SegmentKind.LeadIn, "af_heart", "GenWave",
            new MediaItem("m1", "/media/x.mp3", title, default, artist),
            StationLocalNow, "test-station");

    static SegmentRequest ContextRequest() =>
        new(SegmentKind.ContextSegment, "af_heart", "GenWave", Track: null, StationLocalNow, "test-station",
            PersonaName: null, CounterpartName: null, ContextFacts: ContextFacts);

    /// <summary>One real LlmCopyWriter render; a null <paramref name="reply"/> is an endpoint fault (the template fallback airs).</summary>
    static async Task<SegmentCopy> Write(SegmentRequest request, string? reply, ContextPatterFact? patterFact)
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(
            reply is null
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = reply } } } }),
                        Encoding.UTF8, "application/json"),
                }));
        var patterFacts = new FakeContextPatterFactSource();
        if (patterFact is { } fact)
            patterFacts.Enqueue(fact);

        var writer = new LlmCopyWriter(
            new TemplateCopyWriter(new PatterTemplateRenderer()),
            new SingleHandlerHttpClientFactory(handler),
            new TestOptionsMonitor<LlmOptions>(new LlmOptions
            {
                Endpoint = "http://fake-llm.local", Model = "test-model", TimeoutSeconds = 5, MaxCopyChars = 450,
            }),
            new LlmCopyStatusHolder(),
            new FakeActivePersonaAccessor(),
            new CapturingLogger<LlmCopyWriter>(),
            TimeProvider.System,
            new LlmCallRecorder(
                new LlmCallRing(new TestOptionsMonitor<LlmOptions>(new LlmOptions())),
                new LlmCallCauseCounters(TimeProvider.System)),
            new FakeDegradationModeReader(),
            new FakeStationClockProvider(StationLocalNow),
            patterFactSource: patterFacts);

        return await writer.WriteAsync(request, CancellationToken.None);
    }
}
