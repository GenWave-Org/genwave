using System.Net;
using System.Text;
using System.Text.Json;
using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Support;

/// <summary>
/// The shared LLM-completions test double for STORY-422/STORY-423 (PLAN T441) — wraps a
/// <see cref="FakeHttpMessageHandler"/> that answers every <c>AdScriptWriter</c> completion call a
/// real <c>WebApplicationFactory&lt;Program&gt;</c>-hosted <c>AdSpotJobService</c> makes, so both
/// Story422_AdSpotJobs.cs and Story423_WriteJob.cs can drive the SAME real write-job pipeline off one
/// completions endpoint per Arc without hand-rolling the JSON envelope twice.
///
/// <para>
/// <b>Routed by sponsor name, not by call order (SPEC F174.8).</b> <see cref="AdScriptPromptBuilder.BuildUserContent"/>
/// embeds a literal <c>"Sponsor: {name}"</c> as the first line of every outgoing completion request's
/// user content — the one stable, content-addressable fact this router keys on
/// (<see cref="RouteSponsor"/>), rather than assuming a fixed number of calls arrive in a fixed order
/// (which a station-wide, one-job-at-a-time queue makes an unsafe assumption the instant more than one
/// Scenario's own spot is in flight against the SAME Arc). A request naming no routed sponsor falls
/// through to <see cref="WellFormedReply"/> — the default "this write succeeds" case every Scenario
/// that does not care about the write's own outcome can rely on without registering a route at all.
/// </para>
/// </summary>
internal sealed class AdScriptCompletionsRouter
{
    /// <summary>A short two-voice script that passes the real <c>AdScriptValidator</c> for a 30s spot
    /// (the <c>AdSpotWorkerHarness.WellFormedReply</c>/Story412's own short ANNOUNCER/VOICE1 precedent)
    /// — proven end to end against the real <c>RollingPatterDurationEstimator</c>, never a fake
    /// duration estimator.</summary>
    public const string WellFormedReply =
        "ANNOUNCER: Cravin's Diner has a deal so good it's almost illegal.\n" +
        "VOICE1: Almost. Stop by and taste the difference tonight.\n" +
        "ANNOUNCER: Call 555-0142 - that's 555-0142 - Cravin's Diner.";

    // A route is appended by RouteSponsor on whichever thread the Arc's own arrangement runs on, and
    // read by the handler below on the station's own consumer thread — a plain List<> read racing that
    // append is undefined behaviour, not just a stale read. The lock guards only the append and the
    // snapshot; the snapshot itself is enumerated OUTSIDE the lock, so a route's own respond delegate
    // (which may itself await) never runs while holding it.
    readonly Lock routesLock = new();
    readonly List<(string Marker, Func<HttpRequestMessage, Task<HttpResponseMessage>> Respond)> routes = [];

    public FakeHttpMessageHandler Handler { get; }

    public AdScriptCompletionsRouter()
    {
        Handler = new FakeHttpMessageHandler(async (request, ct) =>
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);

            (string Marker, Func<HttpRequestMessage, Task<HttpResponseMessage>> Respond)[] snapshot;
            lock (routesLock)
                snapshot = [.. routes];

            foreach (var (marker, respond) in snapshot)
            {
                if (body.Contains(marker, StringComparison.Ordinal))
                    return await respond(request);
            }

            return WellFormedResponse();
        });
    }

    /// <summary>How many completion requests this router has answered so far, across every sponsor —
    /// STORY-422 AC6's own "no call ever arrived" proof reads this as a before/after delta rather than
    /// inspecting <see cref="FakeHttpMessageHandler.Requests"/> directly.</summary>
    public int RequestCount => Handler.Requests.Count;

    /// <summary>Routes every completion request whose body names <paramref name="sponsorName"/> (as
    /// the FIRST line <see cref="AdScriptPromptBuilder.BuildUserContent"/> writes) to
    /// <paramref name="respond"/> instead of the default <see cref="WellFormedReply"/> — STORY-423
    /// AC4's own forced-failure case registers a route answering a well-formed 200 whose script fails
    /// the real validator on both the ask and the re-ask (<see cref="CompletionsResponse"/>), so any
    /// respond delegate a caller supplies, transport fault or otherwise, is free to answer however that
    /// caller's own scenario needs.</summary>
    public void RouteSponsor(string sponsorName, Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
    {
        lock (routesLock)
            routes.Add(($"Sponsor: {sponsorName}", respond));
    }

    /// <summary>Routes <paramref name="sponsorName"/>'s completion requests through a new
    /// <see cref="GatedRoute"/> and returns it — STORY-422 AC1's own way to prove two concurrently
    /// dispatched jobs never actually overlap INSIDE the writer, without relying on
    /// <c>IOnAirRenderSignal.InFlight</c> (which by construction can only ever report ONE spot id
    /// waiting at a time, so gating on it proves nothing about concurrency reaching this far).</summary>
    public GatedRoute RouteGated(string sponsorName)
    {
        var route = new GatedRoute();
        RouteSponsor(sponsorName, route.RespondAsync);
        return route;
    }

    public static HttpResponseMessage WellFormedResponse() => CompletionsResponse(WellFormedReply);

    public static HttpResponseMessage CompletionsResponse(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } }),
            Encoding.UTF8, "application/json"),
    };
}
