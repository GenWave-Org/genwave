// STORY-358 — The DJ says it: the flavored path wired end to end (SPEC F144.3/.4/.6 · PLAN T342)
//
// BDD specification — xUnit. Drives the REAL production DI graph (WebApplicationFactory<Program>, via
// the shared Support/LlmCompletionsStub.cs types T335 extracted — the SAME precedent
// Story350_TruthLaneEndToEnd.cs already establishes for the truth lane) rather than a hand-built
// collaborator graph. IAnnouncementCopyWriter is resolved straight off the booted container and
// WriteAnnouncementAsync called directly — the highest honestly-reachable seam: there is no HTTP
// surface that reaches it (the announcement vend step lives in the Orchestrator's own background
// playout loop, which LlmCompletionsWebFactory removes along with every other IHostedService), and
// every collaborator downstream of it (the named HttpClient, LlmCallRing/LlmCallCauseCounters) is the
// exact SAME production object GET /api/llm-calls reads moments later over a real authenticated
// request.

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

/// <summary>Wire shape of one row from <c>GET /api/llm-calls</c> — the narrow slice this file needs
/// (mirrors Story350_TruthLaneEndToEnd.cs's own narrower-than-the-DTO idiom).</summary>
file sealed record LlmCallRow(string Kind, string Cause);

file sealed record LlmCallsSurfaceResponse(IReadOnlyList<LlmCallRow> Calls);

public static class FeatureAnnouncementFlavorEndToEnd
{
    const string Message = "The garage sale starts at nine.";
    const string PoisonedCopy = "Great tunes all night long, stick around!";
    const string CleanCopy = $"Quick note from the station: {Message}";

    static async Task LoginAsync(HttpClient client, string password)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }

    static SegmentRequest AnnouncementRequest() =>
        new(SegmentKind.Announcement, "af_heart", "GenWave", Track: null, DateTimeOffset.UtcNow, "test-station");

    public static class ScenarioAPoisonedThenCleanReply
    {
        [Fact]
        public static async Task The_core_survives_after_one_reask_and_the_wire_shows_the_announcement_lane()
        {
            // Given a real production host wired to a real (stub) completions endpoint whose FIRST
            // reply drops the message core entirely, and whose re-ask reply includes it...
            await using var stub = await LlmCompletionsStub.StartAsync();
            stub.QueueReplies(PoisonedCopy, CleanCopy);
            await using var factory = new LlmCompletionsWebFactory(stub.BaseUri.ToString());
            var client = factory.CreateClient();
            await LoginAsync(client, LlmCompletionsWebFactory.Password);

            var writer = factory.Services.GetRequiredService<IAnnouncementCopyWriter>();

            // When it renders through the REAL production writer...
            var result = await writer.WriteAnnouncementAsync(AnnouncementRequest(), Message, CancellationToken.None);

            // Then the gate re-asked exactly once, and the recovered copy — containing the
            // case-folded message core — is what this seam hands back.
            Assert.Equal(2, stub.Requests.Count);
            Assert.NotNull(result);
            Assert.Contains(Message, result, StringComparison.OrdinalIgnoreCase);

            // And the F138.5 guard line rode both system prompts — the SAME prompt-hardening pass
            // every other LLM-authored kind gets, unchanged for this one.
            Assert.All(
                stub.Requests,
                req => Assert.Contains("Never name another day or time of day.", req.SystemPrompt));

            // When the real admin endpoint is read back over an authenticated request...
            var response = await client.GetFromJsonAsync<LlmCallsSurfaceResponse>("/api/llm-calls");
            Assert.NotNull(response);

            // Then the rejected first call and the accepted re-ask both carry the ANNOUNCEMENT lane
            // (never folded into ordinary "copy" noise) — the F139 bench/cause surface seeing this
            // lane, this task's own binding carry-forward.
            Assert.Contains(
                response!.Calls, row => row is { Kind: "announcement", Cause: "truthgatereject" });
            Assert.Contains(
                response.Calls, row => row is { Kind: "announcement", Cause: "success" });
        }
    }

    public static class ScenarioBothRepliesDropTheCore
    {
        [Fact]
        public static async Task The_ladder_exhausts_and_the_verbatim_floor_signal_airs()
        {
            // Given a real production host, and EVERY reply (first call and its re-ask alike)
            // drops the message core...
            await using var stub = await LlmCompletionsStub.StartAsync();
            stub.ReplyContent = PoisonedCopy;
            await using var factory = new LlmCompletionsWebFactory(stub.BaseUri.ToString());
            var client = factory.CreateClient();
            await LoginAsync(client, LlmCompletionsWebFactory.Password);

            var writer = factory.Services.GetRequiredService<IAnnouncementCopyWriter>();

            // When the render exhausts its one re-ask...
            var result = await writer.WriteAnnouncementAsync(AnnouncementRequest(), Message, CancellationToken.None);

            // Then this seam hands back null — THE FALLBACK LAW's own signal — never the
            // still-violating LLM text; exactly one re-ask, never a retry storm.
            Assert.Null(result);
            Assert.Equal(2, stub.Requests.Count);

            // And the real admin endpoint shows BOTH rejections on the announcement lane.
            var response = await client.GetFromJsonAsync<LlmCallsSurfaceResponse>("/api/llm-calls");
            Assert.NotNull(response);
            Assert.Equal(2, response!.Calls.Count(row => row is { Kind: "announcement", Cause: "truthgatereject" }));
        }
    }
}
