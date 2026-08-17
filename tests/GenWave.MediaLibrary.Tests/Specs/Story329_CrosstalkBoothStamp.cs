// STORY-329 — The aired script is on the record (SPEC F127.11, PLAN T287)
//
// BDD specification — xUnit, no database (mirrors Story304_AiredKindStamp.cs's own
// NullPersonaAccessor idiom, but stops at the in-memory channel BoothLogWriter.Publish writes
// into — never draining to Postgres, since this file's own concern is purely the STAMP SHAPE
// BoothLogWriter.BuildPickStamp produces, not the write-path SQL wiring Story304's own
// DB-integration facts already cover for segment_kind). GenWave.Orchestration.Tests/Specs/
// Story329_BanterOnTheAir.cs proves the OTHER half of this same chain — that Orchestrator
// composes the aired MediaItem with the right CrosstalkScript in the first place; this project
// has no ProjectReference to GenWave.Orchestration to prove that half itself.

using System.Text.Json;
using System.Threading.Channels;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.MediaLibrary.Station;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureCrosstalkBoothStamp
{
    /// <summary>No-op <see cref="IActivePersonaAccessor"/> double — this file's own facts assert on
    /// <c>pick</c>, not the persona/show stamps (Story215_BoothLogPersonaStamp.cs/Story310_ShowStamp.cs's
    /// own concerns), so a fixed "nothing active" answer keeps every scenario below focused.</summary>
    sealed class NullPersonaAccessor : IActivePersonaAccessor
    {
        public Task<Persona?> ResolveAsync(CancellationToken ct) => Task.FromResult<Persona?>(null);
    }

    /// <summary>Publishes <paramref name="evt"/> through the real <see cref="BoothLogWriter"/> and
    /// reads back the ONE <see cref="BoothLogAppendRequest"/> it queued — no drain, no database (this
    /// file's own concern stops at the stamp <see cref="BoothLogWriter"/> itself builds).</summary>
    static async Task<BoothLogAppendRequest> PublishAndCaptureAsync(StationEvent evt)
    {
        var channel = Channel.CreateBounded<BoothLogAppendRequest>(1);
        var writer = new BoothLogWriter(channel.Writer, new NullPersonaAccessor(), NullLogger<BoothLogWriter>.Instance);

        writer.Publish(evt);

        return await channel.Reader.ReadAsync();
    }

    static CrosstalkAiredScript SampleScript() => new(
    [
        new CrosstalkAiredLine(CrosstalkSpeaker.Host, "Did you catch that new single?", false),
        new CrosstalkAiredLine(CrosstalkSpeaker.Neighbor, "I did — it's on repeat over here.", true),
    ]);

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheAiredScriptIsOnTheRecord
    {
        [Fact]
        public static async Task ACrosstalkRowStampsTheFullScriptAsItsPick()
        {
            // Given a TrackAired event carrying a Crosstalk item's full script — PlayoutFeeder's own
            // forwarded MediaItem.CrosstalkScript (SPEC F127.11)...
            var script = SampleScript();
            var crosstalkAiring = new TrackAired(
                "tts:crosstalk:abc123", "GenWave", "Nova", -2.0, DateTimeOffset.UtcNow, 6_000,
                SegmentKind: SegmentKind.Crosstalk)
            {
                CrosstalkScript = script,
            };

            // When it flows through the real writer...
            var request = await PublishAndCaptureAsync(crosstalkAiring);

            // Then the queued row's pick jsonb decodes back to the SAME script, line for line — "what
            // did they say" is answerable from the booth log alone (F127.11), through the ONE
            // canonical (de)serialization every writer/reader of this shape must share.
            Assert.NotNull(request.Pick);
            var decoded = CrosstalkAiredScriptSerializer.Deserialize(request.Pick);
            Assert.NotNull(decoded);
            Assert.Equal(script.Lines, decoded.Lines); // element-wise (record List equality is reference-only)
        }

        [Fact]
        public static async Task ACrosstalkRowsPickIsTheDocumentedWireShape()
        {
            // Given the same TrackAired event as above...
            var script = SampleScript();
            var crosstalkAiring = new TrackAired(
                "tts:crosstalk:abc123", "GenWave", "Nova", -2.0, DateTimeOffset.UtcNow, 6_000,
                SegmentKind: SegmentKind.Crosstalk)
            {
                CrosstalkScript = script,
            };

            // When it flows through the real writer...
            var request = await PublishAndCaptureAsync(crosstalkAiring);

            // Then the queued row's pick is the LITERAL wire shape
            // CrosstalkAiredScriptSerializer's own doc comment promises —
            // {"lines":[{"speaker":"Host",...}]} — read here at the representation level (raw JSON
            // text), NOT by round-tripping back through the same serializer that produced it (the fact
            // above already covers round-trip fidelity and is representation-blind: it would still
            // pass even if CrosstalkSpeaker serialized as its positional int 0 instead of "Host", since
            // Deserialize would decode its own numeric output right back to CrosstalkSpeaker.Host).
            Assert.NotNull(request.Pick);
            using var document = JsonDocument.Parse(request.Pick);

            var topLevelProperties = document.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();
            Assert.Equal(new HashSet<string> { "lines" }, topLevelProperties);

            var firstLine = document.RootElement.GetProperty("lines")[0];
            Assert.Equal("Host", firstLine.GetProperty("speaker").GetString());
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnOrdinaryTrackCarriesNoCrosstalkStamp
    {
        [Fact]
        public static async Task AMusicRowsPickStaysNull()
        {
            // Given an ordinary music TrackAired — no PersonaPick (persona-off), no CrosstalkScript
            // (not a Crosstalk row) — the everyday shape every pre-F127 airing already has
            var musicAiring = new TrackAired("42", "Night Drive", "The Waveforms", -2.5, DateTimeOffset.UtcNow, 214_000);

            // When it flows through the real writer...
            var request = await PublishAndCaptureAsync(musicAiring);

            // Then the queued row's pick stays null — neither stamp shape applies
            Assert.Null(request.Pick);
        }
    }

    public sealed class ScenarioOffSchemaJsonNeverDeserializesToANullLinesRecord
    {
        [Fact]
        public static void DeserializeRejectsOffSchemaJsonRatherThanReturningANullLinesRecord()
        {
            // Given "{}" — valid JSON, but carrying neither a "lines" property nor anything else this
            // shape expects (round-2 review F9, the sibling BoothLogPickStampSerializer's own
            // documented trap: JSON binds a record's constructor parameters by reflection, not
            // through the record's own constructor, so Lines would otherwise deserialize to null
            // despite its own non-nullable annotation)...
            var decoded = CrosstalkAiredScriptSerializer.Deserialize("{}");

            // Then the serializer refuses to hand back a broken, null-Lines record — it answers null,
            // the same "off-schema means null" contract the JSON-literal-null case already has, so
            // every caller (BoothLogController's own crosstalk-first dispatch, F3) can trust a non-null
            // result actually has usable Lines.
            Assert.Null(decoded);
        }
    }
}
