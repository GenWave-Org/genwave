// STORY-242 — Upgrading changes nothing on the air: the key leaves the surface
// (SPEC F91.5/F91.6 AC3, PLAN T120)
//
// BDD specification — xUnit. Entry-point discipline: drives the real
// PUT /api/settings through WebApplicationFactory<Program>. Companion to the
// migration facts in Story242_UpgradeChangesNothing.cs (MediaLibrary.Tests).

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
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

/// <summary>
/// Real Program.cs composition root, mirrors <c>Story167_SpectatorModeSetting.cs</c>'s own
/// <c>SpectatorSettingWebFactory</c>: hosted services and the media catalog/persona accessor are
/// swapped for controllable fakes so no Postgres/Liquidsoap connection is ever attempted.
/// </summary>
file sealed class ActiveIdRetiredWebFactory() : WebApplicationFactory<Program>
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
        });
    }
}

public static class FeatureActiveIdKeyRetired
{
    static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsync(
            "/api/auth/login", JsonContent.Create(new { password = ActiveIdRetiredWebFactory.Password }));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    public sealed class ScenarioWritingTheRetiredKey
    {
        // Given the migrated station, When PUT /api/settings writes Station:Persona:ActiveId.

        [Fact]
        public async Task WriteIsRejectedAsUnknownKey()
        {
            await using var factory = new ActiveIdRetiredWebFactory();
            var client = await LoggedInClientAsync(factory);

            var response = await client.PutAsJsonAsync(
                "/api/settings", new[] { new { key = "Station:Persona:ActiveId", value = "0" } });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("Station:Persona:ActiveId", body, StringComparison.Ordinal);
            Assert.Contains("not an operator-editable setting", body, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task SettingsListingNoLongerContainsTheKey()
        {
            await using var factory = new ActiveIdRetiredWebFactory();
            var client = await LoggedInClientAsync(factory);

            var body = JsonDocument.Parse(await client.GetStringAsync("/api/settings")).RootElement;

            Assert.DoesNotContain(body.EnumerateArray(), entry =>
                string.Equals(entry.GetProperty("key").GetString(), "Station:Persona:ActiveId", StringComparison.OrdinalIgnoreCase));
        }
    }
}
