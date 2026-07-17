// STORY-146 — Duration on the air surfaces (Epic X / SPEC F50.3, closes gitea-#218) — DTO wire half.
// The feeder half lives in Core.Tests/Specs/Story146_FeederStampsDuration.cs; the card/history UI
// in admin-ui/__specs__/now-playing-duration.spec.tsx.
//
// BDD specification — xUnit. LiveController is constructed in-process against the real
// NowPlayingService/PlayHistoryService (both plain in-memory stores, no DB/telnet dependency) —
// mirrors Story084's StatusController pattern rather than standing up a full WebApplicationFactory,
// since neither collaborator needs faking. The anonymous response objects are serialized and
// re-parsed as JSON, the same shape the wire carries (Story084's AsJson helper).

using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using GenWave.Host.Api;
using GenWave.Host.Options;
using GenWave.Host.Playout;

namespace GenWave.Host.Tests.Specs;

public static class FeatureDurationOnAirDtos
{
    static PlayHistoryService MakeHistory() =>
        new(new FakeOptionsMonitor<AdminOptions>(new AdminOptions()));

    static LiveController MakeController(NowPlayingService? nowPlaying = null, PlayHistoryService? history = null) =>
        new(nowPlaying ?? new NowPlayingService(), history ?? MakeHistory());

    /// <summary>Serializes an <see cref="OkObjectResult"/>'s value and parses it back as JSON — the
    /// same shape the wire would carry, without spinning up a full HTTP pipeline (mirrors Story084).</summary>
    static JsonElement AsJson(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return JsonDocument.Parse(JsonSerializer.Serialize(ok.Value)).RootElement;
    }

    public sealed class ScenarioTheAirSurfacesCarryDuration
    {
        [Fact]
        public void NowPlayingCarriesDurationMsForAFeederPushedTrack()
        {
            var nowPlaying = new NowPlayingService();
            nowPlaying.Update(SingleStation.IdString, new NowPlayingSnapshot(
                MediaId: "m1", Title: "Song One", Artist: "Artist A", GainDb: -1.5,
                StartedAt: DateTimeOffset.UtcNow, DurationMs: 225_000, IsDrain: false));
            var controller = MakeController(nowPlaying: nowPlaying);

            var result = controller.GetNowPlaying();

            Assert.Equal(225_000, AsJson(result).GetProperty("durationMs").GetInt32());
        }

        [Fact]
        public void PlayHistoryEntriesCarryDurationMs()
        {
            var history = MakeHistory();
            history.Push(new PlayHistoryEntry(
                SingleStation.IdString, "m1", "Song One", "Artist A", -1.5,
                DateTimeOffset.UtcNow, EndedAt: null, DurationMs: 225_000));
            var controller = MakeController(history: history);

            var result = controller.GetPlayHistory();

            var entry = Assert.Single(AsJson(result).EnumerateArray());
            Assert.Equal(225_000, entry.GetProperty("durationMs").GetInt32());
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioDurationlessPlaysSerializeNull
    {
        [Fact]
        public void AnEngineInitiatedPlaySerializesNullDuration()
        {
            // Engine-initiated plays (safe rotation, engine echo) never carry a fabricated duration
            // (SPEC F50.2) — the snapshot's DurationMs is null all the way to the wire.
            var nowPlaying = new NowPlayingService();
            nowPlaying.Update(SingleStation.IdString, new NowPlayingSnapshot(
                MediaId: "engine-1", Title: "Please Stand By", Artist: "Test Station", GainDb: -2.5,
                StartedAt: DateTimeOffset.UtcNow, DurationMs: null, IsDrain: false));
            var controller = MakeController(nowPlaying: nowPlaying);

            var result = controller.GetNowPlaying();

            Assert.Equal(JsonValueKind.Null, AsJson(result).GetProperty("durationMs").ValueKind);
        }

        [Fact]
        public void ATtsHistoryEntrySerializesNullDuration()
        {
            // tts:* patter items are not catalog rows — no duration to stamp (F50.6).
            var history = MakeHistory();
            history.Push(new PlayHistoryEntry(
                SingleStation.IdString, "tts:seg", "GenWave", "GenWave", 0.0,
                DateTimeOffset.UtcNow, EndedAt: null, DurationMs: null));
            var controller = MakeController(history: history);

            var result = controller.GetPlayHistory();

            var entry = Assert.Single(AsJson(result).EnumerateArray());
            Assert.Equal(JsonValueKind.Null, entry.GetProperty("durationMs").ValueKind);
        }
    }
}
