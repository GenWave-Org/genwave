using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace GenWave.Host.Tests.Support;

/// <summary>
/// Shared HTTP arrangement/read helpers for the write-job suite (STORY-422/STORY-423, PLAN T441) — both
/// Story422_AdSpotJobs.cs and Story423_WriteJob.cs drive the SAME <c>POST/DELETE /api/ads/{id}/write|job</c>
/// and <c>GET /api/ads/{id}</c> surface, so this lives here rather than as a duplicate <c>file</c>-scoped
/// copy per spec file (the <see cref="EphemeralStationDatabase"/>/<see cref="AdScriptCompletionsRouter"/>
/// "shared home in Support/" precedent one seam over).
/// </summary>
internal static class AdSpotJobTestHelpers
{
    public static async Task<long> CreateSponsorAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/sponsors", new { name });
        if (response.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"arrange: POST /api/sponsors({name}) unexpectedly returned {response.StatusCode}");

        var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return body.RootElement.GetProperty("id").GetInt64();
    }

    public static async Task<long> CreateDraftSpotAsync(HttpClient client, long sponsorId, string title, string brief)
    {
        var response = await client.PostAsJsonAsync(
            "/api/ads", new { sponsorId, title, brief, spotSeconds = 30 });
        if (response.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"arrange: POST /api/ads({title}) unexpectedly returned {response.StatusCode}");

        var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return body.RootElement.GetProperty("id").GetInt64();
    }

    /// <summary>Creates a draft spot whose <c>script</c> is set directly at creation (the Story412Arc
    /// "owner sponsor names itself" precedent) — the only route to a spot STORY-423 AC3 can legally
    /// <c>POST /approve</c> off of, since <c>approve</c> re-validates whatever script is already on the
    /// row rather than accepting one itself.</summary>
    public static async Task<(long Id, string ETag)> CreateDraftSpotWithScriptAsync(
        HttpClient client, long sponsorId, string title, string script)
    {
        var response = await client.PostAsJsonAsync(
            "/api/ads", new { sponsorId, title, script, spotSeconds = 30 });
        if (response.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"arrange: POST /api/ads({title}) unexpectedly returned {response.StatusCode}");

        var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var id = body.RootElement.GetProperty("id").GetInt64();
        var etag = response.Headers.ETag?.Tag ?? "";
        return (id, etag);
    }

    public static Task<HttpResponseMessage> PostWriteAsync(HttpClient client, long spotId) =>
        client.PostAsync($"/api/ads/{spotId}/write", content: null);

    /// <summary>STORY-424 (PLAN T442) — the write job's own <see cref="PostWriteAsync"/> sibling.</summary>
    public static Task<HttpResponseMessage> PostPreviewAsync(HttpClient client, long spotId) =>
        client.PostAsync($"/api/ads/{spotId}/preview", content: null);

    /// <summary>Raw <c>GET /api/ads/{id}/preview.wav</c> — the STATUS is the claim itself for several
    /// STORY-424 facts (200/404), so this returns the whole response rather than throwing on a
    /// non-200 the way <see cref="GetSpotAsync"/> does.</summary>
    public static Task<HttpResponseMessage> GetPreviewWavAsync(HttpClient client, long spotId) =>
        client.GetAsync($"/api/ads/{spotId}/preview.wav");

    public static async Task<JsonElement> GetSpotAsync(HttpClient client, long spotId)
    {
        var response = await client.GetAsync($"/api/ads/{spotId}");
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"arrange: GET /api/ads/{spotId} unexpectedly returned {response.StatusCode}");

        var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return body.RootElement.Clone();
    }

    /// <summary>The same GET, but also returns the fresh <c>ETag</c> response header (PLAN T442) —
    /// STORY-424 AC4's own PATCH arrangement needs a version captured AFTER the preview job's own
    /// cast/bed/preview stamps have already bumped the row's Postgres <c>xmin</c>, never the ETag
    /// <see cref="CreateDraftSpotWithScriptAsync"/> captured back at spot-creation time — that token
    /// is stale by the time a preview has settled, and a PATCH sent against it would 409, not 200.</summary>
    public static async Task<(JsonElement Body, string ETag)> GetSpotWithETagAsync(HttpClient client, long spotId)
    {
        var response = await client.GetAsync($"/api/ads/{spotId}");
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"arrange: GET /api/ads/{spotId} unexpectedly returned {response.StatusCode}");

        var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return (body.RootElement.Clone(), response.Headers.ETag?.Tag ?? "");
    }

    /// <summary>Polls <c>GET /api/ads/{id}</c> until <paramref name="ready"/> is satisfied, up to
    /// <paramref name="timeout"/> — returns <see langword="false"/> plus the last body read on a timeout
    /// rather than throwing, so an Arc built on this NEVER crashes into an <see cref="Assert"/> that
    /// never gets to run; the caller's own <see cref="Assert.True"/> (or <see cref="Assert.False"/>) on
    /// the returned flag is what carries the claim. No Arc in this suite uses a throwing poll — every
    /// settle wait here either feeds a fact's own flag or is discarded outright, and either way a
    /// stalled row surfaces as that fact's own red, never a fixture-wide crash.</summary>
    public static async Task<(bool Ready, JsonElement Body)> TryPollUntilAsync(
        HttpClient client, long spotId, Func<JsonElement, bool> ready, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var body = await GetSpotAsync(client, spotId);
        while (true)
        {
            if (ready(body))
                return (true, body);

            if (DateTime.UtcNow >= deadline)
                return (false, body);

            await Task.Delay(TimeSpan.FromMilliseconds(100));
            body = await GetSpotAsync(client, spotId);
        }
    }

    /// <summary>Settled = the job finished, one way or the other (SPEC F174.2/F174.3:
    /// <c>IAdSpotStore.ClearJobAsync</c> clears <c>job_kind</c> on EVERY outcome, success or failure
    /// alike) — either the whole <c>job</c> object is null (success), or it survives with <c>kind: null</c>
    /// and an <c>error</c> (failure).</summary>
    public static bool JobIsSettled(JsonElement body) => JobKind(body) is null;

    public static string? JobKind(JsonElement body) =>
        body.GetProperty("job") is { ValueKind: JsonValueKind.Object } job &&
            job.GetProperty("kind").ValueKind == JsonValueKind.String
            ? job.GetProperty("kind").GetString()
            : null;

    public static DateTime? JobStartedAt(JsonElement body) =>
        body.GetProperty("job") is { ValueKind: JsonValueKind.Object } job &&
            job.GetProperty("startedAt").ValueKind == JsonValueKind.String
            ? job.GetProperty("startedAt").GetDateTime()
            : null;

    public static bool? JobWaitingForStation(JsonElement body) =>
        body.GetProperty("job") is { ValueKind: JsonValueKind.Object } job
            ? job.GetProperty("waitingForStation").GetBoolean()
            : null;

    public static string? JobError(JsonElement body) =>
        body.GetProperty("job") is { ValueKind: JsonValueKind.Object } job &&
            job.GetProperty("error").ValueKind == JsonValueKind.String
            ? job.GetProperty("error").GetString()
            : null;

    public static bool JobIsPresent(JsonElement body) =>
        body.GetProperty("job").ValueKind == JsonValueKind.Object;

    /// <summary>STORY-424 (PLAN T442) — <c>preview</c> is present exactly when
    /// <c>AdSpot.PreviewPath</c> is non-null (<c>AdsController.ToPreviewDto</c>'s own contract); a
    /// preview-settled poll reads this rather than <see cref="JobIsSettled"/> alone, since a preview
    /// job that failed inside its own render never stamps <c>preview</c> at all.</summary>
    public static bool PreviewIsPresent(JsonElement body) =>
        body.GetProperty("preview").ValueKind == JsonValueKind.Object;

    public static DateTime? PreviewAt(JsonElement body) =>
        body.GetProperty("preview") is { ValueKind: JsonValueKind.Object } preview
            ? preview.GetProperty("at").GetDateTime()
            : null;

    public static string? PreviewKey(JsonElement body) =>
        body.GetProperty("preview") is { ValueKind: JsonValueKind.Object } preview
            ? preview.GetProperty("key").GetString()
            : null;

    public static bool? PreviewStale(JsonElement body) =>
        body.GetProperty("preview") is { ValueKind: JsonValueKind.Object } preview
            ? preview.GetProperty("stale").GetBoolean()
            : null;

    /// <summary>A preview job is settled once it is BOTH off the queue (<see cref="JobIsSettled"/>)
    /// AND has actually stamped a <c>preview</c> object — the write-job precedent's bare
    /// <see cref="JobIsSettled"/> alone would also report "settled" the instant a preview render
    /// fails and only clears <c>job_error</c>, before <c>preview</c> ever lands.</summary>
    public static bool PreviewJobSettled(JsonElement body) => JobIsSettled(body) && PreviewIsPresent(body);

    /// <summary>Reads <c>station.ad_spot.script</c> straight out of Postgres (the Story412Arc
    /// <c>ReadAdSpotSponsorNameAsync</c> precedent) — STORY-423 AC2's own "the writer's output lands ON
    /// THE ROW" claim, verified against the database column the API layer reads FROM, not only against
    /// what that same API layer echoes back over GET.</summary>
    public static async Task<string?> ReadAdSpotScriptAsync(string stationConnectionString, long spotId)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select script from station.ad_spot where id = @id";
        cmd.Parameters.AddWithValue("id", spotId);
        return ToNullableString(await cmd.ExecuteScalarAsync());
    }

    /// <summary>Reads <c>station.ad_spot.preview_path</c> straight out of Postgres (the
    /// <see cref="ReadAdSpotScriptAsync"/> precedent, this same file) — STORY-424 AC2's own "the file
    /// lives under {Ads:LibraryRoot}/preview/&lt;spotId&gt;-&lt;key&gt;.wav" claim is about the ROW the
    /// render actually wrote, not merely what <see cref="GenWave.Host.Api.AdSpotDto"/> echoes back over the wire (that
    /// DTO never exposes the on-disk path at all — only <c>at</c>/<c>key</c>/<c>stale</c>).</summary>
    public static async Task<string?> ReadAdSpotPreviewPathAsync(string stationConnectionString, long spotId)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select preview_path from station.ad_spot where id = @id";
        cmd.Parameters.AddWithValue("id", spotId);
        return ToNullableString(await cmd.ExecuteScalarAsync());
    }

    /// <summary>Postgres NULL comes back as <see cref="DBNull"/>, not C# <see langword="null"/> — a bare
    /// <c>(string?)</c> cast throws <see cref="InvalidCastException"/> on it, which is exactly the shape
    /// of throw a shared sequential Arc must never let through (see this file's own house law on
    /// <see cref="TryPollUntilAsync"/>): a column read that can legally be NULL tolerates it here, once,
    /// rather than at every call site.</summary>
    static string? ToNullableString(object? scalar) => scalar is null or DBNull ? null : (string)scalar;

    /// <summary>Overwrites <c>station.ad_spot.preview_path</c> straight in Postgres, leaving every other
    /// column (crucially <c>preview_key</c>) untouched — STORY-424's own path-jail
    /// arrangement: a spec that means to prove <c>AdsController.PreviewWav</c>'s <see cref="GenWave.Ads.AdPreviewRoot.IsUnder"/>
    /// re-assertion, not its staleness check, must keep the stored key matching what
    /// <see cref="GenWave.Ads.AdPreviewKey.Compute"/> would still recompute — otherwise the request 404s
    /// at the staleness gate before the path guard ever runs, and the fact would pass for the wrong
    /// reason.</summary>
    public static async Task SetAdSpotPreviewPathAsync(string stationConnectionString, long spotId, string previewPath)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "update station.ad_spot set preview_path = @previewPath where id = @id";
        cmd.Parameters.AddWithValue("previewPath", previewPath);
        cmd.Parameters.AddWithValue("id", spotId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>STORY-424 AC2's own "no library.media row" claim (PLAN T442: a preview render never
    /// calls <c>ICastSegmentAuthor.AuthorAsync</c>, only <c>AssembleOnlyAsync</c>, so nothing on this
    /// path ever inserts a catalog row) — a plain row count, read before and after a preview settles,
    /// proves the claim directly against the table a write-mode render WOULD have grown.</summary>
    public static async Task<long> CountLibraryMediaRowsAsync(string libraryConnectionString)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select count(*) from library.media";
        return (long)(await cmd.ExecuteScalarAsync() ?? 0L);
    }
}
