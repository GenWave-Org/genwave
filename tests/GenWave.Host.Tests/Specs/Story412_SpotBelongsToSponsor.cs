// STORY-412 — A spot belongs to a sponsor and snapshots the name (SPEC F171.7 · PLAN T436)
//
// BDD specification — xUnit through the deployed entry point (WebApplicationFactory<Program> against
// a real ephemeral Postgres — the Story411_BriefBelongsToSponsor.cs/Story392_AdsApi.cs arc idiom):
// every fact drives POST/PATCH/GET /api/ads (and POST /api/sponsors to arrange the sponsors it needs)
// over HTTP with an authed admin session, never AdSpotRepository/AdsController/SponsorRepository/
// SponsorsController directly. One arc (Story412Arc) arranges everything every HAPPY-PATH/sad-path
// Scenario below reads (the SAME "arrange once, many read-only Scenarios" idiom one controller over).
// AC7's contract scan is pure reflection over the built Host assembly — no HTTP call involved — but its
// result is still captured on the Arc rather than computed inline in the Fact, the same "Facts read
// Arc-captured state only, one claim each" rule every other Fact here holds.

using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using GenWave.Host.Api;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureASpotBelongsToASponsorAndSnapshotsTheName
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    [Collection(Story412Collection.Name)]
    public sealed class ScenarioSponsorNameIsStampedAtCreation(Story412Arc arc)
    {
        [Fact]
        public void PostIs201()
            => Assert.Equal(HttpStatusCode.Created, arc.FirstCreateStatus);

        [Fact]
        public void SponsorNameEqualsTheSponsorsNameAtCreation()
            => Assert.Equal(Story412Arc.SponsorAName, arc.FirstCreateSponsorName);

        [Fact]
        public void TheResponseCarriesTheSponsorObject()
        {
            Assert.Equal(arc.SponsorAId, arc.FirstCreateSponsorObjectId);
            Assert.Equal(Story412Arc.SponsorAName, arc.FirstCreateSponsorObjectName);
            Assert.False(arc.FirstCreateSponsorObjectPaused);
        }
    }

    [Collection(Story412Collection.Name)]
    public sealed class ScenarioSnapshotRefreshesOnASponsorChange(Story412Arc arc)
    {
        [Fact]
        public void PatchingSponsorIdRefreshesSponsorName()
            => Assert.Equal(Story412Arc.SponsorBName, arc.PatchResponseSponsorName);

        // ── NEW fact (not part of the pending stubs above — never rename an existing one): the
        // response-body claim above is the store's own PROJECTION of what it wrote; this reads
        // station.ad_spot.sponsor_name straight out of Postgres by SQL instead, so a controller that
        // merely echoed the REQUEST's own sponsor name back (rather than the store's own refreshed
        // snapshot) would still turn this fact red even if it somehow passed the one above. ──
        [Fact]
        public void TheStationAdSpotRowsSponsorNameColumnIsRefreshedToo()
            => Assert.Equal(Story412Arc.SponsorBName, arc.PatchedRowSponsorNameFromSql);
    }

    [Collection(Story412Collection.Name)]
    public sealed class ScenarioFilterBySponsorId(Story412Arc arc)
    {
        [Fact]
        public void GetAdsFilteredReturnsExactlyThatSponsorsSpots()
            => Assert.Equal(arc.SponsorAExclusiveSpotIds, arc.FilteredSpotIds);
    }

    [Collection(Story412Collection.Name)]
    public sealed class ScenarioTheWordBrandAppearsInNoField(Story412Arc arc)
    {
        [Fact]
        public void NoRequestOrResponsePropertyIsNamedBrand()
            => Assert.Empty(arc.PropertiesNamedBrand);
    }

    [Collection(Story412Collection.Name)]
    public sealed class ScenarioAPausedSponsorsExistingSpotsStayEditable(Story412Arc arc)
    {
        [Fact]
        public void PatchingAPausedSponsorsExistingSpotIs200()
            => Assert.Equal(HttpStatusCode.OK, arc.PausedSponsorSpotPatchStatus);
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    [Collection(Story412Collection.Name)]
    public sealed class ScenarioRejectingInvalidInput(Story412Arc arc)
    {
        [Fact]
        public void MissingSponsorIdIs400SponsorRequired()
        {
            Assert.Equal(HttpStatusCode.BadRequest, arc.MissingSponsorIdStatus);
            Assert.Equal("sponsor_required", arc.MissingSponsorIdType);
            Assert.Equal("sponsorId", arc.MissingSponsorIdField);
        }

        [Fact]
        public void APausedSponsorIs409SponsorPaused()
        {
            Assert.Equal(HttpStatusCode.Conflict, arc.PausedSponsorCreateStatus);
            Assert.Equal("sponsor_paused", arc.PausedSponsorCreateType);
        }

        [Fact]
        public void AnUnknownSponsorIs404SponsorNotFound()
        {
            Assert.Equal(HttpStatusCode.NotFound, arc.UnknownSponsorCreateStatus);
            Assert.Equal("sponsor_not_found", arc.UnknownSponsorCreateType);
        }

        [Fact]
        public void PatchingWithAnUnknownSponsorIdIs404SponsorNotFound()
        {
            Assert.Equal(HttpStatusCode.NotFound, arc.PatchUnknownSponsorStatus);
            Assert.Equal("sponsor_not_found", arc.PatchUnknownSponsorType);
            Assert.Equal("sponsorId", arc.PatchUnknownSponsorField);
        }

        [Fact]
        public void FilteringByANonNumericSponsorIdIs400()
        {
            Assert.Equal(HttpStatusCode.BadRequest, arc.InvalidSponsorIdQueryStatus);
            Assert.Equal("sponsorId", arc.InvalidSponsorIdQueryField);
        }
    }
}

// ── Collection definition — one ephemeral Postgres/factory shared by every happy-path/sad-path
// Scenario above (the Story411Collection/Story392AdsApiCollection "arrange once, many read-only
// Scenarios" idiom). ──

[CollectionDefinition(Name)]
public sealed class Story412Collection : ICollectionFixture<Story412Arc>
{
    public const string Name = "Story412SpotBelongsToSponsor";
}

/// <summary>
/// Arranges every fact STORY-412's Scenarios read, entirely over the REAL production HTTP pipeline with
/// a real admin session — no <c>AdSpotRepository</c>/<c>AdsController</c>/<c>SponsorRepository</c>/
/// <c>SponsorsController</c> call anywhere in this class (except AC7's own reflection scan, which needs
/// no HTTP call at all). Every sponsor a spot needs is created through the real <c>POST /api/sponsors</c>
/// surface (PLAN T434), never seeded by SQL — this story is about the FK between a spot and a real
/// sponsor row, so the sponsor itself must be real too.
/// </summary>
public sealed class Story412Arc : IAsyncLifetime
{
    public const string SponsorAName = "Cattywampus Coffee & Tea";
    public const string SponsorBName = "Halvorsen Ice Rink & Tax Prep";
    public const string PausedSponsorName = "Drowsy Donuts";
    public const string FilterSponsorAName = "Quill & Anchor Booksellers";
    public const string FilterSponsorBName = "Marsh & Pine Outfitters";
    public const string PausedSponsorWithExistingSpotName = "Sleepy Sundries";

    public long SponsorAId { get; private set; }

    public HttpStatusCode MissingSponsorIdStatus { get; private set; }
    public string? MissingSponsorIdType { get; private set; }
    public string? MissingSponsorIdField { get; private set; }

    public HttpStatusCode FirstCreateStatus { get; private set; }
    public string FirstCreateSponsorName { get; private set; } = "";
    public long FirstCreateSponsorObjectId { get; private set; }
    public string FirstCreateSponsorObjectName { get; private set; } = "";
    public bool FirstCreateSponsorObjectPaused { get; private set; }

    public string PatchResponseSponsorName { get; private set; } = "";
    public string PatchedRowSponsorNameFromSql { get; private set; } = "";

    public HttpStatusCode PausedSponsorCreateStatus { get; private set; }
    public string? PausedSponsorCreateType { get; private set; }

    public HttpStatusCode UnknownSponsorCreateStatus { get; private set; }
    public string? UnknownSponsorCreateType { get; private set; }

    public HttpStatusCode PatchUnknownSponsorStatus { get; private set; }
    public string? PatchUnknownSponsorType { get; private set; }
    public string? PatchUnknownSponsorField { get; private set; }

    public HttpStatusCode InvalidSponsorIdQueryStatus { get; private set; }
    public string? InvalidSponsorIdQueryField { get; private set; }

    public HttpStatusCode PausedSponsorSpotPatchStatus { get; private set; }

    public IReadOnlyList<long> SponsorAExclusiveSpotIds { get; private set; } = [];
    public IReadOnlyList<long> FilteredSpotIds { get; private set; } = [];

    public IReadOnlyList<string> PropertiesNamedBrand { get; private set; } = [];

    public async Task InitializeAsync()
    {
        // A LOCAL, not a field — Story412Database is file-local (CS9051), the Story411Database/
        // Story392AdsDatabase precedent.
        await using var database = await Story412Database.StartAsync();
        await using var factory = new Story412WebFactory(database);
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new { password = Story412WebFactory.Password });
        if (login.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"login unexpectedly returned {login.StatusCode}");

        // ── AC1 — sponsorId is required on create. ──
        var missingResponse = await client.PostAsJsonAsync(
            "/api/ads",
            new { sponsorId = (long?)null, title = "Nothing to see here", brief = "n/a", spotSeconds = 30 });
        MissingSponsorIdStatus = missingResponse.StatusCode;
        var missingBody = await JsonDocument.ParseAsync(await missingResponse.Content.ReadAsStreamAsync());
        MissingSponsorIdType = missingBody.RootElement.TryGetProperty("type", out var missingType)
            ? missingType.GetString() : null;
        MissingSponsorIdField = missingBody.RootElement.TryGetProperty("field", out var missingField)
            ? missingField.GetString() : null;

        // ── AC2 — the spot carries sponsorName + the sponsor object at creation. ──
        SponsorAId = await CreateSponsorAsync(client, SponsorAName);
        var firstCreateResponse = await client.PostAsJsonAsync(
            "/api/ads",
            new { sponsorId = SponsorAId, title = "First spot", brief = "A first ad", spotSeconds = 30 });
        FirstCreateStatus = firstCreateResponse.StatusCode;
        var firstCreateBody = await JsonDocument.ParseAsync(await firstCreateResponse.Content.ReadAsStreamAsync());
        FirstCreateSponsorName = firstCreateBody.RootElement.GetProperty("sponsorName").GetString() ?? "";
        var sponsorElement = firstCreateBody.RootElement.GetProperty("sponsor");
        FirstCreateSponsorObjectId = sponsorElement.GetProperty("id").GetInt64();
        FirstCreateSponsorObjectName = sponsorElement.GetProperty("name").GetString() ?? "";
        FirstCreateSponsorObjectPaused = sponsorElement.GetProperty("paused").GetBoolean();
        var firstCreateId = firstCreateBody.RootElement.GetProperty("id").GetInt64();
        var firstCreateEtag = firstCreateResponse.Headers.ETag?.Tag ?? "";

        // ── AC3 — PATCHing sponsorId under a second, real sponsor refreshes sponsorName both in the
        // response AND in the underlying station.ad_spot row. ──
        var sponsorBId = await CreateSponsorAsync(client, SponsorBName);
        var patchRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/ads/{firstCreateId}")
        {
            Content = JsonContent.Create(new { sponsorId = sponsorBId }),
        };
        patchRequest.Headers.TryAddWithoutValidation("If-Match", firstCreateEtag);
        var patchResponse = await client.SendAsync(patchRequest);
        if (patchResponse.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"arrange: PATCH /api/ads/{firstCreateId} unexpectedly returned {patchResponse.StatusCode}");
        }
        var patchBody = await JsonDocument.ParseAsync(await patchResponse.Content.ReadAsStreamAsync());
        PatchResponseSponsorName = patchBody.RootElement.GetProperty("sponsorName").GetString() ?? "";

        PatchedRowSponsorNameFromSql = await ReadAdSpotSponsorNameAsync(
            database.StationConnectionString, firstCreateId);

        // ── AC4 — a paused sponsor cannot have a NEW spot created under it (creating is gated only;
        // ScenarioAPausedSponsorsExistingSpotsStayEditable.PatchingAPausedSponsorsExistingSpotIs200
        // below proves — by actually pausing a sponsor and PATCHing one of its existing spots, not by
        // inference from AC3's own PATCH, which never involves a paused sponsor — that a paused
        // sponsor's EXISTING spots stay editable). ──
        var pausedSponsorId = await CreateSponsorAsync(client, PausedSponsorName);
        var pauseResponse = await client.PostAsync($"/api/sponsors/{pausedSponsorId}/pause", content: null);
        if (pauseResponse.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"arrange: POST /api/sponsors/{pausedSponsorId}/pause unexpectedly returned {pauseResponse.StatusCode}");
        }
        var pausedSponsorCreateResponse = await client.PostAsJsonAsync(
            "/api/ads",
            new { sponsorId = pausedSponsorId, title = "Should never air", brief = "n/a", spotSeconds = 30 });
        PausedSponsorCreateStatus = pausedSponsorCreateResponse.StatusCode;
        var pausedSponsorCreateBody = await JsonDocument.ParseAsync(
            await pausedSponsorCreateResponse.Content.ReadAsStreamAsync());
        PausedSponsorCreateType = pausedSponsorCreateBody.RootElement.TryGetProperty("type", out var pausedType)
            ? pausedType.GetString() : null;

        // ── AC5 — an unknown sponsorId is refused as 404, checked BEFORE any insert. ──
        var unknownSponsorResponse = await client.PostAsJsonAsync(
            "/api/ads",
            new { sponsorId = 999_999L, title = "A spot for nobody", brief = "n/a", spotSeconds = 30 });
        UnknownSponsorCreateStatus = unknownSponsorResponse.StatusCode;
        var unknownSponsorBody = await JsonDocument.ParseAsync(await unknownSponsorResponse.Content.ReadAsStreamAsync());
        UnknownSponsorCreateType = unknownSponsorBody.RootElement.TryGetProperty("type", out var unknownType)
            ? unknownType.GetString() : null;

        // ── T436 review finding (beyond STORY-412 AC1–AC7) — PATCHing an EXISTING spot to reference
        // an unknown sponsorId is refused as 404 sponsor_not_found too, not merely inferred from AC5's
        // create-time check (review finding: the Update guard is separate code from the Create guard,
        // and deleting it went undetected: the prior review round's gate filter stayed green). ──
        var patchTargetSponsorId = await CreateSponsorAsync(client, "Patch Target Sponsor");
        var patchTargetCreateResponse = await client.PostAsJsonAsync(
            "/api/ads",
            new { sponsorId = patchTargetSponsorId, title = "Patch target spot", brief = "n/a", spotSeconds = 30 });
        if (patchTargetCreateResponse.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException(
                $"arrange: POST /api/ads(Patch target spot) unexpectedly returned {patchTargetCreateResponse.StatusCode}");
        }
        var patchTargetCreateBody = await JsonDocument.ParseAsync(await patchTargetCreateResponse.Content.ReadAsStreamAsync());
        var patchTargetId = patchTargetCreateBody.RootElement.GetProperty("id").GetInt64();
        var patchTargetEtag = patchTargetCreateResponse.Headers.ETag?.Tag ?? "";

        var patchUnknownSponsorRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/ads/{patchTargetId}")
        {
            Content = JsonContent.Create(new { sponsorId = 999_999L }),
        };
        patchUnknownSponsorRequest.Headers.TryAddWithoutValidation("If-Match", patchTargetEtag);
        var patchUnknownSponsorResponse = await client.SendAsync(patchUnknownSponsorRequest);
        PatchUnknownSponsorStatus = patchUnknownSponsorResponse.StatusCode;
        var patchUnknownSponsorBody = await JsonDocument.ParseAsync(
            await patchUnknownSponsorResponse.Content.ReadAsStreamAsync());
        PatchUnknownSponsorType = patchUnknownSponsorBody.RootElement.TryGetProperty("type", out var patchUnknownType)
            ? patchUnknownType.GetString() : null;
        PatchUnknownSponsorField = patchUnknownSponsorBody.RootElement.TryGetProperty("field", out var patchUnknownField)
            ? patchUnknownField.GetString() : null;

        // ── T436 review finding (beyond STORY-412 AC1–AC7) — ?sponsorId=abc (not a whole number) is
        // refused as 400 naming the field, never silently ignored into "any sponsor" (review finding:
        // a silent-ignore mutant also went undetected: the prior review round's gate filter stayed
        // green — a caller filtering by a typo'd sponsorId would otherwise get every sponsor's spots
        // back with no signal anything was wrong). ──
        var invalidSponsorIdQueryResponse = await client.GetAsync("/api/ads?sponsorId=abc");
        InvalidSponsorIdQueryStatus = invalidSponsorIdQueryResponse.StatusCode;
        var invalidSponsorIdQueryBody = await JsonDocument.ParseAsync(
            await invalidSponsorIdQueryResponse.Content.ReadAsStreamAsync());
        InvalidSponsorIdQueryField = invalidSponsorIdQueryBody.RootElement.TryGetProperty("field", out var invalidSponsorIdField)
            ? invalidSponsorIdField.GetString() : null;

        // ── T436 review finding (beyond STORY-412 AC1–AC7) — a PAUSED sponsor's EXISTING spot stays
        // editable: the spot is created BEFORE the sponsor is paused (pausing forecloses only NEW
        // spots under it, AC4 above), then the sponsor is paused, then the existing spot's title is
        // PATCHed — 200 proves the class remarks' claim directly rather than merely asserting it in a
        // comment. ──
        var pausedSpotSponsorId = await CreateSponsorAsync(client, PausedSponsorWithExistingSpotName);
        var pausedSpotCreateResponse = await client.PostAsJsonAsync(
            "/api/ads",
            new { sponsorId = pausedSpotSponsorId, title = "Editable under a paused sponsor", brief = "n/a", spotSeconds = 30 });
        if (pausedSpotCreateResponse.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException(
                $"arrange: POST /api/ads(Editable under a paused sponsor) unexpectedly returned {pausedSpotCreateResponse.StatusCode}");
        }
        var pausedSpotCreateBody = await JsonDocument.ParseAsync(await pausedSpotCreateResponse.Content.ReadAsStreamAsync());
        var pausedSpotId = pausedSpotCreateBody.RootElement.GetProperty("id").GetInt64();
        var pausedSpotEtag = pausedSpotCreateResponse.Headers.ETag?.Tag ?? "";

        var pausedSpotPauseResponse = await client.PostAsync($"/api/sponsors/{pausedSpotSponsorId}/pause", content: null);
        if (pausedSpotPauseResponse.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"arrange: POST /api/sponsors/{pausedSpotSponsorId}/pause unexpectedly returned {pausedSpotPauseResponse.StatusCode}");
        }

        var pausedSpotPatchRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/ads/{pausedSpotId}")
        {
            Content = JsonContent.Create(new { title = "Edited while sponsor is paused" }),
        };
        pausedSpotPatchRequest.Headers.TryAddWithoutValidation("If-Match", pausedSpotEtag);
        var pausedSpotPatchResponse = await client.SendAsync(pausedSpotPatchRequest);
        PausedSponsorSpotPatchStatus = pausedSpotPatchResponse.StatusCode;

        // ── AC6 — ?sponsorId= narrows the list to exactly that sponsor's own rows. Dedicated, FRESH
        // sponsors (not SponsorAId/sponsorBId above, whose own spot moved sponsors mid-arc) so the
        // expected id set is exact and unambiguous. ──
        var filterSponsorAId = await CreateSponsorAsync(client, FilterSponsorAName);
        var filterSponsorBId = await CreateSponsorAsync(client, FilterSponsorBName);

        var sponsorASpotIds = new List<long>();
        for (var i = 1; i <= 3; i++)
            sponsorASpotIds.Add(await CreateAdSpotAsync(client, filterSponsorAId, $"Filter A spot {i}"));

        for (var i = 1; i <= 2; i++)
            await CreateAdSpotAsync(client, filterSponsorBId, $"Filter B spot {i}");

        SponsorAExclusiveSpotIds = sponsorASpotIds.OrderBy(id => id).ToList();

        var filteredResponse = await client.GetAsync($"/api/ads?sponsorId={filterSponsorAId}");
        if (filteredResponse.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"arrange: GET /api/ads?sponsorId={filterSponsorAId} unexpectedly returned {filteredResponse.StatusCode}");
        }
        var filteredBody = await JsonDocument.ParseAsync(await filteredResponse.Content.ReadAsStreamAsync());
        FilteredSpotIds = filteredBody.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetInt64())
            .OrderBy(id => id)
            .ToList();

        // ── AC7 — the contract scan: no property anywhere on the Ads/AdBriefs/Sponsors admin surface
        // is named "brand" (case-insensitive). Pure reflection over the built Host assembly — no HTTP
        // call needed — but captured here, not inline in the Fact (the "Facts read Arc-captured state
        // only" rule every other Fact in this suite holds). ──
        PropertiesNamedBrand = FindPropertiesNamedBrand();
    }

    static async Task<long> CreateSponsorAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/sponsors", new { name });
        if (response.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"arrange: POST /api/sponsors({name}) unexpectedly returned {response.StatusCode}");

        var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return body.RootElement.GetProperty("id").GetInt64();
    }

    static async Task<long> CreateAdSpotAsync(HttpClient client, long sponsorId, string title)
    {
        var response = await client.PostAsJsonAsync(
            "/api/ads", new { sponsorId, title, brief = "A filter fixture ad", spotSeconds = 30 });
        if (response.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"arrange: POST /api/ads({title}) unexpectedly returned {response.StatusCode}");

        var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return body.RootElement.GetProperty("id").GetInt64();
    }

    static async Task<string> ReadAdSpotSponsorNameAsync(string stationConnectionString, long spotId)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select sponsor_name from station.ad_spot where id = @id";
        cmd.Parameters.AddWithValue("id", spotId);
        return (string?)await cmd.ExecuteScalarAsync() ?? "";
    }

    /// <summary>AC7's own scan (STORY-412's contract-scan spec, brief's "simplest honest approach"):
    /// every public sealed RECORD in namespace <c>GenWave.Host.Api</c> whose name starts with
    /// <c>AdSpot</c>, <c>AdBrief</c>, or <c>Sponsor</c> — deliberately record-only, so
    /// <c>AdsController</c>/<c>AdBriefsController</c>/<c>SponsorsController</c> themselves (ordinary
    /// sealed classes, not records, despite two of their own names matching the same prefixes) are never
    /// mistaken for a wire shape. Walks every matched type's own properties recursively — including
    /// through nested GenWave.* record types (e.g. <see cref="AdSpotDto.Sponsor"/>'s own
    /// <see cref="SponsorRefDto"/>) and through the element type of any enumerable property (e.g.
    /// <see cref="AdSpotDto.VoicePlan"/>'s own <c>AdVoicePlanEntry</c>) — asserting no property anywhere
    /// is named "brand", case-insensitive.</summary>
    static IReadOnlyList<string> FindPropertiesNamedBrand()
    {
        var assembly = typeof(AdsController).Assembly;
        var targetTypes = assembly.GetTypes().Where(type =>
            type is { IsPublic: true, IsSealed: true, Namespace: "GenWave.Host.Api" } &&
            IsRecord(type) &&
            (type.Name.StartsWith("AdSpot", StringComparison.Ordinal) ||
                type.Name.StartsWith("AdBrief", StringComparison.Ordinal) ||
                type.Name.StartsWith("Sponsor", StringComparison.Ordinal)));

        var offending = new List<string>();
        var visited = new HashSet<Type>();
        foreach (var type in targetTypes)
            Walk(type, type.Name, visited, offending);

        return offending;
    }

    // The C# compiler emits a non-public "EqualityContract" property on every record — the reliable,
    // reflection-visible marker that distinguishes a record from an ordinary sealed class whose name
    // happens to share the same prefix (AdBriefsController, SponsorsController).
    static bool IsRecord(Type type) =>
        type.GetProperty("EqualityContract", BindingFlags.NonPublic | BindingFlags.Instance) is not null;

    static void Walk(Type type, string path, HashSet<Type> visited, List<string> offending)
    {
        if (!visited.Add(type))
            return;

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var propertyPath = $"{path}.{property.Name}";
            if (string.Equals(property.Name, "brand", StringComparison.OrdinalIgnoreCase))
                offending.Add(propertyPath);

            foreach (var nested in NestedTypesOf(property.PropertyType))
                Walk(nested, propertyPath, visited, offending);
        }
    }

    static IEnumerable<Type> NestedTypesOf(Type propertyType)
    {
        var candidate = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (candidate.IsGenericType)
        {
            // IReadOnlyList<T>/IReadOnlyDictionary<TKey,TValue>/etc. — walk every generic argument's
            // own element type too (AdSpotDto.VoicePlan's own IReadOnlyList<AdVoicePlanEntry>, e.g.).
            foreach (var argument in candidate.GetGenericArguments())
            foreach (var nested in NestedTypesOf(argument))
                yield return nested;

            yield break;
        }

        if (candidate.Namespace is { } ns && ns.StartsWith("GenWave.", StringComparison.Ordinal) &&
            candidate != typeof(string) && !candidate.IsEnum)
        {
            yield return candidate;
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Test harness — WebApplicationFactory + ephemeral Postgres subclasses (the Story411_
// BriefBelongsToSponsor.cs/Story392_AdsApi.cs "`file`-scoped types cannot cross files" precedent — this
// file supplies its own). ──

file sealed class Story412WebFactory(Story412Database db) : WebApplicationFactory<Program>
{
    public const string Password = "test-password-t436-spot-sponsor";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", db.LibraryConnectionString);
        builder.UseSetting("ConnectionStrings:Station", db.StationConnectionString);
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
        });
    }
}

/// <summary>This file's own thin subclass of the shared <see cref="EphemeralStationDatabase"/> harness
/// — see that type's own remarks. Supplies only the <c>"genwave-t436"</c> compose project-name prefix
/// this file's own arc needs.</summary>
file sealed class Story412Database : EphemeralStationDatabase
{
    Story412Database(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story412Database> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t436");
        var db = new Story412Database(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}
