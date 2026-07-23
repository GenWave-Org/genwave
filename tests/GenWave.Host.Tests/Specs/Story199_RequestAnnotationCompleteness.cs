// STORY-199 — Request annotations carry full metadata
//
// BDD specification — xUnit (SPEC F75.1-F75.2). Implemented PLAN T44 (/build-loop).
//
// Investigation finding: LiquidsoapAnnotationBuilder ALREADY stamps title, artist, and track_id on
// every annotate: line (Story055 AC3; F38.1's artist="" guard) — F75.1's title/artist/id delta was
// already shipped, riding alongside replay_gain. F75.2's id-based correlation was also already
// shipped: PlayoutFeeder's feeder-owned branch (see PlayoutFeeder.cs `pushedMeta`) sources
// Title/Artist from the pushed MediaItem itself — the catalog's display fields — and never from
// Liquidsoap re-parsing the pushed file's own tags. A catalog row for a file with no readable tags
// therefore still carries correct display metadata and correlates by id end to end.
//
// This spec PINS both facts against the shipped seams — LiquidsoapControl + FakeEngineServer
// (Story055's idiom) for the wire-level annotate line, PlayoutFeeder + FakeLiquidsoapControl
// (Story154's idiom) for the tagless-file correlation path — plus a hostile-title escaping theory
// (quotes, commas, colons, backslashes, newlines, unicode) the shipped Escape() had not yet been
// proven against with a quote-aware re-parse of the produced line.

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Playout;
using GenWave.Host.Engine;
using GenWave.Host.Options;
using GenWave.Host.Playout;
using GenWave.Host.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenWave.Host.Tests.Specs;

public static class FeatureRequestAnnotationCompleteness
{
    static readonly GenWave.Core.Domain.Loudness DefaultLoudness = new(-16.0, -1.0, Measurable: true);

    static LiquidsoapControl Control(FakeEngineServer server) =>
        new(new LiquidsoapOptions
            {
                Host = "127.0.0.1",
                Port = server.Port,
                OutputMetadataCommand = "output.icecast.metadata",
            },
            stationId: "st-01",
            new FakeStationIdentityProvider(new StationIdentity("st-01", "GenWave", "af_heart")),
            new ArtworkUrlResolver(
                new FakeOptionsMonitor<StationOptions>(new StationOptions()), new FakeArtworkTokenStore()),
            NullLogger<LiquidsoapControl>.Instance);

    // ---------------------------------------------------------------------
    // F75.1 — annotated URI shape
    // ---------------------------------------------------------------------

    public static class ScenarioAnnotatedUri
    {
        [Fact]
        public static async Task Feeder_push_annotates_title_artist_and_media_id()
        {
            // Given a feeder push of a catalog track
            await using var server = new FakeEngineServer(_ => "77");
            var control = Control(server);
            var item = new MediaItem("501", "/media/501.mp3", "Comfortably Numb", DefaultLoudness,
                Artist: "Pink Floyd");

            await control.PushAsync(item, -2.10, CancellationToken.None);

            // When the request URI is inspected
            var command = Assert.Single(server.Commands);

            // Then it annotates title, artist, and media id alongside replay_gain (F75.1)
            Assert.Contains("track_id=\"501\"", command, StringComparison.Ordinal);
            Assert.Contains("title=\"Comfortably Numb\"", command, StringComparison.Ordinal);
            Assert.Contains("artist=\"Pink Floyd\"", command, StringComparison.Ordinal);
            Assert.Contains("replay_gain=\"-2.10 dB\"", command, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("She said \"hello\"")]
        [InlineData("Track, Remastered")]
        [InlineData("Side A: The Beginning")]
        [InlineData("C:\\Users\\name\\Track")]
        [InlineData("Quote \" then, comma: colon\\backslash")]
        [InlineData("Café — Résumé 日本語 🎵")]
        [InlineData("Trailing backslash\\")]
        public static void HostileTitlesEscapeWithoutBreakingTheAnnotateLine(string hostileTitle)
        {
            // A title/artist containing embedded quotes, commas, colons, or backslashes must not
            // forge a field boundary, inject a bogus annotation key, or bleed into the locator.
            var item = new MediaItem("9", "/media/9.mp3", hostileTitle, DefaultLoudness, Artist: hostileTitle);

            var line = LiquidsoapAnnotationBuilder.Build(item, 0.0, "st-01", "GenWave");
            var (fields, locator) = ParseAnnotateLine(line);

            Assert.Equal(hostileTitle, fields["title"]);
            Assert.Equal(hostileTitle, fields["artist"]);
            Assert.Equal("/media/9.mp3", locator);
        }

        [Theory]
        [InlineData("Line1\r\nLine2")]
        [InlineData("Line1\nLine2\rEnd")]
        public static void NewlinesInHostileTitlesNeverSplitTheSingleLineCommand(string hostileTitle)
        {
            // CR/LF would otherwise split the telnet command across lines and corrupt protocol
            // framing (Story055 AC8) — re-asserted here as part of F75.1's completeness contract.
            var item = new MediaItem("9", "/media/9.mp3", hostileTitle, DefaultLoudness, Artist: hostileTitle);

            var line = LiquidsoapAnnotationBuilder.Build(item, 0.0, "st-01", "GenWave");
            var (fields, locator) = ParseAnnotateLine(line);

            Assert.DoesNotContain('\n', line);
            Assert.DoesNotContain('\r', line);
            Assert.Equal("/media/9.mp3", locator);
            Assert.DoesNotContain('\n', fields["title"]);
            Assert.DoesNotContain('\r', fields["title"]);
        }

        /// <summary>
        /// A minimal, quote-aware reader of the <c>annotate:key="value",...:uri</c> line — mirrors
        /// how Liquidsoap itself must parse it (backslash escapes the following character; an
        /// unescaped quote closes the value; an unescaped colon after the last pair starts the
        /// locator) — so a round trip through the shipped <see cref="LiquidsoapAnnotationBuilder"/>
        /// proves a hostile value cannot escape its own field.
        /// </summary>
        static (IReadOnlyDictionary<string, string> Fields, string Locator) ParseAnnotateLine(string line)
        {
            const string prefix = "annotate:";
            Assert.StartsWith(prefix, line, StringComparison.Ordinal);

            var i = prefix.Length;
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            while (true)
            {
                var eq = line.IndexOf('=', i);
                var key = line[i..eq];
                i = eq + 1;
                Assert.Equal('"', line[i]);
                i++;

                var value = new System.Text.StringBuilder();
                while (line[i] != '"')
                {
                    if (line[i] == '\\') i++;   // backslash escapes the next character literally
                    value.Append(line[i]);
                    i++;
                }
                i++;   // closing quote
                fields[key] = value.ToString();

                if (line[i] == ',') { i++; continue; }
                Assert.Equal(':', line[i]);
                i++;
                break;
            }

            return (fields, line[i..]);
        }
    }

    // ---------------------------------------------------------------------
    // F75.2 — tagless-file correlation via the annotated id
    // ---------------------------------------------------------------------

    public static class ScenarioTaglessCorrelation
    {
        [Fact]
        public static async Task Tagless_file_still_correlates_and_displays_by_annotated_id()
        {
            // Given a track whose file has no readable tags — the catalog row still carries a
            // display title (filename fallback) and no artist (never enriched). PlayoutFeeder only
            // ever reads these from the pushed MediaItem itself; it never re-parses the file.
            var item = new MediaItem("finding-42", "/media/untagged-042.flac",
                "Untitled Track 42", DefaultLoudness, Artist: null);
            var ls = new FakeLiquidsoapControl();
            var feeder = new PlayoutFeeder(ls, new SingleItemProvider(item),
                new FakeRotationSettingsProvider(new RotationSettings()));

            // When it airs
            await feeder.TickAsync(CancellationToken.None);   // boot (safe) → pushes the tagless item
            await feeder.TickAsync(CancellationToken.None);   // the tagless item airs

            var onAir = feeder.CurrentOnAir;
            Assert.NotNull(onAir);

            var nowPlaying = new NowPlayingService();
            nowPlaying.Update("st-01", new NowPlayingSnapshot(
                onAir.MediaId, onAir.Title, onAir.Artist, onAir.GainDb, onAir.StartedAt,
                onAir.DurationMs, IsDrain: !onAir.IsReal));

            // Then now-playing shows correct title/artist and correlates by the annotated id (F75.2)
            var snapshot = nowPlaying.GetSnapshot("st-01");
            Assert.Equal("finding-42", snapshot?.MediaId);
            Assert.Equal("Untitled Track 42", snapshot?.Title);
            Assert.Null(snapshot?.Artist);
        }

        sealed class SingleItemProvider(MediaItem item) : INextItemProvider
        {
            bool yielded;

            public Task<MediaItem?> GetNextAsync(PlayoutContext ctx, CancellationToken ct)
            {
                if (yielded) return Task.FromResult<MediaItem?>(null);
                yielded = true;
                return Task.FromResult<MediaItem?>(item);
            }
        }
    }
}
