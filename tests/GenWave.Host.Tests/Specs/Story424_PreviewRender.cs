// STORY-424 — Preview renders on demand with a staleness key (SPEC F174.4 · PLAN T442)
//
// BDD specification — xUnit through the deployed entry point (WebApplicationFactory<Program> against a
// real ephemeral Postgres — the Story423_WriteJob.cs/Story399_JinglePackInstall.cs arc idiom): every
// fact drives POST /api/ads/{id}/preview, GET /api/ads/{id}/preview.wav, and PATCH /api/ads/{id} (plus
// POST /api/sponsors + POST /api/ads to arrange what it needs) over HTTP with an authed admin session,
// never AdSpotJobService/AdRenderService/AdsController directly. The real AdSpotJobService,
// AdSpotStamper, and AdRenderService all run inside this Arc's own hosted service — only the render
// pipeline's OWN mixer is faked (ICastSegmentAuthor, never reached over the network or the filesystem
// in production, PLAN T442's own seam), so this suite proves the preview job's OWN wiring (fetch →
// stamp cast/bed → render → stamp preview_path/at/key), not the mixer's own internal audio logic
// (already proven in GenWave.Tts.Tests) — the fake mixer writes a real WAV header (PLAN T442's own
// acceptance line), so GET /api/ads/{id}/preview.wav's own facts read genuine audio/wav bytes back.

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using GenWave.Ads;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

public static class FeaturePreviewRendersOnDemandWithAStalenessKey
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    [Collection(Story424Collection.Name)]
    public sealed class ScenarioPostPreviewEnqueuesAndStampsPreviewKey(Story424Arc arc)
    {
        [Fact]
        public void PreviewIs202()
            => Assert.Equal(HttpStatusCode.Accepted, arc.PreviewEnqueueStatus);

        [Fact]
        public void TheRenderSettledWithoutAnError()
        {
            Assert.True(
                arc.RenderSettled,
                "the preview job never settled — either it never produced a preview object, or it timed out waiting");
            Assert.Null(arc.SettledJobError);
        }

        [Fact]
        public void TheRowHasPreviewPathAtAndKey()
        {
            Assert.True(arc.RenderSettled, "the preview job never settled");
            Assert.NotNull(arc.SettledPreviewAt);
            Assert.False(string.IsNullOrEmpty(arc.SettledPreviewKey), "preview.key was null/empty");
        }

        [Fact]
        public void PreviewKeyIsSha256Hex()
        {
            Assert.True(arc.RenderSettled, "the preview job never settled");
            // Shape only (STORY-424 AC1's own claim), not a recomputed-and-compared digest —
            // AdPreviewKey.Compute's own facts already pin the exact algorithm (GenWave.Ads.Tests).
            Assert.Matches("^[0-9a-f]{64}$", arc.SettledPreviewKey ?? "");
        }

        [Fact]
        public void CastAndMusicAreStampedByThePickers()
        {
            Assert.True(arc.RenderSettled, "the preview job never settled");
            Assert.True(arc.SettledVoicePlanPresent, "voicePlan was still null after the preview settled");
            Assert.True(arc.SettledBedMediaIdPresent, "bedMediaId was still null after the preview settled");
        }
    }

    [Collection(Story424Collection.Name)]
    public sealed class ScenarioPreviewFileLivesUnderThePreviewFolder(Story424Arc arc)
    {
        [Fact]
        public void ThePathMatchesLibraryRootPreviewSpotIdKeyWav()
        {
            Assert.True(arc.RenderSettled, "the preview job never settled");
            Assert.Equal(arc.ExpectedPreviewPath, arc.SettledPreviewPathFromSql);
        }

        [Fact]
        public void NoLibraryMediaRowExistsForIt()
        {
            Assert.True(arc.RenderSettled, "the preview job never settled");
            Assert.True(arc.MediaRowCountUnchanged, "a preview render inserted a library.media row");
        }
    }

    [Collection(Story424Collection.Name)]
    public sealed class ScenarioGetPreviewWavStreamsTheFile(Story424Arc arc)
    {
        [Fact]
        public void PreviewWavIs200()
        {
            Assert.True(arc.RenderSettled, "the preview job never settled");
            Assert.Equal(HttpStatusCode.OK, arc.PreviewWavStatus);
        }

        [Fact]
        public void TheContentTypeIsAudioWav()
        {
            Assert.True(arc.RenderSettled, "the preview job never settled");
            Assert.Equal("audio/wav", arc.PreviewWavContentType);
        }

        [Fact]
        public void TheBodyIsARiffWave()
        {
            Assert.True(arc.RenderSettled, "the preview job never settled");
            Assert.True(arc.PreviewWavBody.Length >= 12, "the preview body is too short to carry a RIFF/WAVE header");
            Assert.Equal("RIFF", Encoding.ASCII.GetString(arc.PreviewWavBody, 0, 4));
            Assert.Equal("WAVE", Encoding.ASCII.GetString(arc.PreviewWavBody, 8, 4));
        }
    }

    [Collection(Story424Collection.Name)]
    public sealed class ScenarioAnEditInvalidatesThePreview(Story424Arc arc)
    {
        [Fact]
        public void TheFreshPreviewReportsStaleFalse()
        {
            Assert.True(arc.RenderSettled, "the preview job never settled");
            Assert.True(arc.SettledPreviewStale.HasValue, "preview.stale was absent from the wire, not merely false");
            Assert.False(arc.SettledPreviewStale.Value, "a freshly rendered preview must report stale=false");
        }

        [Fact]
        public void TheRowReportsPreviewStaleTrue()
        {
            Assert.True(arc.RenderSettled, "the preview job never settled");
            Assert.True(
                arc.StaleAfterEditPreviewStale.HasValue, "preview.stale was absent from the wire after the edit");
            Assert.True(arc.StaleAfterEditPreviewStale.Value, "an edited spot's stored preview key must now read stale");
        }

        [Fact]
        public void PreviewWavIs404WhenStale()
        {
            Assert.True(arc.RenderSettled, "the preview job never settled");
            Assert.Equal(HttpStatusCode.NotFound, arc.StaleAfterEditPreviewWavStatus);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    [Collection(Story424Collection.Name)]
    public sealed class ScenarioRejectingWithoutAScript(Story424Arc arc)
    {
        [Fact]
        public void PreviewWithoutAScriptIs400ScriptRequired()
        {
            Assert.Equal(HttpStatusCode.BadRequest, arc.NoScriptPreviewStatus);
            Assert.Equal("script_required", arc.NoScriptPreviewType);
            Assert.Equal("script", arc.NoScriptPreviewField);
        }
    }

    // ---------------------------------------------------------------------
    // ROUTE MATRIX (segregated) — 409 against a non-editable state, 404 against an unknown id, 401
    // against an anonymous caller, and the null-vs-object shape of `preview` before/after a render
    // (SPEC F174.4; PLAN T442).
    // ---------------------------------------------------------------------

    [Collection(Story424Collection.Name)]
    public sealed class ScenarioPreviewAgainstAReadySpotIs409(Story424Arc arc)
    {
        [Fact]
        public void StatusIs409()
            => Assert.Equal(HttpStatusCode.Conflict, arc.PreviewOnReadySpotStatus);

        [Fact]
        public void TypeIsAdPreviewNotEditable()
            => Assert.Equal("ad_preview_not_editable", arc.PreviewOnReadySpotType);
    }

    [Collection(Story424Collection.Name)]
    public sealed class ScenarioPreviewAgainstARetiredSpotIs409(Story424Arc arc)
    {
        [Fact]
        public void StatusIs409()
            => Assert.Equal(HttpStatusCode.Conflict, arc.PreviewOnRetiredSpotStatus);

        [Fact]
        public void TypeIsAdPreviewNotEditable()
            => Assert.Equal("ad_preview_not_editable", arc.PreviewOnRetiredSpotType);
    }

    [Collection(Story424Collection.Name)]
    public sealed class ScenarioAnUnknownIdIs404OnBothRoutes(Story424Arc arc)
    {
        [Fact]
        public void PostPreviewIs404()
            => Assert.Equal(HttpStatusCode.NotFound, arc.PreviewUnknownIdStatus);

        [Fact]
        public void GetPreviewWavIs404()
            => Assert.Equal(HttpStatusCode.NotFound, arc.PreviewWavUnknownIdStatus);
    }

    [Collection(Story424Collection.Name)]
    public sealed class ScenarioAnAnonymousCallerIs401OnBothRoutes(Story424Arc arc)
    {
        [Fact]
        public void PostPreviewIs401()
            => Assert.Equal(HttpStatusCode.Unauthorized, arc.UnauthenticatedPreviewStatus);

        [Fact]
        public void GetPreviewWavIs401()
            => Assert.Equal(HttpStatusCode.Unauthorized, arc.UnauthenticatedPreviewWavStatus);
    }

    [Collection(Story424Collection.Name)]
    public sealed class ScenarioThePreviewFieldIsNullBeforeARenderAndAnObjectAfter(Story424Arc arc)
    {
        [Fact]
        public void ANeverRenderedSpotsPreviewFieldIsJsonNull()
            => Assert.Equal(JsonValueKind.Null, arc.NoScriptSpotPreviewValueKind);

        [Fact]
        public void ARenderedSpotsPreviewFieldIsAJsonObject()
        {
            Assert.True(arc.RenderSettled, "the preview job never settled");
            Assert.True(arc.SettledPreviewIsJsonObject, "preview was not a JSON object once the render settled");
        }
    }

    // ---------------------------------------------------------------------
    // PATH JAIL (segregated) — a stored preview_path resolving outside the
    // preview root 404s and logs Warning naming only the spot id; a preview file removed from disk
    // by hand 404s on the next read (SPEC F174.4; PLAN T442).
    // ---------------------------------------------------------------------

    [Collection(Story424Collection.Name)]
    public sealed class ScenarioPreviewWavIs404WhenTheStoredPathEscapesThePreviewRoot(Story424Arc arc)
    {
        [Fact]
        public void PreviewWavIs404WhenTheStoredPathEscapesThePreviewRoot()
        {
            Assert.True(arc.EscapedPathArrangementSettled, "arrange: the escaped-path spot's own preview job never settled");
            Assert.Equal(HttpStatusCode.NotFound, arc.EscapedPathPreviewWavStatus);
        }

        [Fact]
        public void TheWarningNamesTheSpotId()
        {
            Assert.True(arc.EscapedPathArrangementSettled, "arrange: the escaped-path spot's own preview job never settled");
            Assert.True(arc.EscapedPathWarningNamesTheSpotId, "no Warning line named the escaped spot's id");
        }

        [Fact]
        public void TheWarningOmitsTheRawPath()
        {
            Assert.True(arc.EscapedPathArrangementSettled, "arrange: the escaped-path spot's own preview job never settled");
            Assert.True(arc.EscapedPathWarningOmitsTheRawPath, "the Warning line carried the raw escaped path");
        }
    }

    [Collection(Story424Collection.Name)]
    public sealed class ScenarioPreviewWavIs404WhenTheFileIsGone(Story424Arc arc)
    {
        [Fact]
        public void TheFirstReadIs200()
        {
            Assert.True(arc.FileGoneArrangementSettled, "arrange: the file-gone spot's own preview job never settled");
            Assert.Equal(HttpStatusCode.OK, arc.FileGoneInitialPreviewWavStatus);
        }

        [Fact]
        public void PreviewWavIs404WhenTheFileIsGone()
        {
            Assert.True(arc.FileGoneArrangementSettled, "arrange: the file-gone spot's own preview job never settled");
            Assert.Equal(HttpStatusCode.NotFound, arc.FileGoneAfterDeletePreviewWavStatus);
        }
    }
}

[CollectionDefinition(Name)]
public sealed class Story424Collection : ICollectionFixture<Story424Arc>
{
    public const string Name = "Story424PreviewRender";
}

/// <summary>
/// Arranges every fact STORY-424's Scenarios read, over the real production HTTP pipeline with a real
/// admin session and the real <see cref="AdSpotJobService"/>/<see cref="AdSpotStamper"/>/
/// <see cref="AdRenderService"/> running as a hosted service (the <see cref="Story423Arc"/> precedent
/// one story over) — only <see cref="ICastSegmentAuthor"/> is faked (<see cref="WavHeaderCastSegmentAuthor"/>,
/// Fakes/), the one I/O edge this suite never means to exercise for real.
/// </summary>
public sealed class Story424Arc : IAsyncLifetime
{
    string? authoredRoot;

    public HttpStatusCode PreviewEnqueueStatus { get; private set; }

    public bool RenderSettled { get; private set; }
    public string? SettledJobError { get; private set; }
    public DateTime? SettledPreviewAt { get; private set; }
    public string? SettledPreviewKey { get; private set; }
    public bool? SettledPreviewStale { get; private set; }
    public bool SettledPreviewIsJsonObject { get; private set; }
    public bool SettledVoicePlanPresent { get; private set; }
    public bool SettledBedMediaIdPresent { get; private set; }
    public string? SettledPreviewPathFromSql { get; private set; }
    public string? ExpectedPreviewPath { get; private set; }
    public bool MediaRowCountUnchanged { get; private set; }

    public HttpStatusCode PreviewWavStatus { get; private set; }
    public string? PreviewWavContentType { get; private set; }
    public byte[] PreviewWavBody { get; private set; } = [];

    public bool? StaleAfterEditPreviewStale { get; private set; }
    public HttpStatusCode StaleAfterEditPreviewWavStatus { get; private set; }

    public HttpStatusCode NoScriptPreviewStatus { get; private set; }
    public string? NoScriptPreviewType { get; private set; }
    public string? NoScriptPreviewField { get; private set; }
    public JsonValueKind NoScriptSpotPreviewValueKind { get; private set; }

    public HttpStatusCode PreviewOnReadySpotStatus { get; private set; }
    public string? PreviewOnReadySpotType { get; private set; }
    public HttpStatusCode PreviewOnRetiredSpotStatus { get; private set; }
    public string? PreviewOnRetiredSpotType { get; private set; }
    public HttpStatusCode PreviewUnknownIdStatus { get; private set; }
    public HttpStatusCode PreviewWavUnknownIdStatus { get; private set; }
    public HttpStatusCode UnauthenticatedPreviewStatus { get; private set; }
    public HttpStatusCode UnauthenticatedPreviewWavStatus { get; private set; }

    public bool EscapedPathArrangementSettled { get; private set; }
    public HttpStatusCode EscapedPathPreviewWavStatus { get; private set; }
    public bool EscapedPathWarningNamesTheSpotId { get; private set; }
    public bool EscapedPathWarningOmitsTheRawPath { get; private set; }

    public bool FileGoneArrangementSettled { get; private set; }
    public HttpStatusCode FileGoneInitialPreviewWavStatus { get; private set; }
    public HttpStatusCode FileGoneAfterDeletePreviewWavStatus { get; private set; }

    public async Task InitializeAsync()
    {
        // A local, not a field — Story424Database is file-local (CS9051), the Story423Database
        // precedent one story over.
        await using var database = await Story424Database.StartAsync();
        authoredRoot = Directory.CreateTempSubdirectory("t442-story424-preview-").FullName;

        var adsLibraryId = await SeedAdsLibraryAsync(database.LibraryConnectionString);
        await SeedReadyBedRowAsync(database.LibraryConnectionString, adsLibraryId);

        await using var factory = new Story424WebFactory(database, authoredRoot);
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Story424WebFactory.Password });
        if (login.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"login unexpectedly returned {login.StatusCode}");

        var sponsorId = await AdSpotJobTestHelpers.CreateSponsorAsync(client, "Larkspur & Loom Mercantile");

        // ── AC1-AC4 — one spot carries the happy-path arrangement for every Scenario that reads a
        // genuinely rendered preview; STORY-424's own facts never disagree on what a single successful
        // render produced, so one render serves all of them (the Story423Arc "one arrangement, many
        // read-only Scenarios" precedent). ──
        const string script =
            "ANNOUNCER: Larkspur & Loom Mercantile has a deal so good it's almost illegal.\n" +
            "ANNOUNCER: Call 555-0177 - that's 555-0177 - Larkspur & Loom.";
        var (spotId, _) = await AdSpotJobTestHelpers.CreateDraftSpotWithScriptAsync(
            client, sponsorId, "Preview happy path", script);

        var mediaCountBefore = await AdSpotJobTestHelpers.CountLibraryMediaRowsAsync(database.LibraryConnectionString);

        var previewResponse = await AdSpotJobTestHelpers.PostPreviewAsync(client, spotId);
        PreviewEnqueueStatus = previewResponse.StatusCode;

        // TryPollUntilAsync, not PollUntilAsync — every fact reading RenderSettled asserts it FIRST
        // (the Story423Arc "JobIsNullAfterCompletion" precedent), so a render that never settles dies
        // at that fact's own assertion instead of a fixture-wide TimeoutException.
        var (settled, settledBody) = await AdSpotJobTestHelpers.TryPollUntilAsync(
            client, spotId, AdSpotJobTestHelpers.PreviewJobSettled, TimeSpan.FromSeconds(10));
        RenderSettled = settled;
        SettledJobError = AdSpotJobTestHelpers.JobError(settledBody);
        SettledPreviewAt = AdSpotJobTestHelpers.PreviewAt(settledBody);
        SettledPreviewKey = AdSpotJobTestHelpers.PreviewKey(settledBody);
        SettledPreviewStale = AdSpotJobTestHelpers.PreviewStale(settledBody);
        SettledPreviewIsJsonObject = AdSpotJobTestHelpers.PreviewIsPresent(settledBody);
        SettledVoicePlanPresent = settledBody.GetProperty("voicePlan").ValueKind != JsonValueKind.Null;
        SettledBedMediaIdPresent = settledBody.GetProperty("bedMediaId").ValueKind != JsonValueKind.Null;

        SettledPreviewPathFromSql = await AdSpotJobTestHelpers.ReadAdSpotPreviewPathAsync(database.StationConnectionString, spotId);
        ExpectedPreviewPath = SettledPreviewKey is null
            ? null
            : Path.Combine(authoredRoot, "preview", $"{spotId}-{SettledPreviewKey}.wav");

        var mediaCountAfter = await AdSpotJobTestHelpers.CountLibraryMediaRowsAsync(database.LibraryConnectionString);
        MediaRowCountUnchanged = mediaCountBefore == mediaCountAfter;

        // ── AC3 — GET /api/ads/{id}/preview.wav streams the rendered file back. ──
        var wavResponse = await AdSpotJobTestHelpers.GetPreviewWavAsync(client, spotId);
        PreviewWavStatus = wavResponse.StatusCode;
        PreviewWavContentType = wavResponse.Content.Headers.ContentType?.MediaType;
        PreviewWavBody = await wavResponse.Content.ReadAsByteArrayAsync();

        // ── AC4 — an edit invalidates the preview. The ETag captured back at CreateDraftSpotWithScriptAsync
        // is stale by now (PLAN T442: the preview job's own cast/bed/preview stamps are separate
        // UPDATEs, each bumping the row's Postgres xmin) — a fresh GET is what supplies the If-Match
        // this PATCH actually needs. ──
        const string editedScript =
            "ANNOUNCER: Larkspur & Loom Mercantile now has an even better deal.\n" +
            "ANNOUNCER: Call 555-0177 - that's 555-0177 - Larkspur & Loom.";
        var (_, freshEtag) = await AdSpotJobTestHelpers.GetSpotWithETagAsync(client, spotId);
        var patchRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/ads/{spotId}")
        {
            Content = JsonContent.Create(new { script = editedScript }),
        };
        patchRequest.Headers.TryAddWithoutValidation("If-Match", freshEtag);
        var patchResponse = await client.SendAsync(patchRequest);
        if (patchResponse.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException(
                $"arrange: PATCH /api/ads/{spotId} unexpectedly returned {patchResponse.StatusCode}: {await patchResponse.Content.ReadAsStringAsync()}");

        var afterEditBody = await AdSpotJobTestHelpers.GetSpotAsync(client, spotId);
        StaleAfterEditPreviewStale = AdSpotJobTestHelpers.PreviewStale(afterEditBody);

        var staleWavResponse = await AdSpotJobTestHelpers.GetPreviewWavAsync(client, spotId);
        StaleAfterEditPreviewWavStatus = staleWavResponse.StatusCode;

        // ── AC5 — a preview attempt against a brief-only, still-scriptless draft is 400 script_required. ──
        var noScriptSpotId = await AdSpotJobTestHelpers.CreateDraftSpotAsync(
            client, sponsorId, "No script yet", "A brief with no script written yet.");
        var noScriptResponse = await AdSpotJobTestHelpers.PostPreviewAsync(client, noScriptSpotId);
        NoScriptPreviewStatus = noScriptResponse.StatusCode;
        var noScriptBody = await JsonDocument.ParseAsync(await noScriptResponse.Content.ReadAsStreamAsync());
        NoScriptPreviewType = noScriptBody.RootElement.TryGetProperty("type", out var noScriptType)
            ? noScriptType.GetString() : null;
        NoScriptPreviewField = noScriptBody.RootElement.TryGetProperty("field", out var noScriptField)
            ? noScriptField.GetString() : null;

        // The scriptless spot's own row never reached a render — `preview` on its wire body must read
        // as JSON null, never merely absent (AdsController.ToPreviewDto's own null-exactly-when-never-
        // rendered contract).
        var noScriptSpotBody = await AdSpotJobTestHelpers.GetSpotAsync(client, noScriptSpotId);
        NoScriptSpotPreviewValueKind = noScriptSpotBody.GetProperty("preview").ValueKind;

        // ── Route matrix — 409 against a Ready row (seeded directly via SQL: this Arc's own WebFactory
        // removes AdSpotWorker, the only path that would otherwise ever land a row in Ready) and
        // against a Retired one (reached for real, POST /retire is legal straight off Draft), 404
        // against an id naming no spot at all on both routes, 401 against an anonymous caller on both
        // routes. ──
        var (readySpotId, _) = await AdsWireFixtures.InsertReadySpotAsync(
            database.StationConnectionString, brand: "Ready Spot For Preview 409", mediaId: 1);
        var previewOnReadyResponse = await AdSpotJobTestHelpers.PostPreviewAsync(client, readySpotId);
        PreviewOnReadySpotStatus = previewOnReadyResponse.StatusCode;
        var previewOnReadyBody = await JsonDocument.ParseAsync(await previewOnReadyResponse.Content.ReadAsStreamAsync());
        PreviewOnReadySpotType = previewOnReadyBody.RootElement.TryGetProperty("type", out var readyType)
            ? readyType.GetString() : null;

        var (retiringSpotId, retiringEtag) = await AdSpotJobTestHelpers.CreateDraftSpotWithScriptAsync(
            client, sponsorId, "Retired before preview", "ANNOUNCER: Never actually airs.");
        var retireRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/ads/{retiringSpotId}/retire");
        retireRequest.Headers.TryAddWithoutValidation("If-Match", retiringEtag);
        var retireResponse = await client.SendAsync(retireRequest);
        if (retireResponse.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException(
                $"arrange: POST /api/ads/{retiringSpotId}/retire unexpectedly returned {retireResponse.StatusCode}: {await retireResponse.Content.ReadAsStringAsync()}");
        var previewOnRetiredResponse = await AdSpotJobTestHelpers.PostPreviewAsync(client, retiringSpotId);
        PreviewOnRetiredSpotStatus = previewOnRetiredResponse.StatusCode;
        var previewOnRetiredBody = await JsonDocument.ParseAsync(await previewOnRetiredResponse.Content.ReadAsStreamAsync());
        PreviewOnRetiredSpotType = previewOnRetiredBody.RootElement.TryGetProperty("type", out var retiredType)
            ? retiredType.GetString() : null;

        PreviewUnknownIdStatus = (await AdSpotJobTestHelpers.PostPreviewAsync(client, 999_999)).StatusCode;
        PreviewWavUnknownIdStatus = (await AdSpotJobTestHelpers.GetPreviewWavAsync(client, 999_999)).StatusCode;

        var unauthenticatedClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        UnauthenticatedPreviewStatus = (await AdSpotJobTestHelpers.PostPreviewAsync(unauthenticatedClient, spotId)).StatusCode;
        UnauthenticatedPreviewWavStatus = (await AdSpotJobTestHelpers.GetPreviewWavAsync(unauthenticatedClient, spotId)).StatusCode;

        // ── The path-jail: a stored preview_path that resolves outside the
        // preview root must 404 AND log one Warning naming only the spot id, never the raw path. The
        // file it points at is REAL (so a mutant that deletes AdPreviewRoot.IsUnder's own re-assertion
        // out of PreviewWav would otherwise sail straight through to File.Exists/PhysicalFile and
        // answer 200) — only the stored PATH is wrong; preview_key is left exactly as the real render
        // produced it, so this fact dies at the path guard, never the staleness gate ahead of it. ──
        var (escapedSpotId, _) = await AdSpotJobTestHelpers.CreateDraftSpotWithScriptAsync(
            client, sponsorId, "Escaped preview path", script);
        var escapedPreviewResponse = await AdSpotJobTestHelpers.PostPreviewAsync(client, escapedSpotId);
        var (escapedSettled, escapedSettledBody) = await AdSpotJobTestHelpers.TryPollUntilAsync(
            client, escapedSpotId, AdSpotJobTestHelpers.PreviewJobSettled, TimeSpan.FromSeconds(10));
        EscapedPathArrangementSettled = escapedSettled;
        if (escapedSettled)
        {
            var outsideDirectory = Directory.CreateTempSubdirectory("t442-preview-outside-").FullName;
            var outsidePath = Path.Combine(outsideDirectory, "escape.wav");
            await File.WriteAllBytesAsync(outsidePath, [1, 2, 3, 4]);
            await AdSpotJobTestHelpers.SetAdSpotPreviewPathAsync(database.StationConnectionString, escapedSpotId, outsidePath);

            var escapedWavResponse = await AdSpotJobTestHelpers.GetPreviewWavAsync(client, escapedSpotId);
            EscapedPathPreviewWavStatus = escapedWavResponse.StatusCode;
            var escapedWarning = factory.Logs.Messages.FirstOrDefault(m => m.Contains($"spot {escapedSpotId}", StringComparison.Ordinal));
            EscapedPathWarningNamesTheSpotId = escapedWarning is not null;
            EscapedPathWarningOmitsTheRawPath = escapedWarning is not null && !escapedWarning.Contains(outsidePath, StringComparison.Ordinal);
            Directory.Delete(outsideDirectory, recursive: true);
        }

        // ── A preview that rendered fine, streamed 200 once, then had its file
        // removed by hand (the disk, not the row — preview_path/key stay exactly as the render left
        // them) must 404 on the next read rather than throw or serve a stale handle. ──
        var (fileGoneSpotId, _) = await AdSpotJobTestHelpers.CreateDraftSpotWithScriptAsync(
            client, sponsorId, "Preview file removed by hand", script);
        await AdSpotJobTestHelpers.PostPreviewAsync(client, fileGoneSpotId);
        var (fileGoneSettled, _) = await AdSpotJobTestHelpers.TryPollUntilAsync(
            client, fileGoneSpotId, AdSpotJobTestHelpers.PreviewJobSettled, TimeSpan.FromSeconds(10));
        var fileGonePath = fileGoneSettled
            ? await AdSpotJobTestHelpers.ReadAdSpotPreviewPathAsync(database.StationConnectionString, fileGoneSpotId)
            : null;
        if (fileGonePath is not null && File.Exists(fileGonePath))
        {
            FileGoneArrangementSettled = true;
            FileGoneInitialPreviewWavStatus = (await AdSpotJobTestHelpers.GetPreviewWavAsync(client, fileGoneSpotId)).StatusCode;
            File.Delete(fileGonePath);
            FileGoneAfterDeletePreviewWavStatus = (await AdSpotJobTestHelpers.GetPreviewWavAsync(client, fileGoneSpotId)).StatusCode;
        }
    }

    public Task DisposeAsync()
    {
        if (authoredRoot is not null && Directory.Exists(authoredRoot))
            Directory.Delete(authoredRoot, recursive: true);
        return Task.CompletedTask;
    }

    /// <summary>The Story399_JinglePackInstall.cs <c>SeedAdsLibraryAsync</c> precedent — db/01's own
    /// seed guarantees <c>default</c> is id=1 in a fresh ephemeral database, so <c>ads</c> lands
    /// deterministically id=2 here, matching <see cref="Story424WebFactory"/>'s own
    /// <c>Station:Scope:LibraryIds</c> setting below.</summary>
    static async Task<long> SeedAdsLibraryAsync(string libraryConnectionString)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<long>("insert into library.library (name) values ('ads') returning id");
    }

    /// <summary>A row satisfying <c>AdBedPoolRepository.PoolSql</c>'s own predicate exactly
    /// (<c>imaging_kind='jingle'</c>, <c>jingle_role='bed'</c>, <c>state='ready'</c>,
    /// <c>unavailable_since is null</c>, <c>library_id=@libraryId</c>) — the Story427_MusicPickerBrowse.cs
    /// <c>InsertJinglePackRowAsync</c> precedent, seeded into THIS Arc's own <c>ads</c> library so
    /// <see cref="AdSpotStamper.StampBedIfNeededAsync"/> genuinely finds it. The fake
    /// <see cref="ICastSegmentAuthor"/> never reads the bed's own <c>path</c>, so it needs no real
    /// on-disk audio behind it.</summary>
    static async Task SeedReadyBedRowAsync(string libraryConnectionString, long adsLibraryId)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            insert into library.media
                (path, format, size_bytes, mtime, state, duration_ms, title, artist,
                 eligible, imaging_kind, jingle_role, pack_slug, library_id)
            values
                ('/authored/jingle/story424-bed.wav', 'wav', 1024, now(), 'ready', 20000,
                 'Story 424 Bed', 'Story 424 Pack', true, 'jingle', 'bed', 'story424-pack', @libraryId)
            """;
        cmd.Parameters.AddWithValue("libraryId", adsLibraryId);
        await cmd.ExecuteNonQueryAsync();
    }
}

// ── Test harness — WebApplicationFactory + ephemeral Postgres subclasses (the Story423_WriteJob.cs/
// Story399_JinglePackInstall.cs "`file`-scoped types cannot cross files" precedent — this file
// supplies its own). ──

file sealed class Story424WebFactory(Story424Database db, string authoredRoot) : WebApplicationFactory<Program>
{
    public const string Password = "test-password-t442-preview-render";

    /// <summary>Captures the path-jail Warning line so a fact can assert on it — the
    /// <see cref="PluginDoorWebFactory"/> wiring idiom
    /// (<c>services.AddSingleton&lt;ILoggerProvider&gt;(...)</c>) one factory over.</summary>
    public CapturingLoggerProvider Logs { get; } = new();

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
        builder.UseSetting("Station:Scope:LibraryIds:1", "2");
        builder.UseSetting("Station:Safe:AuthoredRoot", authoredRoot);
        builder.UseSetting("Ads:JobQueueCapacity", "4");
        builder.UseSetting("Llm:Endpoint", "http://fake-llm.local");
        builder.UseSetting("Llm:Model", "test-model");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.AddHostedService(sp => sp.GetRequiredService<AdSpotJobService>());

            // The one I/O edge this suite fakes (PLAN T442) — never reached for real: no ffmpeg/kokoro
            // available in this sandbox, and STORY-424 needs a REAL WAV header out of it, unlike the
            // GenWave.Ads.Tests placeholder precedent (see WavHeaderCastSegmentAuthor's own remarks).
            services.RemoveAll<ICastSegmentAuthor>();
            services.AddSingleton<ICastSegmentAuthor, WavHeaderCastSegmentAuthor>();

            // The path-jail Warning line PreviewWav logs on an escaped preview_path; captured, never
            // asserted via console/Loki (the CapturingLoggerProvider precedent, Support/).
            services.AddSingleton<ILoggerProvider>(Logs);

            // This Arc never calls POST /api/ads/{id}/write — a handler that throws proves that stays
            // true rather than silently reaching a real network.
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new SingleHandlerHttpClientFactory(
                new FakeHttpMessageHandler((_, _) =>
                    throw new InvalidOperationException("no LLM handler wired — this Arc never calls /write"))));
        });
    }
}

/// <summary>This file's own thin subclass of the shared <see cref="EphemeralStationDatabase"/> harness
/// — see that type's own remarks. Supplies only the <c>"genwave-t442"</c> compose project-name prefix
/// this file's own arc needs.</summary>
file sealed class Story424Database : EphemeralStationDatabase
{
    Story424Database(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story424Database> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t442");
        var db = new Story424Database(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}
