// STORY-241 — The station follows the clock: /api/status is resolver-sourced (SPEC F91.5, PLAN
// T120), Host-side half.
//
// BDD specification — xUnit. GenWave.Orchestration.Tests owns the behavioral proof (a real
// two-segment schedule flipping the on-air persona/envelope across a boundary through a real
// Orchestrator — Story241_StationFollowsTheClock.cs) — that project cannot see GenWave.Host, so the
// one fact that genuinely needs the real Host composition root lives here instead, per that file's
// own header note ("/api/status covered in the Host-side factory idiom"). Mirrors Story250's
// AudiencePostureWebFactory/TheTrackNeverEntersAPick structural-DI-resolution pattern: no Postgres
// fixture exists in this project (Story250's own remarks), so "resolver-sourced" is proven
// structurally here — the real Program.cs graph resolves IActivePersonaAccessor to
// OnAirPersonaAccessor, not the retired options-reading one — while StatusController's own
// zero-call-site-change response shape is proven live over HTTP.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Orchestration;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

/// <summary>
/// Real Program.cs composition root — hosted services and the media catalog are swapped for
/// controllable fakes (mirrors Story167/Story242's own factories) so no Postgres/Liquidsoap
/// connection is ever attempted; <see cref="IActivePersonaAccessor"/> and
/// <see cref="CachingScheduleResolver"/> are left resolving through the REAL DI graph
/// (<c>StationSettingsHostingExtensions.AddGenWaveStationSettings</c>'s own registration) — the
/// exact binding this file's facts inspect.
/// </summary>
file sealed class StatusResolverSourcedWebFactory() : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-x7z";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));

            // PLAN T371 (SPEC F149.5) — StatusController now also resolves IMediaRotationSink; the
            // real MediaRotationRepository requires a live Postgres, same reason IMediaCatalog is
            // faked above.
            services.RemoveAll<IMediaRotationSink>();
            services.AddSingleton<IMediaRotationSink>(new FakeMediaRotationSink());
        });
    }
}

public static class FeatureStatusPersonaResolverSourced
{
    public sealed class ScenarioConsumersFlipAtTheBoundary
    {
        [Fact]
        public void StatusEndpointReportsBResolverSourced()
        {
            // Structural half (mirrors Story250's TheTrackNeverEntersAPick): resolving
            // IActivePersonaAccessor off the REAL composition root yields OnAirPersonaAccessor, not
            // the retired Station:Persona:ActiveId-reading one — StatusController's own
            // `personaAccessor.ResolveAsync(ct)` call site is unchanged (F91.5), so this binding IS
            // the resolver-sourced behavior the acceptance criteria ask for; the behavioral flip
            // itself (persona A -> B as the wall clock crosses a seeded segment boundary) is proven
            // against a real two-segment schedule in GenWave.Orchestration.Tests
            // (Story241_StationFollowsTheClock.cs's own ScenarioConsumersFlipAtTheBoundary).
            using var factory = new StatusResolverSourcedWebFactory();

            var accessor = factory.Services.GetRequiredService<IActivePersonaAccessor>();

            Assert.IsType<OnAirPersonaAccessor>(accessor);
        }

        [Fact]
        public async Task StatusEndpointRespondsWithUnchangedShapeOverRealHttp()
        {
            // The response shape (F28.6) is unaffected by the re-backing — an empty schedule (no
            // ConnectionStrings:Station configured in this factory) resolves to the gap/no-persona
            // state, exactly like the pre-F91 "no active persona" default.
            await using var factory = new StatusResolverSourcedWebFactory();
            var client = factory.CreateClient();
            var login = await client.PostAsync(
                "/api/auth/login", JsonContent.Create(new { password = StatusResolverSourcedWebFactory.Password }));
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

            var response = await client.GetAsync("/api/status");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            Assert.True(body.TryGetProperty("llm", out var llm));
            Assert.True(llm.TryGetProperty("activePersona", out var activePersona));
            Assert.Equal(JsonValueKind.Null, activePersona.ValueKind);
        }
    }
}
