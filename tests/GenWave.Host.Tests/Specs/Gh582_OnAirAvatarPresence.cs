// gh-#582 — "Avatars for announcers swap album/station art during patter"
//
// BDD specification — xUnit. Dean's complaint: the DJ-card face (SPEC F129.3, STORY-335) renders
// at 26px, "tiny to the point of being a waste of space... no amount of squinting makes it
// better." His ruling on the two remedies the issue offered (swap the album-art slot vs. enlarge
// in place): "I think enlarging makes more sense, and swapping a circular avatar for square album
// artwork might not be very visually appealing." This suite pins the enlarged treatment landing on
// the spectator page's own DJ card — the admin-ui On Air card (NowPlayingCard.tsx) never rendered
// any avatar at all and carries no album/station-art slot to swap, so it was never the surface
// this issue describes; the spectator page's .now-playing__art (station/album art, 72px square)
// alongside .dj-card__avatar (the DJ's own circular face, 26px) is the exact pairing Dean's
// comment describes.
//
// No new API field: the now-playing payload already carries kind ("track"/"patter") and
// djAvatarUrl (SPEC F129.2/F129.3, STORY-335/336) — Story335_TheFaceOnThePublicSurface.cs and
// Story336_TheFaceOnTheStream.cs already cover that payload/URL wiring. This suite only pins the
// STATIC page's own rendering logic, mirroring Gh258_SpectatorStationLogo.cs's own
// "no JS test rig by design" idiom (Story335's own top-of-file remarks): fetch the shipped
// /spectator HTML, app.js, and styles.css through WebApplicationFactory<Program> and assert on
// their exact, load-bearing source text rather than executing the script in a browser.

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

/// <summary>Mirrors Gh258_SpectatorStationLogo.cs's own StationLogoWebFactory — the minimal DI
/// shape for reading the spectator page's static assets with no Postgres fixture.</summary>
file sealed class OnAirAvatarWebFactory : WebApplicationFactory<Program>
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

public static class FeatureOnAirAvatarPresence
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — the enlarge-in-place treatment
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheFaceEnlargesForTheActualSpeaker
    {
        [Fact]
        public async Task TheEnlargedClassIsGatedOnBothSpeakingAndARealFace()
        {
            // The one line that decides it all: isSpeaking (kind === "patter") AND a real,
            // successfully-attempted face together — not "has a dj" alone, which also covers an
            // entire music segment naming its scheduled host (SPEC F129.3's own posture).
            await using var factory = new OnAirAvatarWebFactory();
            var client = factory.CreateClient();

            var js = await client.GetStringAsync("/spectator/app.js");

            Assert.Contains(
                "card.classList.toggle(\"dj-card--speaking\", isSpeaking && target !== null);",
                js, StringComparison.Ordinal);
        }

        [Fact]
        public async Task OnlyAPatterKindIsEverPassedAsSpeaking()
        {
            await using var factory = new OnAirAvatarWebFactory();
            var client = factory.CreateClient();

            var js = await client.GetStringAsync("/spectator/app.js");

            Assert.Contains("nowPlaying.kind === \"patter\",", js, StringComparison.Ordinal);
        }

        [Fact]
        public async Task TheStylesheetDoublesTheAvatarPastTheTwentySixPixelByline()
        {
            // 56px is more than double the byline's 26px (Dean: "at least twice the size it is
            // now").
            await using var factory = new OnAirAvatarWebFactory();
            var client = factory.CreateClient();

            var css = await client.GetStringAsync("/spectator/styles.css");

            Assert.Contains(".dj-card--speaking .dj-card__avatar", css, StringComparison.Ordinal);
            Assert.Contains("width: 56px;", css, StringComparison.Ordinal);
        }

        [Fact]
        public async Task TheEnlargedAvatarStaysCircularNotSquare()
        {
            // Dean explicitly ruled out the album-art slot for this reason: "swapping a circular
            // avatar for square album artwork might not be very visually appealing." The enlarged
            // treatment only resizes/re-borders .dj-card__avatar — it never touches that element's
            // inherited border-radius: 999px (the base .dj-card__avatar rule, unchanged), and never
            // touches .now-playing__art (the square album/station-art slot) at all.
            await using var factory = new OnAirAvatarWebFactory();
            var client = factory.CreateClient();

            var css = await client.GetStringAsync("/spectator/styles.css");

            Assert.DoesNotContain("border-radius", ExtractBlock(css, ".dj-card--speaking .dj-card__avatar"));
        }

        static string ExtractBlock(string css, string selector)
        {
            var start = css.IndexOf(selector, StringComparison.Ordinal);
            Assert.True(start >= 0, $"selector '{selector}' not found");
            var openBrace = css.IndexOf('{', start);
            var closeBrace = css.IndexOf('}', openBrace);
            return css[openBrace..closeBrace];
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — RIGHT FACE OR NO FACE: no real avatar means no enlarge
    // ---------------------------------------------------------------------

    public sealed class ScenarioAFacelessPersonaKeepsTodaysArt
    {
        [Fact]
        public async Task ALoadFailureImmediatelyDropsTheSpeakingClassRatherThanWaitingForTheNextTick()
        {
            // A faceless persona (djAvatarUrl null) never sets target !== null in the first place —
            // that half of the gate is covered by TheEnlargedClassIsGatedOnBothSpeakingAndARealFace
            // above. This fact pins the OTHER half: a real URL that fails to load mid-patter must
            // not leave the enlarged frame around an honest "no face" placeholder glyph for even
            // one clock tick.
            await using var factory = new OnAirAvatarWebFactory();
            var client = factory.CreateClient();

            var js = await client.GetStringAsync("/spectator/app.js");

            Assert.Contains(
                "document.getElementById(\"dj-card\").classList.remove(\"dj-card--speaking\");",
                js, StringComparison.Ordinal);
        }

        [Fact]
        public async Task StandbyNeverPassesSpeakingTrue()
        {
            await using var factory = new OnAirAvatarWebFactory();
            var client = factory.CreateClient();

            var js = await client.GetStringAsync("/spectator/app.js");

            Assert.Contains("renderDjCard(null, null, null, false);", js, StringComparison.Ordinal);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — music playback stays exactly as it was
    // ---------------------------------------------------------------------

    public sealed class ScenarioMusicPlaybackIsUntouched
    {
        [Fact]
        public async Task TheBylineAvatarSizeIsUnchangedAtTwentySixPixels()
        {
            // The plain, always-on .dj-card__avatar rule (the show-host byline shown during music
            // too, SPEC F129.3) stays at its shipped 26px — only the .dj-card--speaking modifier
            // above changes size, and it is never applied for kind === "track".
            await using var factory = new OnAirAvatarWebFactory();
            var client = factory.CreateClient();

            var css = await client.GetStringAsync("/spectator/styles.css");

            Assert.Contains("width: 26px;", css, StringComparison.Ordinal);
        }
    }
}
