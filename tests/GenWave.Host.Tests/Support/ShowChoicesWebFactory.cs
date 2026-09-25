using System.Net.Http.Json;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Configuration;
using GenWave.Host.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace GenWave.Host.Tests.Support;

/// <summary>
/// Host-level fixture for STORY-482/PLAN T585 (moved from <c>Story482_CrosstalkShowCheckboxes.cs</c>,
/// review finding 3 — one top-level type per file) — mirrors
/// <c>Story479_LiveChoiceLists.LiveChoiceListsWebFactory</c>'s own posture exactly, swapping
/// <see cref="IShowStore"/> (the <see cref="ShowChoiceCatalog"/> edge) and
/// <see cref="IStationSettingsStore"/> (so no real Postgres connection is attempted) rather than the
/// LLM/TTS probe edges that factory swaps.
///
/// <paramref name="shows"/> seeds the fake <see cref="IShowStore"/>. <paramref
/// name="crosstalkShowsValue"/>, left <see langword="null"/>, keeps <c>Crosstalk:Shows</c> at its
/// blank C# default; a scenario proving the "saved value missing from the catalog" append (AC3) sets
/// it directly via <see cref="IWebHostBuilder.UseSetting"/> — the identical shortcut
/// <c>LiveChoiceListsWebFactory</c>'s own <c>llmModel</c>/<c>stationVoice</c> parameters use, rather
/// than a PUT round trip, since the claim under test is GET's own append behavior, not PUT.
/// <paramref name="throwOnGetAll"/> scripts the fake store's NEXT (and, since GET resolves
/// <c>Crosstalk:Shows</c> exactly once per request, only) <see cref="IShowStore.GetAllAsync"/> call to
/// throw, proving SPEC F205.7d's "that key only" degrade.
/// </summary>
internal sealed class ShowChoicesWebFactory(
    IReadOnlyList<Show> shows,
    string? crosstalkShowsValue = null,
    bool throwOnGetAll = false)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-t585-show-choices";

    internal FakeShowStore ShowStore { get; } = new(shows);
    internal FakeSettingsStore SettingsStore { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);

        if (crosstalkShowsValue is not null)
            builder.UseSetting("Crosstalk:Shows", crosstalkShowsValue);

        if (throwOnGetAll)
            ShowStore.ThrowOnGetAll = new InvalidOperationException("simulated show store outage");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IStationSettingsStore>();
            services.AddSingleton<IStationSettingsStore>(SettingsStore);

            services.RemoveAll<IShowStore>();
            services.AddSingleton<IShowStore>(ShowStore);
        });
    }

    /// <summary>Logs in via the real POST /api/auth/login round trip (LiveChoiceListsWebFactory's own
    /// idiom) and returns the cookie-bearing client.</summary>
    internal async Task<HttpClient> LoggedInClientAsync()
    {
        var client = CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        if (login.StatusCode != System.Net.HttpStatusCode.NoContent)
            throw new InvalidOperationException($"Login failed unexpectedly: {login.StatusCode}");
        return client;
    }
}
