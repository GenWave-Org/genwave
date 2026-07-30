// gh-#298 — spectator player: pause must detach the live source; resume rejoins the live head
//
// BDD specification — xUnit. Field-diagnosed (v2.8.11): after pause+resume the audio ran two
// songs behind the live head while every display was correct. Chrome keeps downloading a paused
// progressive stream into its media cache, then resume plays the bank — permanently behind live.
// No server-side bound can help (a paused Chrome keeps reading, so icecast's queue-size never
// trips) and no HTTP header marks a progressive stream "live". The fix teaches OUR player live
// semantics on top of the gh-#114 recovery machinery:
//   - user pause = honest stop: detach the src (removeAttribute + load()), killing both the
//     ongoing background download and the banked buffer;
//   - play with no source attached = rejoin live: reattach through recoverPlayer's existing
//     cache-busted path (one cache-buster in the file, never duplicated);
//   - the end-of-stream subtlety stays: ended fires pause first with player.ended already true,
//     and that early return must now also skip the detach so the ended handler can recover;
//   - MediaSession garnish, fully feature-detected: station metadata + infinite duration so OS
//     controls present the stream as live (no scrubber).
//
// These specs pin the served app.js content (the repo's established idiom for spectator JS
// behavior — the browser half is verified live): substring/shape assertions against the exact
// handler blocks, not a JS runtime.

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;

namespace GenWave.Host.Tests.Specs;

file sealed class LivePauseWebFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Station:SpectatorMode", "true");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-x7z");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
        });
    }
}

public static class FeatureSpectatorPlayerLivePause
{
    static async Task<string> ServedAppJsAsync()
    {
        await using var factory = new LivePauseWebFactory();
        var client = factory.CreateClient();
        return await client.GetStringAsync("/spectator/app.js");
    }

    // Extracts one addEventListener block by slicing from its registration to the next
    // listener's — shape assertions then hold for THAT handler, not just anywhere in the file.
    static string HandlerBlock(string js, string eventName, string nextEventName)
    {
        var start = js.IndexOf($"addEventListener(\"{eventName}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"no {eventName} listener registered in app.js");
        var end = js.IndexOf($"addEventListener(\"{nextEventName}\"", start, StringComparison.Ordinal);
        Assert.True(end > start, $"no {nextEventName} listener after the {eventName} listener");
        return js[start..end];
    }

    static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal);
             i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    // ── HAPPY PATH — pause is an honest stop ─────────────────────────────

    public sealed class ScenarioPauseDetachesTheSource
    {
        [Fact]
        public async Task ThePauseHandlerRemovesTheSrcAndReloads()
        {
            var js = await ServedAppJsAsync();

            var pauseHandler = HandlerBlock(js, "pause", "stalled");

            // Both halves of the detach: removeAttribute alone leaves the current network
            // connection open; load() is what actually aborts Chrome's ongoing download.
            Assert.Contains("player.removeAttribute(\"src\")", pauseHandler, StringComparison.Ordinal);
            Assert.Contains("player.load()", pauseHandler, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ThePauseHandlerStillDisarmsRecoveryFirst()
        {
            // gh-#114's arming contract is unchanged: pause disarms intent and cancels any
            // pending recovery timer — the detach is in addition to, not instead of.
            var js = await ServedAppJsAsync();

            var pauseHandler = HandlerBlock(js, "pause", "stalled");

            Assert.Contains("playIntended = false", pauseHandler, StringComparison.Ordinal);
            Assert.Contains("clearTimeout(recoveryTimer)", pauseHandler, StringComparison.Ordinal);
        }
    }

    // ── HAPPY PATH — play rejoins the live head ──────────────────────────

    public sealed class ScenarioPlayRejoinsLive
    {
        [Fact]
        public async Task ThePlayHandlerReattachesWhenNoSourceIsAttached()
        {
            var js = await ServedAppJsAsync();

            var playHandler = HandlerBlock(js, "play", "playing");

            // Guarded on the src attribute being absent (only the honest-stop pause leaves the
            // element that way) and routed through recoverPlayer — the cache-busted reattach.
            Assert.Contains("!player.getAttribute(\"src\")", playHandler, StringComparison.Ordinal);
            Assert.Contains("recoverPlayer(player)", playHandler, StringComparison.Ordinal);
        }

        [Fact]
        public async Task TheCacheBusterLivesInExactlyOnePlace()
        {
            // The rejoin reuses recoverPlayer rather than composing its own URL — one
            // cache-buster expression in the whole file, so the two paths can never drift.
            var js = await ServedAppJsAsync();

            Assert.Equal(1, Occurrences(js, "reconnect=${Date.now()}"));
        }

        [Fact]
        public async Task ThePlayHandlerStillArmsRecovery()
        {
            var js = await ServedAppJsAsync();

            var playHandler = HandlerBlock(js, "play", "playing");

            Assert.Contains("playIntended = true", playHandler, StringComparison.Ordinal);
        }
    }

    // ── HAPPY PATH — the end-of-stream subtlety survives ─────────────────

    public sealed class ScenarioEndedRecoveryStaysIntact
    {
        [Fact]
        public async Task TheEndedGuardComesBeforeTheDetach()
        {
            // End-of-stream fires pause first with player.ended already true — that pause is the
            // mount dropping, not the user stopping. The early return must precede the detach,
            // or recovery would lose the src it is about to replace and the disarm would kill
            // the ended handler's scheduled attempt.
            var js = await ServedAppJsAsync();

            var pauseHandler = HandlerBlock(js, "pause", "stalled");
            var guardAt = pauseHandler.IndexOf("if (player.ended) return", StringComparison.Ordinal);
            var detachAt = pauseHandler.IndexOf("player.removeAttribute(\"src\")", StringComparison.Ordinal);

            Assert.True(guardAt >= 0, "the pause handler lost its player.ended early return");
            Assert.True(guardAt < detachAt, "the detach runs before the player.ended guard — end-of-stream would tear down the src recovery needs");
        }

        [Fact]
        public async Task TheEndedListenerStillSchedulesRecovery()
        {
            var js = await ServedAppJsAsync();

            Assert.Contains(
                "player.addEventListener(\"ended\", () => schedulePlayerRecovery(player, recoveryDelayMs))",
                js, StringComparison.Ordinal);
        }
    }

    // ── HAPPY PATH — MediaSession garnish, feature-detected ──────────────

    public sealed class ScenarioMediaSessionPresentsLive
    {
        [Fact]
        public async Task MediaSessionIsFeatureDetectedNeverAssumed()
        {
            // Every touch of the API sits behind the presence check — a browser without
            // mediaSession must skip the garnish, not throw.
            var js = await ServedAppJsAsync();

            Assert.Contains("\"mediaSession\" in navigator", js, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ThePositionStateDeclaresAnInfiniteDuration()
        {
            // Infinite duration = live: OS controls drop the scrubber. Optional-chained so an
            // engine without setPositionState skips it rather than throwing.
            var js = await ServedAppJsAsync();

            Assert.Contains("setPositionState?.({ duration: Infinity })", js, StringComparison.Ordinal);
        }

        [Fact]
        public async Task TheMetadataCarriesTheStationMarkAsArtwork()
        {
            // The sharp card-sized logo.png (gh-#258's provenance), not the 32px favicon.
            var js = await ServedAppJsAsync();

            Assert.Contains("navigator.mediaSession.metadata = new MediaMetadata(", js, StringComparison.Ordinal);
            Assert.Contains("artwork: [{ src: STATION_ICON_PATH", js, StringComparison.Ordinal);
        }
    }

    // ── HAPPY PATH — the markup does not fight the detach ────────────────

    public sealed class ScenarioMarkupStaysHonest
    {
        [Fact]
        public async Task ThePlayerKeepsPreloadNone()
        {
            // preload="none" is load-bearing for this fix: the element must never start a
            // download on its own — only an explicit play (or recovery) attaches and fetches.
            await using var factory = new LivePauseWebFactory();
            var client = factory.CreateClient();

            var html = await client.GetStringAsync("/spectator");

            Assert.Contains("id=\"player\" class=\"player\" controls preload=\"none\"", html, StringComparison.Ordinal);
        }
    }
}
