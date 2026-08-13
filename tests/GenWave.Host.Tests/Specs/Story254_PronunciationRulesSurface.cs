// STORY-254 — A place to edit how the DJ says it (gh-#284)
//
// SPEC F97.3, F100.3. Pronunciation rules are operator data (F68.5 posture) but today the
// only way to author them is a JSON blob in settings. This is the surface: rows, not a blob.
//
// Two things make it more than a CRUD page:
//
//   1. THE MERGE IS VISIBLE. Rules come from the station setting AND the active persona card,
//      with the persona winning (F97.4). An operator staring at a station rule that is being
//      shadowed by a card rule, with no indication of why it isn't working, is exactly the
//      confusion this surface exists to prevent.
//   2. HIT COUNTS LAND HERE. F100.3 rules that facts go where the operator is already
//      looking rather than into a new panel — so "is my rule firing?" is answered on the row
//      itself.
//
// The API is driven through the production endpoint (WebApplicationFactory), not the
// controller class: a rules list that works in a unit test and 404s in production is the
// failure mode this file guards against.
//
// PLAN T144 review (round 2): rules are addressed by CONTENT identity (Pattern, Word), never
// array position — PUT/DELETE take that identity via query parameters
// (?pattern=&word=), never a path segment. Station-row uniqueness on that identity is enforced
// at write time (POST/PUT collision -> 409), and a stale reference (a row another tab already
// deleted) 404s rather than silently acting on whatever now occupies that position.

namespace GenWave.Host.Tests.Specs;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Configuration;
using GenWave.Tts;
using ContextPronunciationRule = GenWave.Core.Domain.PronunciationRule;

// ── In-process fakes (PLAN T144) ─────────────────────────────────────────────────────────────────
// Mirrors Story186_CorrectionsObservability's own fakes exactly (file-scoped there too, redefined
// here rather than shared — established convention for this suite).

file sealed class PronunciationsConfigurationProvider : ConfigurationProvider
{
    public void SetAndReload(string key, string value)
    {
        Set(key, value);
        OnReload();
    }
}

file sealed class PronunciationsConfigurationSource(PronunciationsConfigurationProvider provider) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => provider;
}

/// <summary><see cref="IStationSettingsStore"/> test double standing in for a live Postgres
/// <c>station.settings</c> table — also drives a REAL live reload of the app's own
/// <see cref="IConfiguration"/>, exactly like Story186's <c>ObservabilitySettingsStore</c>, so a
/// write through <c>PronunciationsController</c> is visible to the very next request with no
/// process restart, the identical F19 live-reload contract production carries.</summary>
file sealed class PronunciationsSettingsStore : IStationSettingsStore
{
    readonly PronunciationsConfigurationProvider provider = new();

    public PronunciationsSettingsStore(IConfiguration configuration)
    {
        ((IConfigurationBuilder)configuration).Add(new PronunciationsConfigurationSource(provider));
    }

    public Task WriteAsync(string key, object value, CancellationToken cancellationToken = default)
    {
        if (!StationSettingsAllowlist.ByKey.ContainsKey(key))
            throw new ArgumentException($"Key '{key}' is not on the station settings allowlist.", nameof(key));

        provider.SetAndReload(key, value?.ToString() ?? string.Empty);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}

/// <summary>Answers a fixed active persona card (or none) on every call — the narrowest double for
/// <see cref="ActivePersonaPronunciationRulesCache.RefreshIfStaleAsync"/> to resolve through, mirrors
/// GenWave.Tts.Tests' own file-scoped persona-accessor doubles.</summary>
file sealed class FakePersonaAccessor(PersonaCard? card) : IActivePersonaAccessor
{
    public Task<Persona?> ResolveAsync(CancellationToken ct) => Task.FromResult<Persona?>(null);
    public Task<PersonaCard?> ResolveCardAsync(CancellationToken ct) => Task.FromResult(card);
}

/// <summary>Boots the real host with the two fakes above swapped in — real routing/auth, real
/// <see cref="PronunciationsController"/>, real <c>PronunciationRuleSet</c>/<c>PronunciationRuleHitStats</c>
/// singletons from <c>TtsServiceCollectionExtensions.AddGenWaveTts</c>. <paramref name="activeCard"/>
/// stands in for the active persona's card (SPEC F97.3's other half) — <see langword="null"/> (the
/// default) means no active persona.</summary>
file sealed class PronunciationsWebFactory(PersonaCard? activeCard = null) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-pr0n";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IStationSettingsStore>();
            services.AddSingleton<IStationSettingsStore>(sp =>
                new PronunciationsSettingsStore(sp.GetRequiredService<IConfiguration>()));

            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakePersonaAccessor(activeCard));
        });
    }
}

// ── Specs ────────────────────────────────────────────────────────────────────────────────────────

/// <summary>Wire shape of one row from <c>GET /api/pronunciations</c> — mirrors
/// GenWave.Host.Api.PronunciationRuleDto without depending on it directly.</summary>
file sealed record PronunciationRuleRow(
    string Pattern, string Word, string Ipa, string Source, bool InEffect, long? HitCount, string? Reason);

/// <summary>Wire shape of a successful <c>POST</c>/<c>PUT /api/pronunciations</c> body (gh-#491) —
/// mirrors GenWave.Host.Api.PronunciationRuleWriteResponse without depending on it directly.</summary>
file sealed record PronunciationRuleWriteResponseBody(PronunciationRuleRow Rule, List<string> Warnings);

public static class FeaturePronunciationRulesSurface
{
    // Widened to the base WebApplicationFactory<Program> type: a file-local type (PronunciationsWebFactory)
    // cannot appear in a member SIGNATURE of this public type, though its Password constant can still
    // be read below (an expression, not a signature) — one source of truth for the login password.
    static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = PronunciationsWebFactory.Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    static object RuleBody(string pattern, string ipa, string? word = null) => new { pattern, word, ipa };

    /// <summary>The content-addressed PUT/DELETE route (T144 review F1/F2) — query-string, never a
    /// path segment, so pattern/word text carrying spaces or other path-hostile characters still
    /// round-trips through ordinary percent-encoding.</summary>
    static string RuleRoute(string pattern, string word) =>
        $"/api/pronunciations?pattern={Uri.EscapeDataString(pattern)}&word={Uri.EscapeDataString(word)}";

    /// <summary>Seeds <c>Tts:Pronunciations</c> directly through the raw settings API — bypassing
    /// <c>PronunciationsController</c>'s own <c>PronunciationRuleValidator</c> guard, exactly the way
    /// legacy data or a hand-edit through <c>PUT /api/settings</c> could leave behind a rule that
    /// <c>SettingValidator</c>'s shape check accepts but never compiles (T144 review F3).</summary>
    static async Task SeedRawStationRulesAsync(HttpClient client, string json)
    {
        var response = await client.PutAsJsonAsync("/api/settings", new[]
        {
            new { key = "Tts:Pronunciations", value = json },
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public static class ScenarioTheListIsFirstClass
    {
        [Fact]
        public static async Task The_endpoint_returns_rules_as_rows()
        {
            // GET the real route through WebApplicationFactory<Program>.
            await using var factory = new PronunciationsWebFactory();
            var client = await LoggedInClientAsync(factory);
            await client.PostAsJsonAsync("/api/pronunciations", RuleBody("MacLeod", "/x/"));

            var rows = await client.GetFromJsonAsync<List<PronunciationRuleRow>>("/api/pronunciations");

            Assert.Contains(rows!, r => r.Pattern == "MacLeod");
        }

        [Fact]
        public static async Task A_rule_can_be_created()
        {
            await using var factory = new PronunciationsWebFactory();
            var client = await LoggedInClientAsync(factory);

            var response = await client.PostAsJsonAsync("/api/pronunciations", RuleBody("MacLeod", "/x/"));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        [Fact]
        public static async Task A_rule_can_be_edited()
        {
            await using var factory = new PronunciationsWebFactory();
            var client = await LoggedInClientAsync(factory);
            await client.PostAsJsonAsync("/api/pronunciations", RuleBody("MacLeod", "/old/"));

            await client.PutAsJsonAsync(RuleRoute("MacLeod", "MacLeod"), RuleBody("MacLeod", "/new/"));

            var rows = await client.GetFromJsonAsync<List<PronunciationRuleRow>>("/api/pronunciations");
            Assert.Equal("new", rows!.Single(r => r.Pattern == "MacLeod").Ipa);
        }

        [Fact]
        public static async Task A_rule_can_be_removed()
        {
            await using var factory = new PronunciationsWebFactory();
            var client = await LoggedInClientAsync(factory);
            await client.PostAsJsonAsync("/api/pronunciations", RuleBody("MacLeod", "/x/"));

            await client.DeleteAsync(RuleRoute("MacLeod", "MacLeod"));

            var rows = await client.GetFromJsonAsync<List<PronunciationRuleRow>>("/api/pronunciations");
            Assert.DoesNotContain(rows!, r => r.Pattern == "MacLeod");
        }
    }

    public static class ScenarioTheMergedViewShowsWhichSourceWon
    {
        [Fact]
        public static async Task Each_row_names_its_source()
        {
            // station | persona — the operator must be able to see where a rule came from.
            await using var factory = new PronunciationsWebFactory();
            var client = await LoggedInClientAsync(factory);
            await client.PostAsJsonAsync("/api/pronunciations", RuleBody("MacLeod", "/x/"));

            var rows = await client.GetFromJsonAsync<List<PronunciationRuleRow>>("/api/pronunciations");

            Assert.Equal("station", rows!.Single(r => r.Pattern == "MacLeod").Source);
        }

        [Fact]
        public static async Task A_shadowed_station_rule_is_marked_as_not_in_effect()
        {
            // The confusion this surface exists to prevent (F97.4): a card rule shares the SAME
            // (pattern, word) identity as a station rule, so the card wins and the station rule is
            // shadowed rather than firing.
            var card = new PersonaCard(
                PersonaCard.CurrentSchemaVersion, "Test Persona", "", "", [],
                new VoiceSpec("kokoro", "af_heart", 1.0, "en"), EnergyDisposition: 0, [], [],
                Pronunciations: [new ContextPronunciationRule("MacLeod", "MacLeod", "/cardIpa/")]);
            await using var factory = new PronunciationsWebFactory(card);
            var client = await LoggedInClientAsync(factory);
            await client.PostAsJsonAsync("/api/pronunciations", RuleBody("MacLeod", "/stationIpa/"));

            var rows = await client.GetFromJsonAsync<List<PronunciationRuleRow>>("/api/pronunciations");

            Assert.False(rows!.Single(r => r.Source == "station" && r.Pattern == "MacLeod").InEffect);
        }

        [Fact]
        public static async Task Each_row_carries_its_hit_count()
        {
            await using var factory = new PronunciationsWebFactory();
            var client = await LoggedInClientAsync(factory);
            await client.PostAsJsonAsync("/api/pronunciations", RuleBody("MacLeod", "/x/"));

            // Records a real fire against the SAME PronunciationRuleHitStats singleton the render
            // path (PronunciationRuleHitReporter, T142) increments — the store this endpoint joins
            // against, per the T142 review ruling ("Snapshot() is your read seam; join it against a
            // fresh merge at request time").
            factory.Services.GetRequiredService<PronunciationRuleHitStats>().RecordFired("MacLeod", "MacLeod");

            var rows = await client.GetFromJsonAsync<List<PronunciationRuleRow>>("/api/pronunciations");

            Assert.Equal(1, rows!.Single(r => r.Pattern == "MacLeod").HitCount);
        }
    }

    // -------------------------------------------------------------------------------------
    // CONTENT IDENTITY IS THE TOTAL ADDRESSING SCHEME (T144 review findings F1/F2)
    // -------------------------------------------------------------------------------------
    public static class ScenarioContentIdentityIsEnforced
    {
        [Fact]
        public static async Task A_duplicate_pattern_and_word_is_rejected_with_conflict()
        {
            // The reachable defect the review probe found: a second POST for the same identity used
            // to commit anyway, aliasing two rows onto the same array position.
            await using var factory = new PronunciationsWebFactory();
            var client = await LoggedInClientAsync(factory);
            await client.PostAsJsonAsync("/api/pronunciations", RuleBody("MacLeod", "/first/"));

            var second = await client.PostAsJsonAsync("/api/pronunciations", RuleBody("MacLeod", "/second/"));

            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        }

        [Fact]
        public static async Task Editing_a_rule_to_collide_with_another_is_rejected_with_conflict()
        {
            await using var factory = new PronunciationsWebFactory();
            var client = await LoggedInClientAsync(factory);
            await client.PostAsJsonAsync("/api/pronunciations", RuleBody("Alpha", "/alpha/"));
            await client.PostAsJsonAsync("/api/pronunciations", RuleBody("Beta", "/beta/"));

            // Renaming Beta onto Alpha's identity would leave two rows claiming the same (pattern, word).
            var response = await client.PutAsJsonAsync(RuleRoute("Beta", "Beta"), RuleBody("Alpha", "/beta/"));

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact]
        public static async Task Deleting_an_already_deleted_rule_returns_not_found()
        {
            // The stale-two-tabs case (review PROBE4): a second delete for a row already gone must
            // 404, never silently act on whatever now occupies that position.
            await using var factory = new PronunciationsWebFactory();
            var client = await LoggedInClientAsync(factory);
            await client.PostAsJsonAsync("/api/pronunciations", RuleBody("MacLeod", "/x/"));
            await client.DeleteAsync(RuleRoute("MacLeod", "MacLeod"));

            var second = await client.DeleteAsync(RuleRoute("MacLeod", "MacLeod"));

            Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        }
    }

    // -------------------------------------------------------------------------------------
    // DROPPED RULES STAY VISIBLE (T144 review finding F3)
    // -------------------------------------------------------------------------------------
    public static class ScenarioDroppedRulesAreVisible
    {
        [Fact]
        public static async Task A_rule_that_never_compiled_still_appears_as_a_row()
        {
            // SettingValidator's own shape guard accepts a blank/absent ipa (Story253_PronunciationsSettingShape)
            // — PronunciationRuleSet.Create silently drops it at compile time. The operator must never
            // see an empty list over this non-empty setting, and Reason must name the actual offending
            // field (F3's whole point — "it's broken" without saying why is not much better than gone).
            await using var factory = new PronunciationsWebFactory();
            var client = await LoggedInClientAsync(factory);
            await SeedRawStationRulesAsync(client, """[{"pattern":"MacLeod","word":"MacLeod"}]""");

            var rows = await client.GetFromJsonAsync<List<PronunciationRuleRow>>("/api/pronunciations");

            var row = rows!.Single(r => r.Pattern == "MacLeod");
            Assert.True(!row.InEffect && row.Reason is not null && row.Reason.Contains("Ipa", StringComparison.Ordinal));
        }

        [Fact]
        public static async Task A_rule_that_never_compiled_is_deletable()
        {
            await using var factory = new PronunciationsWebFactory();
            var client = await LoggedInClientAsync(factory);
            await SeedRawStationRulesAsync(client, """[{"pattern":"MacLeod","word":"MacLeod"}]""");

            var delete = await client.DeleteAsync(RuleRoute("MacLeod", "MacLeod"));

            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        [Fact]
        public static async Task A_blank_pattern_dead_row_still_appears_with_its_reason()
        {
            // T144 review round 2 blocker (P5b): the (pattern="", word="") identity is the ONLY one a
            // blank-pattern row can ever have — it must render, not be swallowed as though it were
            // "no identity".
            await using var factory = new PronunciationsWebFactory();
            var client = await LoggedInClientAsync(factory);
            await SeedRawStationRulesAsync(client, """[{"pattern":"","ipa":"/a/"}]""");

            var rows = await client.GetFromJsonAsync<List<PronunciationRuleRow>>("/api/pronunciations");

            Assert.Contains(rows!, r => r.Pattern == "" && r.Reason != null);
        }

        [Fact]
        public static async Task A_blank_pattern_dead_row_is_deletable_by_its_empty_identity()
        {
            // The blocker itself: a non-nullable `string` [FromQuery] parameter under [ApiController]
            // is implicitly REQUIRED, so `?pattern=&word=` 400ed before the controller body ever ran —
            // permanently undeletable. Update/Delete now bind `string?` and coalesce to "" instead.
            await using var factory = new PronunciationsWebFactory();
            var client = await LoggedInClientAsync(factory);
            await SeedRawStationRulesAsync(client, """[{"pattern":"","ipa":"/a/"}]""");

            var delete = await client.DeleteAsync(RuleRoute("", ""));

            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }
    }

    public static class ScenarioTheAdminUiRendersIt
    {
        [Fact(Skip = "Pending T145 — see docs/PLAN.md")]
        public static void Rules_render_as_editable_rows_rather_than_a_json_blob()
        {
            Assert.Fail("pending T145");
        }

        [Fact(Skip = "Pending T145 — see docs/PLAN.md")]
        public static void A_shadowed_rule_is_visibly_not_in_effect()
        {
            Assert.Fail("pending T145");
        }
    }

    // -------------------------------------------------------------------------------------
    // ENTRY POINT — the live claim (F68.5): a saved rule affects the very next spoken line.
    // -------------------------------------------------------------------------------------
    public static class ScenarioASavedRuleIsLive
    {
        [Fact(Skip = "Pending T146 — see docs/PLAN.md")]
        public static void The_next_render_after_a_save_reflects_the_new_rule()
        {
            // Save through the real endpoint, then render — with no restart in between.
            Assert.Fail("pending T146");
        }

        [Fact(Skip = "Pending T146 — see docs/PLAN.md")]
        public static void No_process_restart_is_required()
        {
            Assert.Fail("pending T146");
        }
    }

    // -------------------------------------------------------------------------------------
    // SAD PATH
    // -------------------------------------------------------------------------------------
    public static class ScenarioInvalidRules
    {
        [Fact]
        public static async Task An_empty_pattern_is_rejected()
        {
            await using var factory = new PronunciationsWebFactory();
            var client = await LoggedInClientAsync(factory);

            var response = await client.PostAsJsonAsync("/api/pronunciations", RuleBody("", "/x/"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public static async Task Malformed_ipa_is_rejected()
        {
            // T137/T138's dead-rule shapes: an ipa carrying ')' truncates the wire annotation early
            // (KokoroSpeechMarkup) — PronunciationRuleSet.Create would silently drop this rule at
            // compile time; the API rejects it in place instead (F97.5's declared-vs-compiled
            // honesty, extended to the write path).
            await using var factory = new PronunciationsWebFactory();
            var client = await LoggedInClientAsync(factory);

            var response = await client.PostAsJsonAsync("/api/pronunciations", RuleBody("MacLeod", "/x)/"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public static async Task A_rejected_rule_is_not_persisted()
        {
            // Nothing half-saved: the surface rejects, the store is untouched.
            await using var factory = new PronunciationsWebFactory();
            var client = await LoggedInClientAsync(factory);
            await client.PostAsJsonAsync("/api/pronunciations", RuleBody("", "/x/"));

            var rows = await client.GetFromJsonAsync<List<PronunciationRuleRow>>("/api/pronunciations");

            Assert.Empty(rows!);
        }

        [Fact(Skip = "Pending T145 — see docs/PLAN.md")]
        public static void The_offending_field_is_highlighted_in_place()
        {
            Assert.Fail("pending T145");
        }
    }

    public static class ScenarioAuthoringWarnsAboutCollidingCorrections
    {
        /// <summary>Seeds <c>Tts:Corrections</c> through the raw settings API, the same live-key
        /// route <see cref="SeedRawStationRulesAsync"/> already uses for the rules key — the exact
        /// state gh-#491 found in the field (a legacy correction the operator forgot).</summary>
        static async Task SeedCorrectionsAsync(HttpClient client, string json)
        {
            var response = await client.PutAsJsonAsync("/api/settings", new[]
            {
                new { key = "Tts:Corrections", value = json },
            });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public static async Task Creating_a_rule_over_an_existing_correction_returns_the_collision_warning()
        {
            // gh-#491: the write succeeds (the collision is legitimate mid-migration state — never
            // a 400) but the operator is told which correction the new rule now suppresses.
            await using var factory = new PronunciationsWebFactory();
            var client = await LoggedInClientAsync(factory);
            await SeedCorrectionsAsync(client, """[{"from":"MacLeod","to":"Maa-cloud"}]""");

            var response = await client.PostAsJsonAsync("/api/pronunciations", RuleBody("MacLeod", "/x/"));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<PronunciationRuleWriteResponseBody>();
            var warning = Assert.Single(body!.Warnings);
            Assert.Contains("MacLeod", warning, StringComparison.Ordinal);
            Assert.Contains("Maa-cloud", warning, StringComparison.Ordinal);
        }

        [Fact]
        public static async Task Editing_a_rule_over_an_existing_correction_warns_too()
        {
            // The tweak-an-existing-rule path (gh-#491 ruling): the operator iterating IPA against
            // a corrected word hits PUT, not POST — the warning must ride both.
            await using var factory = new PronunciationsWebFactory();
            var client = await LoggedInClientAsync(factory);
            await SeedCorrectionsAsync(client, """[{"from":"MacLeod","to":"Maa-cloud"}]""");
            await client.PostAsJsonAsync("/api/pronunciations", RuleBody("MacLeod", "/old/"));

            var response = await client.PutAsJsonAsync(RuleRoute("MacLeod", "MacLeod"), RuleBody("MacLeod", "/new/"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<PronunciationRuleWriteResponseBody>();
            Assert.Single(body!.Warnings);
        }

        [Fact]
        public static async Task A_rule_with_no_colliding_correction_writes_with_no_warnings()
        {
            await using var factory = new PronunciationsWebFactory();
            var client = await LoggedInClientAsync(factory);
            await SeedCorrectionsAsync(client, """[{"from":"GWAV","to":"Gee-Wave"}]""");

            var response = await client.PostAsJsonAsync("/api/pronunciations", RuleBody("MacLeod", "/x/"));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<PronunciationRuleWriteResponseBody>();
            Assert.Empty(body!.Warnings);
            Assert.Equal("MacLeod", body.Rule.Pattern);
        }
    }
}
