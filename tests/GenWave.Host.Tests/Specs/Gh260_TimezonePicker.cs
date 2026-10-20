// gh-#260 — Station:Timezone is a picker, not free text. GET /api/settings serves the host's own zone
// list (TimeZoneInfo.GetSystemTimeZones) as the key's choices, with "" (the container's own clock)
// as a real, default choice. Entry point: the real Program.cs composition root via
// ShowChoicesWebFactory (fake settings/show stores, no Postgres); every fact reads raw JSON.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GenWave.Host.Api;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureTimezonePicker
{
    const string Key = "Station:Timezone";

    public sealed class ScenarioTheSettingsPageLoads : IDisposable
    {
        // Given: a fresh station; GET /api/settings

        readonly ShowChoicesWebFactory factory;
        readonly JsonElement timezone;
        readonly List<JsonElement> choices;

        public ScenarioTheSettingsPageLoads()
        {
            factory = new ShowChoicesWebFactory([]);
            var client = factory.LoggedInClientAsync().GetAwaiter().GetResult();
            timezone = SettingsResponseJson.GetJson(client, "/api/settings").FindKey(Key);
            choices = timezone.GetProperty("choices").EnumerateArray().ToList();
        }

        public void Dispose() => factory.Dispose();

        [Fact]
        public void IsAChoice() => Assert.Equal("choice", timezone.GetProperty("kind").GetString());

        [Fact]
        public void TheServerClockIsTheFirstAndDefaultChoice()
        {
            var first = choices[0];
            Assert.Equal("", first.GetProperty("value").GetString());
            Assert.True(first.GetProperty("isDefault").GetBoolean());
        }

        [Fact]
        public void EveryHostZoneIsOffered() => Assert.Equal(
            TimeZoneInfo.GetSystemTimeZones().Select(zone => zone.Id).Order(StringComparer.Ordinal),
            choices.Skip(1).Select(c => c.GetProperty("value").GetString()));

        [Fact]
        public void UtcIsLabelledWithItsOffset() => Assert.Equal(
            "UTC (UTC+00:00)",
            choices.Single(c => c.GetProperty("value").GetString() == "UTC").GetProperty("label").GetString());
    }

    public sealed class ScenarioPickingAZone : IDisposable
    {
        // Given: PUT Station:Timezone = America/Edmonton

        readonly ShowChoicesWebFactory factory;
        readonly HttpStatusCode status;
        readonly string? stored;

        public ScenarioPickingAZone()
        {
            factory = new ShowChoicesWebFactory([]);
            var client = factory.LoggedInClientAsync().GetAwaiter().GetResult();
            status = client.PutAsJsonAsync(
                "/api/settings", new[] { new SettingUpdateRequest(Key, "America/Edmonton") })
                .GetAwaiter().GetResult().StatusCode;
            stored = factory.SettingsStore.ReadAllAsync().GetAwaiter().GetResult().GetValueOrDefault(Key);
        }

        public void Dispose() => factory.Dispose();

        [Fact]
        public void IsSaved()
        {
            Assert.True((int)status is >= 200 and < 300, $"PUT returned {status}");
            Assert.Equal("America/Edmonton", stored);
        }
    }

    public sealed class ScenarioAZoneTheHostDoesNotKnow : IDisposable
    {
        // Given: PUT Station:Timezone = Mars/Olympus_Mons

        readonly ShowChoicesWebFactory factory;
        readonly HttpStatusCode status;
        readonly string? stored;

        public ScenarioAZoneTheHostDoesNotKnow()
        {
            factory = new ShowChoicesWebFactory([]);
            var client = factory.LoggedInClientAsync().GetAwaiter().GetResult();
            status = client.PutAsJsonAsync(
                "/api/settings", new[] { new SettingUpdateRequest(Key, "Mars/Olympus_Mons") })
                .GetAwaiter().GetResult().StatusCode;
            stored = factory.SettingsStore.ReadAllAsync().GetAwaiter().GetResult().GetValueOrDefault(Key);
        }

        public void Dispose() => factory.Dispose();

        [Fact]
        public void IsRejected()
        {
            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Null(stored);
        }
    }
}
