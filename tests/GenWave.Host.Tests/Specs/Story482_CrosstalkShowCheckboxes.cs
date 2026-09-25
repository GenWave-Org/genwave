// STORY-482 — Crosstalk shows as checkboxes (gh-#778 · SPEC F205.7 amended 2026-09-24 · PLAN T585)
//
// BDD specification — xUnit. Entry point: GET/PUT /api/settings through the real Program.cs
// composition root (WebApplicationFactory<Program>, the LiveChoiceListsWebFactory pattern from
// Story479_LiveChoiceLists.cs — see Support/ShowChoicesWebFactory.cs), with IShowStore/
// IStationSettingsStore swapped for fakes so no real Postgres connection is attempted. Every
// GET-driving fact reads the response as raw JSON (JsonDocument, via Support/SettingsResponseJson.cs)
// — exact camelCase field names (kind/choices/choicesFailed, value/label) — never round-tripped back
// through SettingDto, so a renamed wire field cannot pass silently (mirrors T580 review finding R1).

using System.Net.Http.Json;
using System.Text.Json;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Host.Configuration;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureCrosstalkShowCheckboxes
{
    static Show NewShow(long id, string name, string slug) =>
        new(id, name, slug, Tagline: null, Flavor: null, ImportedFrom: null, ImportedAt: null,
            CreatedAt: DateTime.UtcNow, UpdatedAt: DateTime.UtcNow);

    public sealed class ScenarioThreeShowRows : IDisposable
    {
        // Given: fake IShowStore → morning-drive "Morning Drive", late-late "Late Late", jazz-hour "Jazz Hour";
        //        GET /api/settings

        readonly ShowChoicesWebFactory factory;
        readonly JsonElement shows;

        public ScenarioThreeShowRows()
        {
            factory = new ShowChoicesWebFactory(
            [
                NewShow(1, "Morning Drive", "morning-drive"),
                NewShow(2, "Late Late", "late-late"),
                NewShow(3, "Jazz Hour", "jazz-hour"),
            ]);
            var client = factory.LoggedInClientAsync().GetAwaiter().GetResult();
            shows = SettingsResponseJson.GetJson(client, "/api/settings").FindKey("Crosstalk:Shows");
        }

        public void Dispose() => factory.Dispose();

        /// <summary>AC1 — choices are the three (slug, name) pairs, ordered the same way
        /// IShowStore.GetAllAsync's own "ordered by name" contract does (alphabetically by name, not
        /// insertion order): Jazz Hour, Late Late, Morning Drive.</summary>
        [Fact]
        public void ListsTheShows() => Assert.Equal(
            [("jazz-hour", "Jazz Hour"), ("late-late", "Late Late"), ("morning-drive", "Morning Drive")],
            shows.GetProperty("choices").EnumerateArray()
                .Select(c => (c.GetProperty("value").GetString(), c.GetProperty("label").GetString())));

        /// <summary>AC2 — kind "multi-choice"</summary>
        [Fact]
        public void IsAMultiChoice() => Assert.Equal("multi-choice", shows.GetProperty("kind").GetString());
    }

    public sealed class ScenarioASavedShowThatWasDeleted : IDisposable
    {
        // Given: Crosstalk:Shows = ["gone-show"]; fake IShowStore without it; GET /api/settings

        readonly ShowChoicesWebFactory factory;
        readonly List<(string? Value, string? Label)> choices;

        public ScenarioASavedShowThatWasDeleted()
        {
            factory = new ShowChoicesWebFactory([], crosstalkShowsValue: "[\"gone-show\"]");
            var client = factory.LoggedInClientAsync().GetAwaiter().GetResult();
            var shows = SettingsResponseJson.GetJson(client, "/api/settings").FindKey("Crosstalk:Shows");
            choices = shows.GetProperty("choices").EnumerateArray()
                .Select(c => (c.GetProperty("value").GetString(), c.GetProperty("label").GetString()))
                .ToList();
        }

        public void Dispose() => factory.Dispose();

        /// <summary>AC3 — choices include ("gone-show", "gone-show (not found)")</summary>
        [Fact]
        public void AppendsTheSavedSlug() => Assert.Contains(("gone-show", "gone-show (not found)"), choices);
    }

    public sealed class ScenarioSavingASlugArray : IDisposable
    {
        // Given: PUT Crosstalk:Shows = ["morning-drive"]; the settings row read back off the store

        readonly ShowChoicesWebFactory factory;
        readonly string? storedValue;

        public ScenarioSavingASlugArray()
        {
            factory = new ShowChoicesWebFactory([NewShow(1, "Morning Drive", "morning-drive")]);
            var client = factory.LoggedInClientAsync().GetAwaiter().GetResult();
            var response = client.PutAsJsonAsync(
                "/api/settings", new[] { new SettingUpdateRequest("Crosstalk:Shows", "[\"morning-drive\"]") })
                .GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            var stored = factory.SettingsStore.ReadAllAsync().GetAwaiter().GetResult();
            storedValue = stored.GetValueOrDefault("Crosstalk:Shows");
        }

        public void Dispose() => factory.Dispose();

        /// <summary>AC6 — the stored value is the JSON array ["morning-drive"], byte-identical to
        /// what was sent — no migration to a different persisted shape (SPEC F205.7a).</summary>
        [Fact]
        public void StoresTheSameShape() => Assert.Equal("[\"morning-drive\"]", storedValue);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioASavedArrayWithBlanksAndDuplicates : IDisposable
    {
        // Given: Crosstalk:Shows = ["", "gone-show", "gone-show", "old-show"]; neither row exists;
        //        GET /api/settings (round-1 review finding 1 — SPEC F205.7e: a blank value is never
        //        appended, and a slug repeated in the saved array is appended only once)

        readonly ShowChoicesWebFactory factory;
        readonly List<(string? Value, string? Label)> choices;

        public ScenarioASavedArrayWithBlanksAndDuplicates()
        {
            factory = new ShowChoicesWebFactory(
                [], crosstalkShowsValue: "[\"\",\"gone-show\",\"gone-show\",\"old-show\"]");
            var client = factory.LoggedInClientAsync().GetAwaiter().GetResult();
            var shows = SettingsResponseJson.GetJson(client, "/api/settings").FindKey("Crosstalk:Shows");
            choices = shows.GetProperty("choices").EnumerateArray()
                .Select(c => (c.GetProperty("value").GetString(), c.GetProperty("label").GetString()))
                .ToList();
        }

        public void Dispose() => factory.Dispose();

        /// <summary>SPEC F205.7e — a slug repeated in the saved array is appended exactly once, not
        /// once per repetition.</summary>
        [Fact]
        public void AppendsARepeatedSlugOnce() =>
            Assert.Single(choices, c => c == ("gone-show", "gone-show (not found)"));

        /// <summary>SPEC F205.7e — "each missing slug is appended": a second, distinct missing slug in
        /// the same saved array is appended too, not just the first one encountered.</summary>
        [Fact]
        public void AppendsEveryDistinctMissingSlug() =>
            Assert.Contains(("old-show", "old-show (not found)"), choices);

        /// <summary>SPEC F205.7e — "A blank value is never appended": the leading "" in the saved
        /// array produces no choice with a blank or null value.</summary>
        [Fact]
        public void NeverAppendsABlankValue() =>
            Assert.DoesNotContain(choices, c => string.IsNullOrEmpty(c.Value));
    }

    public sealed class ScenarioTheShowStoreIsDown : IDisposable
    {
        // Given: fake IShowStore.GetAllAsync throws; GET /api/settings

        readonly ShowChoicesWebFactory factory;
        readonly JsonElement body;

        public ScenarioTheShowStoreIsDown()
        {
            factory = new ShowChoicesWebFactory([], throwOnGetAll: true);
            var client = factory.LoggedInClientAsync().GetAwaiter().GetResult();
            body = SettingsResponseJson.GetJson(client, "/api/settings");
        }

        public void Dispose() => factory.Dispose();

        /// <summary>AC7 — Crosstalk:Shows has choicesFailed</summary>
        [Fact]
        public void FlagsTheFailure() => Assert.True(body.FindKey("Crosstalk:Shows").GetProperty("choicesFailed").GetBoolean());

        /// <summary>AC7 — every other key still returns: one Crosstalk:Shows failure never shrinks
        /// the response below the full allowlisted key count (200, not a partial/failed page).</summary>
        [Fact]
        public void ServesTheRestOfThePage() => Assert.Equal(StationSettingsAllowlist.All.Count, body.GetArrayLength());
    }
}
