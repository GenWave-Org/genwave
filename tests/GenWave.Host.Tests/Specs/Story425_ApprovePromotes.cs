// STORY-425 — Approve promotes the preview to on-air (SPEC F174.5 · STORY-429 AC1 · PLAN T445)
//
// BDD specification — xUnit through the deployed entry point (WebApplicationFactory<Program> against a
// real ephemeral Postgres — the Story424_PreviewRender.cs arc idiom): every fact drives
// POST /api/ads/{id}/approve over HTTP with an authed admin session (plus POST /api/sponsors and
// POST /api/ads to arrange what it needs, and a direct SQL stamp standing in for a real preview render —
// STORY-424's own render pipeline already proves how a preview gets there in the first place). Unlike
// Story424's own WebFactory, ICastSegmentAuthor here stays the REAL production chain
// (CastSegmentAuthor -> CrosstalkAssembler -> MediaRepository) end to end — this suite's whole point is
// proving a promotion genuinely lands a library.media row, not merely that AdRenderService called the
// right interface method. Only the ffmpeg-backed loudness/cue measurement underneath
// CrosstalkAssembler.MeasureAsync is faked (no real ffmpeg/ffprobe available in this sandbox, and
// MeasureAsync never calls anything else — see that class's own remarks), so the landed row's
// loudness/cue columns carry the fakes' own canned values, never real ffmpeg output; nothing about that
// substitution touches the path-move/insert/confirm mechanics this suite actually means to prove.
//
// AC5 (a landing failure falls back cleanly) needs the ads root itself to be unwritable in a way that
// reproduces even as CI's own root user — a bare permissions denial would not. A regular FILE sitting
// where AdRenderService.PromotePreviewAsync's own Directory.CreateDirectory(adsRoot) must land always
// throws, root or not, so that scenario runs its own WebFactory/AuthoredRoot pair, kept apart from the
// happy-path arrangement above it so AC5's own arrangement can never wedge every other Scenario sharing
// the same "ads" directory.

using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using GenWave.Ads;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureApprovePromotesThePreviewToOnAir
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    [Collection(Story425Collection.Name)]
    public sealed class ScenarioApprovePromotesWhenThePreviewIsCurrent(Story425Arc arc)
    {
        [Fact]
        public void ApproveIs200WithStateReady()
        {
            Assert.Equal(HttpStatusCode.OK, arc.ApproveStatus);
            Assert.Equal("ready", arc.ApprovedState);
        }

        [Fact]
        public void MediaIdPointsAtThePromotedFileNotThePreviewPath()
        {
            Assert.NotNull(arc.ApprovedMediaId);
            Assert.NotNull(arc.LandedMediaPath);
            Assert.StartsWith(arc.AdsRootPrefix, arc.LandedMediaPath, StringComparison.Ordinal);
            Assert.NotEqual(arc.PreviewPathBeforePromotion, arc.LandedMediaPath);
        }

        [Fact]
        public void PreviewStampsAreCleared()
        {
            Assert.Null(arc.RowAfterApprovePreviewPath);
            Assert.Null(arc.RowAfterApprovePreviewKey);
            Assert.Null(arc.RowAfterApprovePreviewAt);
        }
    }

    [Collection(Story425Collection.Name)]
    public sealed class ScenarioTheAiredTitleMatchesWhatThePreviewSays(Story425Arc arc)
    {
        [Fact]
        public void TitleAndArtistAreTheF161LandingShape()
        {
            Assert.Equal(arc.SpotTitle, arc.LandedMediaTitle);
            Assert.Equal(Story425WebFactory.StationName, arc.LandedMediaArtist);
        }

        [Fact]
        public void TheLandedRowIsEligibleAndTaggedAsAnAd()
        {
            Assert.True(arc.LandedMediaEligible);
            Assert.Equal("ad", arc.LandedMediaImagingKind);
        }
    }

    [Collection(Story425Collection.Name)]
    public sealed class ScenarioApproveWithoutAPreviewBehavesAsToday(Story425Arc arc)
    {
        [Fact]
        public void ApproveIs200WithStateApproved()
        {
            Assert.Equal(HttpStatusCode.OK, arc.NoPreviewApproveStatus);
            Assert.Equal("approved", arc.NoPreviewApprovedState);
        }

        [Fact]
        public void NoMediaIdIsSet()
            => Assert.Null(arc.NoPreviewApprovedMediaId);
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    [Collection(Story425Collection.Name)]
    public sealed class ScenarioRefusingAndFallingBack(Story425Arc arc)
    {
        [Fact]
        public void ApproveOnAStalePreviewIs409PreviewStale()
        {
            Assert.Equal(HttpStatusCode.Conflict, arc.StaleApproveStatus);
            Assert.Equal("preview_stale", arc.StaleApproveType);
        }

        [Fact]
        public void ALandingFailureIs500()
        {
            Assert.Equal(HttpStatusCode.InternalServerError, arc.FailureApproveStatus);
            Assert.Equal("preview_promotion_failed", arc.FailureApproveType);
        }

        [Fact]
        public void TheRowIsLeftApprovedWithNoMediaId()
        {
            Assert.Equal("approved", arc.FailureRowState);
            Assert.Null(arc.FailureRowMediaId);
        }

        [Fact]
        public void NoPartialMediaRowRemains()
            => Assert.Equal(0, arc.FailureLandedMediaRowCount);

        [Fact]
        public void PreviewStampsSurviveSoABareRetryCanPromoteWithoutReRendering()
        {
            Assert.NotNull(arc.FailureRowPreviewPath);
            Assert.NotNull(arc.FailureRowPreviewKey);
            Assert.NotNull(arc.FailureRowPreviewAt);
        }
    }

    // SPEC F174.5's staleness gate covers two more arms beside a wrong key (AC4 above): the stamped
    // file itself missing from disk, and a stamped path that resolves outside the preview root. Both
    // refuse the same way — 409 preview_stale, row left exactly as it was.

    [Collection(Story425Collection.Name)]
    public sealed class ScenarioApproveIsRefusedWhenThePreviewFileIsGone(Story425Arc arc)
    {
        [Fact]
        public void ApproveIs409PreviewStale()
        {
            Assert.Equal(HttpStatusCode.Conflict, arc.MissingFileApproveStatus);
            Assert.Equal("preview_stale", arc.MissingFileApproveType);
        }

        [Fact]
        public void TheRowIsLeftDraftWithNoMediaId()
        {
            Assert.Equal("draft", arc.MissingFileRowStateAfter);
            Assert.Null(arc.MissingFileRowMediaIdAfter);
        }

        [Fact]
        public void ThePreviewStampsAreUntouched()
            => Assert.True(arc.MissingFileRowPreviewUnchanged);
    }

    [Collection(Story425Collection.Name)]
    public sealed class ScenarioApproveIsRefusedWhenTheStoredPathEscapesThePreviewRoot(Story425Arc arc)
    {
        [Fact]
        public void ApproveIs409PreviewStale()
        {
            Assert.Equal(HttpStatusCode.Conflict, arc.EscapedPathApproveStatus);
            Assert.Equal("preview_stale", arc.EscapedPathApproveType);
        }

        [Fact]
        public void TheRowIsLeftDraftWithNoMediaId()
        {
            Assert.Equal("draft", arc.EscapedPathRowStateAfter);
            Assert.Null(arc.EscapedPathRowMediaIdAfter);
        }

        [Fact]
        public void ThePreviewStampsAreUntouched()
            => Assert.True(arc.EscapedPathRowPreviewUnchanged);

        [Fact]
        public void TheWarningNamesTheSpotId()
            => Assert.True(arc.EscapedPathWarningNamesTheSpotId, "no Warning line named the escaped spot's id");

        [Fact]
        public void TheWarningOmitsTheRawPath()
            => Assert.True(arc.EscapedPathWarningOmitsTheRawPath, "the Warning line carried the raw escaped path");
    }
}

[CollectionDefinition(Name)]
public sealed class Story425Collection : ICollectionFixture<Story425Arc>
{
    public const string Name = "Story425ApprovePromotes";
}

/// <summary>
/// Arranges every fact STORY-425's Scenarios read, over the real production HTTP pipeline with a real
/// admin session and the REAL <see cref="GenWave.Tts.ICastSegmentAuthor"/> chain (unlike
/// <see cref="Story424Arc"/> one story over — see this file's own header remarks for why) — only
/// <see cref="ILoudnessAnalyzer"/>/<see cref="ICueAnalyzer"/> are faked. AC1-AC4 all share ONE
/// <see cref="Story425WebFactory"/>/authored-root pair (a single sponsor, four spots); AC5 gets its own
/// pair, since ITS OWN arrangement — the ads directory pre-occupied by a regular file — would otherwise
/// permanently break every other Scenario's own promotion sharing the same root.
/// </summary>
public sealed class Story425Arc : IAsyncLifetime
{
    string? authoredRoot;
    string? failureAuthoredRoot;

    public string SpotTitle { get; } = "Approve promotes";
    public string AdsRootPrefix { get; private set; } = "";

    public HttpStatusCode ApproveStatus { get; private set; }
    public string? ApprovedState { get; private set; }
    public long? ApprovedMediaId { get; private set; }
    public string? PreviewPathBeforePromotion { get; private set; }
    public string? RowAfterApprovePreviewPath { get; private set; }
    public string? RowAfterApprovePreviewKey { get; private set; }
    public DateTime? RowAfterApprovePreviewAt { get; private set; }
    public string? LandedMediaPath { get; private set; }
    public string? LandedMediaTitle { get; private set; }
    public string? LandedMediaArtist { get; private set; }
    public bool? LandedMediaEligible { get; private set; }
    public string? LandedMediaImagingKind { get; private set; }

    public HttpStatusCode NoPreviewApproveStatus { get; private set; }
    public string? NoPreviewApprovedState { get; private set; }
    public long? NoPreviewApprovedMediaId { get; private set; }

    public HttpStatusCode StaleApproveStatus { get; private set; }
    public string? StaleApproveType { get; private set; }

    public HttpStatusCode FailureApproveStatus { get; private set; }
    public string? FailureApproveType { get; private set; }
    public string? FailureRowState { get; private set; }
    public long? FailureRowMediaId { get; private set; }
    public long FailureLandedMediaRowCount { get; private set; }
    public string? FailureRowPreviewPath { get; private set; }
    public string? FailureRowPreviewKey { get; private set; }
    public DateTime? FailureRowPreviewAt { get; private set; }

    public HttpStatusCode MissingFileApproveStatus { get; private set; }
    public string? MissingFileApproveType { get; private set; }
    public string? MissingFileRowStateAfter { get; private set; }
    public long? MissingFileRowMediaIdAfter { get; private set; }
    public bool MissingFileRowPreviewUnchanged { get; private set; }

    public HttpStatusCode EscapedPathApproveStatus { get; private set; }
    public string? EscapedPathApproveType { get; private set; }
    public string? EscapedPathRowStateAfter { get; private set; }
    public long? EscapedPathRowMediaIdAfter { get; private set; }
    public bool EscapedPathRowPreviewUnchanged { get; private set; }
    public bool EscapedPathWarningNamesTheSpotId { get; private set; }
    public bool EscapedPathWarningOmitsTheRawPath { get; private set; }

    public async Task InitializeAsync()
    {
        // A local, not a field — Story425Database is file-local (CS9051), the Story424Database
        // precedent one story over.
        await using var database = await Story425Database.StartAsync();
        authoredRoot = Directory.CreateTempSubdirectory("t445-story425-approve-").FullName;
        failureAuthoredRoot = Directory.CreateTempSubdirectory("t445-story425-approve-failure-").FullName;
        AdsRootPrefix = Path.Combine(authoredRoot, "ads") + Path.DirectorySeparatorChar;

        // AC5's own arrangement (STORY-425 AC5) — a regular FILE sitting exactly where
        // AdRenderService.PromotePreviewAsync's own Directory.CreateDirectory(adsRoot) must land,
        // reproducing even as CI's own root user (a bare permissions denial would not).
        await File.WriteAllBytesAsync(Path.Combine(failureAuthoredRoot, "ads"), [0]);

        await SeedAdsLibraryAsync(database.LibraryConnectionString);

        await using var factory = new Story425WebFactory(database, authoredRoot);
        var client = factory.CreateClient();
        await LoginAsync(client);

        var sponsorId = await AdSpotJobTestHelpers.CreateSponsorAsync(client, "Larkspur & Loom Mercantile");
        var sponsor = await FetchSponsorAsync(client, sponsorId);

        // The running factory's OWN IConfiguration, not a fresh empty builder — appsettings.json ships
        // a non-empty Station:Ads:CastVoices default (neither WebFactory below overrides it), so only
        // reading the SAME configuration instance AdsController itself resolves reproduces the exact
        // AdLiveSettings a real approve call would compute.
        var liveSettings = AdLiveSettingsReader.Read(factory.Services.GetRequiredService<IConfiguration>());

        const string script =
            "ANNOUNCER: Larkspur & Loom Mercantile has a deal so good it's almost illegal.\n" +
            "ANNOUNCER: Call 555-0177 - that's 555-0177 - Larkspur & Loom.";

        // ── AC1, AC2 — a current preview promotes to on-air in the SAME approve call. ──
        var (spotId, _) = await AdSpotJobTestHelpers.CreateDraftSpotWithScriptAsync(client, sponsorId, SpotTitle, script);

        var key = ComputeCurrentPreviewKey(script, sponsor, liveSettings);
        var previewPath = Path.Combine(authoredRoot, "preview", $"{spotId}-{key}.wav");
        Directory.CreateDirectory(Path.Combine(authoredRoot, "preview"));
        await File.WriteAllBytesAsync(previewPath, BuildMinimalWavBytes());
        await AdSpotJobTestHelpers.SetAdSpotPreviewStampAsync(database.StationConnectionString, spotId, previewPath, key, DateTime.UtcNow);
        PreviewPathBeforePromotion = previewPath;

        var (_, freshEtag) = await AdSpotJobTestHelpers.GetSpotWithETagAsync(client, spotId);
        var approveResponse = await PostApproveAsync(client, spotId, freshEtag);
        ApproveStatus = approveResponse.StatusCode;
        var approveBody = await JsonDocument.ParseAsync(await approveResponse.Content.ReadAsStreamAsync());
        ApprovedState = ReadState(approveBody.RootElement);
        ApprovedMediaId = ReadMediaId(approveBody.RootElement);

        var rowAfterApprove = await AdSpotJobTestHelpers.ReadAdSpotRowAsync(database.StationConnectionString, spotId);
        RowAfterApprovePreviewPath = rowAfterApprove.PreviewPath;
        RowAfterApprovePreviewKey = rowAfterApprove.PreviewKey;
        RowAfterApprovePreviewAt = rowAfterApprove.PreviewAt;
        if (rowAfterApprove.MediaId is { } landedMediaId)
        {
            var media = await AdSpotJobTestHelpers.ReadLibraryMediaAsync(database.LibraryConnectionString, landedMediaId);
            LandedMediaPath = media.Path;
            LandedMediaTitle = media.Title;
            LandedMediaArtist = media.Artist;
            LandedMediaEligible = media.Eligible;
            LandedMediaImagingKind = media.ImagingKind;
        }

        // ── AC3 — a row carrying no preview at all takes exactly today's path: script check, then
        // ApproveAsync, nothing else. ──
        var (noPreviewSpotId, noPreviewEtag) = await AdSpotJobTestHelpers.CreateDraftSpotWithScriptAsync(
            client, sponsorId, "Approve without preview", script);
        var noPreviewResponse = await PostApproveAsync(client, noPreviewSpotId, noPreviewEtag);
        NoPreviewApproveStatus = noPreviewResponse.StatusCode;
        var noPreviewBody = await JsonDocument.ParseAsync(await noPreviewResponse.Content.ReadAsStreamAsync());
        NoPreviewApprovedState = ReadState(noPreviewBody.RootElement);
        NoPreviewApprovedMediaId = ReadMediaId(noPreviewBody.RootElement);

        // ── AC4 — a stale (wrong-key) preview refuses the approve outright; the row is never touched
        // by a rejected approve. ──
        var (staleSpotId, _) = await AdSpotJobTestHelpers.CreateDraftSpotWithScriptAsync(
            client, sponsorId, "Approve on stale preview", script);
        var stalePreviewPath = Path.Combine(authoredRoot, "preview", $"{staleSpotId}-stale.wav");
        await File.WriteAllBytesAsync(stalePreviewPath, BuildMinimalWavBytes());
        await AdSpotJobTestHelpers.SetAdSpotPreviewStampAsync(
            database.StationConnectionString, staleSpotId, stalePreviewPath, new string('0', 64), DateTime.UtcNow);

        // PLAN T445 ruling: the ETag captured back at CreateDraftSpotWithScriptAsync is stale by the
        // time the SQL stamp above has bumped this row's own Postgres xmin (the AC1/AC2 arrangement's
        // own GetSpotWithETagAsync precedent) — a fresh GET is what supplies the If-Match this approve
        // actually needs, so this fact proves preview_stale on an otherwise-valid request, never the
        // unrelated xmin 409.
        var (_, staleEtag) = await AdSpotJobTestHelpers.GetSpotWithETagAsync(client, staleSpotId);
        var staleResponse = await PostApproveAsync(client, staleSpotId, staleEtag);
        StaleApproveStatus = staleResponse.StatusCode;
        var staleBody = await JsonDocument.ParseAsync(await staleResponse.Content.ReadAsStreamAsync());
        StaleApproveType = ReadType(staleBody.RootElement);

        // ── SPEC F174.5's file-missing arm — the stamped KEY is current, but the file it names is gone
        // from disk. ──
        var (missingFileSpotId, _) = await AdSpotJobTestHelpers.CreateDraftSpotWithScriptAsync(
            client, sponsorId, "Approve on missing preview file", script);
        var missingFileKey = ComputeCurrentPreviewKey(script, sponsor, liveSettings);
        var missingFilePreviewPath = Path.Combine(authoredRoot, "preview", $"{missingFileSpotId}-{missingFileKey}.wav");
        await File.WriteAllBytesAsync(missingFilePreviewPath, BuildMinimalWavBytes());
        await AdSpotJobTestHelpers.SetAdSpotPreviewStampAsync(
            database.StationConnectionString, missingFileSpotId, missingFilePreviewPath, missingFileKey, DateTime.UtcNow);
        var missingFileRowBefore = await AdSpotJobTestHelpers.ReadAdSpotRowAsync(database.StationConnectionString, missingFileSpotId);
        File.Delete(missingFilePreviewPath);

        var (_, missingFileEtag) = await AdSpotJobTestHelpers.GetSpotWithETagAsync(client, missingFileSpotId);
        var missingFileResponse = await PostApproveAsync(client, missingFileSpotId, missingFileEtag);
        MissingFileApproveStatus = missingFileResponse.StatusCode;
        var missingFileBody = await JsonDocument.ParseAsync(await missingFileResponse.Content.ReadAsStreamAsync());
        MissingFileApproveType = ReadType(missingFileBody.RootElement);

        var missingFileRowAfter = await AdSpotJobTestHelpers.ReadAdSpotRowAsync(database.StationConnectionString, missingFileSpotId);
        MissingFileRowStateAfter = missingFileRowAfter.State;
        MissingFileRowMediaIdAfter = missingFileRowAfter.MediaId;
        MissingFileRowPreviewUnchanged =
            missingFileRowAfter.PreviewPath == missingFileRowBefore.PreviewPath &&
            missingFileRowAfter.PreviewKey == missingFileRowBefore.PreviewKey &&
            missingFileRowAfter.PreviewAt == missingFileRowBefore.PreviewAt;

        // ── SPEC F174.5's escaped-path arm (the Story424 "PATH JAIL" precedent, one story over) — the
        // stamped KEY is current, but the stored path itself resolves outside the preview root. ──
        var (escapedSpotId, _) = await AdSpotJobTestHelpers.CreateDraftSpotWithScriptAsync(
            client, sponsorId, "Approve on escaped preview path", script);
        var escapedKey = ComputeCurrentPreviewKey(script, sponsor, liveSettings);
        var escapedPreviewPath = Path.Combine(authoredRoot, "preview", $"{escapedSpotId}-{escapedKey}.wav");
        await File.WriteAllBytesAsync(escapedPreviewPath, BuildMinimalWavBytes());
        await AdSpotJobTestHelpers.SetAdSpotPreviewStampAsync(
            database.StationConnectionString, escapedSpotId, escapedPreviewPath, escapedKey, DateTime.UtcNow);

        var outsideDirectory = Directory.CreateTempSubdirectory("t445-story425-preview-outside-").FullName;
        var outsidePath = Path.Combine(outsideDirectory, "escape.wav");
        await File.WriteAllBytesAsync(outsidePath, BuildMinimalWavBytes());
        await AdSpotJobTestHelpers.SetAdSpotPreviewPathAsync(database.StationConnectionString, escapedSpotId, outsidePath);

        // The "untouched" comparison below reads the row's own state AFTER this arrangement's escape
        // (the row a real render would have left it in), not before — a refused approve must leave
        // THIS state exactly as it found it, not the pre-escape stamp.
        var escapedRowBefore = await AdSpotJobTestHelpers.ReadAdSpotRowAsync(database.StationConnectionString, escapedSpotId);

        var (_, escapedEtag) = await AdSpotJobTestHelpers.GetSpotWithETagAsync(client, escapedSpotId);
        var escapedResponse = await PostApproveAsync(client, escapedSpotId, escapedEtag);
        EscapedPathApproveStatus = escapedResponse.StatusCode;
        var escapedBody = await JsonDocument.ParseAsync(await escapedResponse.Content.ReadAsStreamAsync());
        EscapedPathApproveType = ReadType(escapedBody.RootElement);

        var escapedRowAfter = await AdSpotJobTestHelpers.ReadAdSpotRowAsync(database.StationConnectionString, escapedSpotId);
        EscapedPathRowStateAfter = escapedRowAfter.State;
        EscapedPathRowMediaIdAfter = escapedRowAfter.MediaId;
        EscapedPathRowPreviewUnchanged =
            escapedRowAfter.PreviewPath == escapedRowBefore.PreviewPath &&
            escapedRowAfter.PreviewKey == escapedRowBefore.PreviewKey &&
            escapedRowAfter.PreviewAt == escapedRowBefore.PreviewAt;

        var escapedWarning = factory.Logs.Messages.FirstOrDefault(m => m.Contains($"spot {escapedSpotId}", StringComparison.Ordinal));
        EscapedPathWarningNamesTheSpotId = escapedWarning is not null;
        EscapedPathWarningOmitsTheRawPath = escapedWarning is not null && !escapedWarning.Contains(outsidePath, StringComparison.Ordinal);
        Directory.Delete(outsideDirectory, recursive: true);

        // ── AC5 — a landing failure falls back to Approved with no media row, no partial state — its
        // own WebFactory/authored-root pair (see this file's own header remarks). ──
        await using var failureFactory = new Story425WebFactory(database, failureAuthoredRoot);
        var failureClient = failureFactory.CreateClient();
        await LoginAsync(failureClient);

        var (failureSpotId, _) = await AdSpotJobTestHelpers.CreateDraftSpotWithScriptAsync(
            failureClient, sponsorId, "Approve landing failure", script);
        var failureKey = ComputeCurrentPreviewKey(script, sponsor, liveSettings);
        var failurePreviewPath = Path.Combine(failureAuthoredRoot, "preview", $"{failureSpotId}-{failureKey}.wav");
        Directory.CreateDirectory(Path.Combine(failureAuthoredRoot, "preview"));
        await File.WriteAllBytesAsync(failurePreviewPath, BuildMinimalWavBytes());
        await AdSpotJobTestHelpers.SetAdSpotPreviewStampAsync(
            database.StationConnectionString, failureSpotId, failurePreviewPath, failureKey, DateTime.UtcNow);

        var (_, failureEtag) = await AdSpotJobTestHelpers.GetSpotWithETagAsync(failureClient, failureSpotId);
        var failureResponse = await PostApproveAsync(failureClient, failureSpotId, failureEtag);
        FailureApproveStatus = failureResponse.StatusCode;
        var failureBody = await JsonDocument.ParseAsync(await failureResponse.Content.ReadAsStreamAsync());
        FailureApproveType = ReadType(failureBody.RootElement);

        var failureRow = await AdSpotJobTestHelpers.ReadAdSpotRowAsync(database.StationConnectionString, failureSpotId);
        FailureRowState = failureRow.State;
        FailureRowMediaId = failureRow.MediaId;
        FailureRowPreviewPath = failureRow.PreviewPath;
        FailureRowPreviewKey = failureRow.PreviewKey;
        FailureRowPreviewAt = failureRow.PreviewAt;
        FailureLandedMediaRowCount = await AdSpotJobTestHelpers.CountLibraryMediaRowsUnderPathAsync(
            database.LibraryConnectionString, Path.Combine(failureAuthoredRoot, "ads"));
    }

    public Task DisposeAsync()
    {
        if (authoredRoot is not null && Directory.Exists(authoredRoot))
            Directory.Delete(authoredRoot, recursive: true);
        if (failureAuthoredRoot is not null && Directory.Exists(failureAuthoredRoot))
            Directory.Delete(failureAuthoredRoot, recursive: true);
        return Task.CompletedTask;
    }

    static async Task LoginAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Story425WebFactory.Password });
        if (login.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"login unexpectedly returned {login.StatusCode}");
    }

    static Task<HttpResponseMessage> PostApproveAsync(HttpClient client, long spotId, string etag)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/ads/{spotId}/approve");
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return client.SendAsync(request);
    }

    static string? ReadState(JsonElement root) =>
        root.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.String
            ? state.GetString() : null;

    static long? ReadMediaId(JsonElement root) =>
        root.TryGetProperty("mediaId", out var mediaId) && mediaId.ValueKind == JsonValueKind.Number
            ? mediaId.GetInt64() : null;

    static string? ReadType(JsonElement root) =>
        root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
            ? type.GetString() : null;

    /// <summary>Reads the full <c>GET /api/sponsors/{id}</c> row back (STORY-425; PLAN T445) —
    /// <see cref="AdPreviewKey.Compute"/> needs the sponsor's own Tagline/About/Phone/Address/Website/
    /// Tone, not merely its id/name cross-reference, the SAME full row
    /// <see cref="GenWave.Host.Api.AdsController.PreviewIsStaleAsync"/> itself resolves via
    /// <c>ISponsorStore.GetAsync</c> before recomputing a key.</summary>
    static async Task<Sponsor> FetchSponsorAsync(HttpClient client, long sponsorId)
    {
        var response = await client.GetAsync($"/api/sponsors/{sponsorId}");
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"arrange: GET /api/sponsors/{sponsorId} unexpectedly returned {response.StatusCode}");

        var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;
        return new Sponsor(
            Id: root.GetProperty("id").GetInt64(),
            Name: root.GetProperty("name").GetString() ?? "",
            PackSlug: NullableString(root, "packSlug"),
            Tagline: NullableString(root, "tagline"),
            About: NullableString(root, "about"),
            Phone: NullableString(root, "phone"),
            Address: NullableString(root, "address"),
            Website: NullableString(root, "website"),
            Tone: NullableString(root, "tone"),
            Paused: root.GetProperty("paused").GetBoolean(),
            PausedAt: root.GetProperty("pausedAt").ValueKind == JsonValueKind.Null ? null : root.GetProperty("pausedAt").GetDateTime(),
            CreatedAt: root.GetProperty("createdAt").GetDateTime(),
            UpdatedAt: root.GetProperty("updatedAt").GetDateTime(),
            Version: "");
    }

    static string? NullableString(JsonElement root, string property) =>
        root.GetProperty(property).ValueKind == JsonValueKind.Null ? null : root.GetProperty(property).GetString();

    /// <summary>Independently recomputes exactly what <see cref="AdPreviewKey.Compute"/> would produce
    /// for a freshly-created, never-rendered spot (STORY-425; PLAN T445) — of <see cref="AdSpot"/>'s own
    /// 25 fields, only Script/VoicePlan/BedMediaId/SpotSeconds feed the digest (that method's own
    /// remarks), so every other field on this placeholder row is a valid but otherwise unread stand-in.
    /// <see cref="AdsOptions.BedDuckDb"/>'s own default (-12.0) rides along unread by either WebFactory
    /// here (no <c>Ads:BedDuckDb</c> override on either one), the same default
    /// <see cref="GenWave.Ads.AdRenderService"/> itself reads via <c>adsOptions.CurrentValue</c>.</summary>
    static string ComputeCurrentPreviewKey(string script, Sponsor sponsor, AdLiveSettings liveSettings)
    {
        var placeholderSpot = new AdSpot(
            Id: 0, SponsorId: sponsor.Id, SponsorName: sponsor.Name, Title: "placeholder", Brief: null,
            Script: script, Source: AdSource.Owner, PackSlug: null, SpotSeconds: 30, VoicePlan: null,
            BedMediaId: null, State: AdState.Draft, FailReason: null, MediaId: null, Generation: 0,
            CreatedAt: DateTime.UtcNow, StateChangedAt: DateTime.UtcNow, RenderedAt: null, RetiredAt: null,
            Version: "0");
        return AdPreviewKey.Compute(placeholderSpot, sponsor, liveSettings, bedDuckDb: -12.0);
    }

    /// <summary>A real, minimal, exactly-44-byte RIFF/WAVE file (PCM, mono, 8kHz, 16-bit, zero payload
    /// samples) — the <c>WavHeaderCastSegmentAuthor.BuildMinimalWavBytes</c> precedent one Fakes/ file
    /// over, trimmed to bare header only: this suite's own <see cref="ILoudnessAnalyzer"/>/
    /// <see cref="ICueAnalyzer"/> fakes never read the bytes either way, so the file's only job is to
    /// exist and parse as genuine <c>RIFF</c>/<c>WAVE</c> for <see cref="File.Move(string, string)"/> to
    /// move and <c>MediaRepository.InsertAuthoredAsync</c> to stat.</summary>
    static byte[] BuildMinimalWavBytes()
    {
        const int sampleRate = 8000;
        const short bitsPerSample = 16;
        const short channels = 1;

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write("RIFF"u8);
            writer.Write(36);
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write(channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * channels * bitsPerSample / 8);
            writer.Write((short)(channels * bitsPerSample / 8));
            writer.Write(bitsPerSample);
            writer.Write("data"u8);
            writer.Write(0);
        }

        return stream.ToArray();
    }

    /// <summary>The Story424Arc <c>SeedAdsLibraryAsync</c> precedent, this file's own copy (<c>file</c>-
    /// scoped types cannot cross files) — db/01's own seed guarantees <c>default</c> is id=1 in a fresh
    /// ephemeral database, so <c>ads</c> lands deterministically id=2, matching
    /// <see cref="Story425WebFactory"/>'s own <c>Station:Scope:LibraryIds</c> setting below.</summary>
    static async Task<long> SeedAdsLibraryAsync(string libraryConnectionString)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<long>("insert into library.library (name) values ('ads') returning id");
    }
}

// ── Test harness — WebApplicationFactory + ephemeral Postgres subclasses (the Story424_PreviewRender.cs
// "`file`-scoped types cannot cross files" precedent — this file supplies its own). ──

file sealed class Story425WebFactory(Story425Database db, string authoredRoot) : WebApplicationFactory<Program>
{
    public const string Password = "test-password-t445-approve-promotes";

    /// <summary>The station's own name, configured below — <see cref="AdRenderService.PromotePreviewAsync"/>
    /// stamps a promoted row's <c>artist</c> from <c>IStationIdentityProvider.Current.Name</c>, never the
    /// sponsor's own name (SPEC F161.3's landing shape; STORY-425 AC2).</summary>
    public const string StationName = "GWAV 108.8";

    /// <summary>Captures Warning+ log lines (the Story424WebFactory precedent, one story over) — the
    /// escaped-path arrangement's own "one Warning naming the spot id, never the raw path" claim reads
    /// this rather than console/Loki.</summary>
    public CapturingLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", db.LibraryConnectionString);
        builder.UseSetting("ConnectionStrings:Station", db.StationConnectionString);
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", StationName);
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");
        builder.UseSetting("Station:Scope:LibraryIds:1", "2");
        builder.UseSetting("Station:Safe:AuthoredRoot", authoredRoot);
        builder.UseSetting("Ads:JobQueueCapacity", "4");
        builder.UseSetting("Llm:Endpoint", "http://fake-llm.local");
        builder.UseSetting("Llm:Model", "test-model");

        builder.ConfigureTestServices(services =>
        {
            // No hosted service at all (PLAN T445) — every fact in this suite drives
            // POST /api/ads/{id}/approve synchronously over HTTP; nothing here ever needs
            // AdSpotJobService's own job-queue tick or the stuck-render guardian running in the
            // background (AdSpotJobService itself stays resolvable for AdsController's own constructor
            // injection regardless — it is registered as a plain singleton, separately from its
            // IHostedService wrapper, the Story424WebFactory precedent's own remarks apply identically
            // here).
            services.RemoveAll<IHostedService>();

            // The ONLY two I/O edges this suite fakes (the brief's own "replace ONLY
            // ILoudnessAnalyzer + ICueAnalyzer" instruction) — ICastSegmentAuthor stays the REAL
            // production chain end to end (CastSegmentAuthor -> CrosstalkAssembler -> MediaRepository)
            // so a promotion genuinely lands a library.media row; only the ffmpeg-backed measurement
            // CrosstalkAssembler.MeasureAsync calls underneath it is swapped, since no real ffmpeg/
            // ffprobe is available in this sandbox and MeasureAsync never calls anything else (that
            // class's own remarks).
            services.RemoveAll<ILoudnessAnalyzer>();
            services.AddSingleton<ILoudnessAnalyzer, FakeLoudnessAnalyzer>();
            services.RemoveAll<ICueAnalyzer>();
            services.AddSingleton<ICueAnalyzer, FakeCueAnalyzer>();

            services.AddSingleton<ILoggerProvider>(Logs);
        });
    }
}

/// <summary>This file's own thin subclass of the shared <see cref="EphemeralStationDatabase"/> harness
/// — see that type's own remarks. Supplies only the <c>"genwave-t445"</c> compose project-name prefix
/// this file's own arc needs.</summary>
file sealed class Story425Database : EphemeralStationDatabase
{
    Story425Database(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story425Database> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t445");
        var db = new Story425Database(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}
