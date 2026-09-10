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

    public static async Task<JsonElement> GetSpotAsync(HttpClient client, long spotId)
    {
        var response = await client.GetAsync($"/api/ads/{spotId}");
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"arrange: GET /api/ads/{spotId} unexpectedly returned {response.StatusCode}");

        var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return body.RootElement.Clone();
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
        return (string?)await cmd.ExecuteScalarAsync();
    }
}
