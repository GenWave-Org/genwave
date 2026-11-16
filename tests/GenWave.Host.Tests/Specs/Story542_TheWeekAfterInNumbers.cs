// STORY-542 — The week after, in numbers (gh-#293 gh-#920 · SPEC F218.29 · PLAN T708)
//
// BDD specification — xUnit. AC1/AC2 over real Postgres (Category=Integration, the fresh-install schema: db/01 +
// db/06); AC3 through the deployed pipeline (WebApplicationFactory<Program>) with a fixed repeat reader — the
// SQL behind the reader is pinned in GenWave.MediaLibrary.Tests (Story542_TheWeekAfterInNumbers_Repository.cs).
// Also pins that db/51 (the upgrade path) carries the same view text as db/06, and that the SQL key phrase
// agrees with GenWave.Memory.RepetitionText.KeyPhrase (seen through RepetitionCheck's hit phrase).

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;
using GenWave.Memory;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace GenWave.Host.Tests.Specs;

public static class FeatureTheWeekAfterInNumbers
{
    const string FlipCard =
        """{"quirkRules":[{"text":"Says \"banger, log it\" after a great track","maxPerShift":1},{"text":"Likes tea"}]}""";

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — Catchphrases per shift
    [Trait("Category", "Integration")]
    public sealed class ScenarioCatchphrasesPerShift : IAsyncLifetime
    {
        RepeatViewsDatabase? database;
        IReadOnlyList<(string ShiftKey, string Phrase, int Occurrences)> rows = [];

        // Given utterances where Flip's "banger, log it" appears twice in one shift and once in another
        // When  the view is queried
        public async Task InitializeAsync()
        {
            database = await RepeatViewsDatabase.StartAsync();
            var flip = await database.AddPersonaAsync("Flip", FlipCard);
            await database.AddUtteranceAsync(flip, "shift-a", "Banger, log it. Next up, a good one.");
            await database.AddUtteranceAsync(flip, "shift-a", "And that one? Another banger, log it!");
            await database.AddUtteranceAsync(flip, "shift-b", "Quiet one, no signature here.");
            await database.AddUtteranceAsync(flip, "shift-b", "Okay, BANGER, LOG IT.");
            rows = await database.CatchphrasesAsync(flip);
        }

        public async Task DisposeAsync()
        {
            if (database is not null) await database.DisposeAsync();
        }

        [Fact]
        public void ItReports2ForTheFirstShiftKey() =>
            Assert.Contains(("shift-a", "banger, log it", 2), rows);

        [Fact]
        public void ItReports1ForTheSecond() =>
            Assert.Contains(("shift-b", "banger, log it", 1), rows);

        [Fact]
        public void ARuleWithNoPerShiftLimitIsNotCounted() =>
            Assert.DoesNotContain(rows, r => r.Phrase.Contains("tea", StringComparison.OrdinalIgnoreCase));
    }

    // AC2 — Catches and fallbacks
    [Trait("Category", "Integration")]
    public sealed class ScenarioCatchesAndFallbacks : IAsyncLifetime
    {
        RepeatViewsDatabase? database;
        (int Catches, int Trims, int Fallbacks) numbers;
        (int Catches, int Trims, int Fallbacks) quietPersona;

        // Given 3 repeat-caught rows and 1 fallback utterance for Flip in 7 days
        // When  the view is queried
        public async Task InitializeAsync()
        {
            database = await RepeatViewsDatabase.StartAsync();
            var flip = await database.AddPersonaAsync("Flip");
            await database.AddRepeatCaughtAsync(flip, "one", "re-asked");
            await database.AddRepeatCaughtAsync(flip, "two", "template");
            await database.AddRepeatCaughtAsync(flip, "three", "trimmed", TimeSpan.FromDays(6));
            await database.AddRepeatCaughtAsync(flip, "too old", "template", TimeSpan.FromDays(8));
            await database.AddUtteranceAsync(flip, "shift-a", "A stock line.", fallback: true);
            await database.AddUtteranceAsync(flip, "shift-a", "A stock line, long ago.", fallback: true, age: TimeSpan.FromDays(9));
            await database.AddUtteranceAsync(flip, "shift-a", "A fine line.");
            numbers = await database.RepeatCatchesAsync(flip);
            quietPersona = await database.RepeatCatchesAsync(await database.AddPersonaAsync("Tony"));
        }

        public async Task DisposeAsync()
        {
            if (database is not null) await database.DisposeAsync();
        }

        [Fact]
        public void ItReportsCatches3() => Assert.Equal(3, numbers.Catches);

        [Fact]
        public void ItReportsFallbacks1() => Assert.Equal(1, numbers.Fallbacks);

        [Fact]
        public void ADjWithNothingToReportHasARowOfZeros() => Assert.Equal((0, 0, 0), quietPersona);
    }

    // AC3 — On the Status page
    public sealed class ScenarioOnTheStatusPage
    {
        // Given the views have rows
        // When  the Status page loads

        [Fact]
        public async Task ARepeatsPanelShowsThePerDJNumbersInPlainWords()
        {
            var numbers = new RepeatNumbers(
                [new DjRepeatNumbers(7, "Flip", Catches: 3, Trims: 2, Fallbacks: 1)],
                [new CatchphraseOveruse(7, "Flip", "shift-a", "banger, log it", MaxPerShift: 1, Occurrences: 2,
                    LastSaidAt: new DateTimeOffset(2026, 11, 9, 3, 0, 0, TimeSpan.Zero))]);
            await using var factory = new RepeatsStatusWebFactory(new FakeRepeatNumbersReader(numbers));
            var client = factory.CreateClient();
            var login = await client.PostAsJsonAsync("/api/auth/login", new { password = RepeatsStatusWebFactory.Password });
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

            var response = await client.GetAsync("/api/status");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var repeats = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("repeats");
            var dj = Assert.Single(repeats.GetProperty("djs").EnumerateArray());
            Assert.Equal("Flip", dj.GetProperty("name").GetString());
            Assert.Equal(3, dj.GetProperty("catches").GetInt32());
            Assert.Equal(2, dj.GetProperty("trims").GetInt32());
            Assert.Equal(1, dj.GetProperty("fallbacks").GetInt32());
            var overused = Assert.Single(repeats.GetProperty("overused").EnumerateArray());
            Assert.Equal("banger, log it", overused.GetProperty("phrase").GetString());
            Assert.Equal(2, overused.GetProperty("occurrences").GetInt32());
            Assert.Equal(1, overused.GetProperty("maxPerShift").GetInt32());
            Assert.Equal(new DateTimeOffset(2026, 11, 9, 3, 0, 0, TimeSpan.Zero), overused.GetProperty("lastSaidAt").GetDateTimeOffset());
        }

        [Fact]
        public async Task AnUnreachableStoreLeavesTheRestOfTheStatusIntact()
        {
            await using var factory = new RepeatsStatusWebFactory(new UnavailableRepeatNumbersReader());
            var client = factory.CreateClient();
            await client.PostAsJsonAsync("/api/auth/login", new { password = RepeatsStatusWebFactory.Password });

            var response = await client.GetAsync("/api/status");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal(JsonValueKind.Null, body.GetProperty("repeats").ValueKind);
            Assert.True(body.TryGetProperty("catalog", out _));
        }
    }

    // ---------------------------------------------------------------------
    // The SQL mirrors the C# and db/06 mirrors db/51
    // ---------------------------------------------------------------------

    public sealed class ScenarioDb06AndDb51CarryTheSameViews
    {
        [Fact]
        public void TheViewTextIsIdentical()
        {
            var root = RepoRootLocator.Find(AppContext.BaseDirectory);
            var fresh = ViewBlock(Path.Combine(root, "db", "06-station-settings-migration.sh"));
            var upgrade = ViewBlock(Path.Combine(root, "db", "51-repeat-views-migration.sh"));

            Assert.Equal(fresh, upgrade);
        }

        static string ViewBlock(string path)
        {
            var lines = File.ReadAllLines(path)
                .SkipWhile(l => !l.Contains("-- begin f218-repeat-views", StringComparison.Ordinal))
                .TakeWhile(l => !l.Contains("-- end f218-repeat-views", StringComparison.Ordinal))
                .ToList();
            Assert.NotEmpty(lines);
            return string.Join('\n', lines);
        }
    }

    [Trait("Category", "Integration")]
    public sealed class ScenarioTheSqlKeyPhraseIsTheCSharpKeyPhrase : IAsyncLifetime
    {
        RepeatViewsDatabase? database;

        public static TheoryData<string> Rules => new()
        {
            "Signs off with \"stay groovy my friends\" every night",
            "Says 'that's a wrap, my friends' at the end of each shift",
            "Says ‘don’t touch that dial’ before the news",
            "Says “one” then “a much longer span, naturally” on air",
            "Tells listeners to wave to the neighbours!",
            "Waves  to   the neighbours, loudly.",
        };

        public async Task InitializeAsync() => database = await RepeatViewsDatabase.StartAsync();

        public async Task DisposeAsync()
        {
            if (database is not null) await database.DisposeAsync();
        }

        [Theory]
        [MemberData(nameof(Rules))]
        public async Task TheViewNamesTheSamePhraseRepetitionCheckDoes(string rule)
        {
            var hit = Assert.Single(new RepetitionCheck().Check(rule, new RepetitionContext([], [rule])).Hits);
            var card = JsonSerializer.Serialize(new { quirkRules = new[] { new { text = rule, maxPerShift = 1 } } });
            var persona = await database!.AddPersonaAsync($"Persona {Guid.NewGuid():N}", card);
            await database.AddUtteranceAsync(persona, "shift-a", $"Well, {hit.Phrase}, folks.");

            var rows = await database.CatchphrasesAsync(persona);

            var row = Assert.Single(rows);
            Assert.Equal(hit.Phrase, row.Phrase);
            Assert.Equal(1, row.Occurrences);
        }
    }
}

file sealed class UnavailableRepeatNumbersReader : IRepeatNumbersReader
{
    public Task<RepeatNumbers?> ReadAsync(CancellationToken ct) => Task.FromResult<RepeatNumbers?>(null);
}

/// <summary>The deployed pipeline with every Postgres-backed status dependency faked, as Story084's factory does.</summary>
file sealed class RepeatsStatusWebFactory(IRepeatNumbersReader reader) : WebApplicationFactory<Program>
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
            services.RemoveAll<IMediaRotationSink>();
            services.AddSingleton<IMediaRotationSink>(new FakeMediaRotationSink());
            services.RemoveAll<IRotFindingStore>();
            services.AddSingleton<IRotFindingStore>(new FakeRotFindingStore());
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
            services.RemoveAll<IRepeatNumbersReader>();
            services.AddSingleton(reader);
        });
    }
}
