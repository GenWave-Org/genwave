// STORY-428 — The writer knows the sponsor (worker half: SPEC F174.8 · PLAN T443)
//
// STORY-428 spans two test projects: the prompt grammar itself — which facts get a line, the tone
// fallback ladder, the anti-label-smuggling flatten — is pinned in GenWave.Tts.Tests (this feature's
// own prompt is a pure function of an AdScriptWriteRequest). THIS file pins the OTHER half: that
// AdSpotWorker.GenerateOneAsync (GenWave.Ads) actually reads a resolved Sponsor's six facts off
// ISponsorStore.GetAsync and hands every one of them, plus the tone fallback, to the REAL
// AdScriptWriter — proven against the deployed call site itself (the AdSpotWorkerHarness precedent,
// Story417_OwnerSponsorsAreReal.cs's own ScenarioTheWorkerWiresTheSponsorFieldsItResolves), not just
// against AdScriptPromptBuilder in isolation.

using System.Net;
using System.Text;
using System.Text.Json;
using GenWave.Ads.Tests.Fakes;
using GenWave.Ads.Tests.Support;
using GenWave.Core.Domain;

namespace GenWave.Ads.Tests.Specs;

public static class FeatureTheWorkerHandsTheWriterTheSponsorsFacts
{
    static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    const string SponsorName = "Cravin's Diner";

    // The Story417_OwnerSponsorsAreReal ScriptNamingTheSponsor precedent (this project's own Specs
    // folder) — well under the 30s target with a 555 number, so nothing but the sponsor facts
    // themselves are under test here; the spot always lands.
    const string WellFormedReply =
        $"ANNOUNCER: {SponsorName} has a deal so good it's almost illegal.\n" +
        "VOICE1: Almost. Stop by and taste the difference tonight.\n" +
        "ANNOUNCER: Call 555-0142 - that's 555-0142 - the deal of the year.";

    /// <summary>A <see cref="FakeHttpMessageHandler"/> that always accepts with
    /// <see cref="WellFormedReply"/>, capturing every outbound request body in arrival order — the
    /// Story350_ContextFactGate.cs (GenWave.Tts.Tests) role-keyed-extraction precedent, read back by
    /// <see cref="ExtractUserContent"/> below.</summary>
    static (FakeHttpMessageHandler Handler, List<string> RequestBodies) CapturingHandler()
    {
        var bodies = new List<string>();
        var handler = new FakeHttpMessageHandler(async (request, ct) =>
        {
            bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = WellFormedReply } } } }),
                    Encoding.UTF8, "application/json"),
            };
        });
        return (handler, bodies);
    }

    // Role-keyed, not positional (the Story350_ContextFactGate.cs precedent) — looks up the message BY
    // its own "role" field rather than trusting messages[0]/messages[1] to stay system-then-user.
    static string ExtractUserContent(string requestBodyJson)
    {
        using var doc = JsonDocument.Parse(requestBodyJson);
        foreach (var message in doc.RootElement.GetProperty("messages").EnumerateArray())
        {
            if (message.GetProperty("role").GetString() == "user")
                return message.GetProperty("content").GetString() ?? "";
        }

        return "";
    }

    public sealed class ScenarioTheWorkerWiresEverySponsorFact
    {
        [Fact]
        public async Task TheWorkerPassesEverySponsorFactAndTheHouseToneToTheWriter()
        {
            // Given an owner brief with no tone of its own, naming a sponsor with ALL five facts and a
            // house tone on file...
            var (handler, bodies) = CapturingHandler();
            var harness = AdSpotWorkerHarness.Build(Now, llmHandler: handler);
            harness.Briefs.AddEnabled(SponsorName, premise: "A deal too good to pass up");
            var sponsorId = harness.Briefs.SponsorIdsByBrand[SponsorName];
            harness.Sponsors.WithFacts(
                sponsorId, tagline: "Open at six", about: "A retro diner on Main Street",
                phone: "(406) 222-0100", address: "12 Main Street", website: "https://cravinsdiner.example",
                tone: "warm");

            // When the worker ticks...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the writer's own user content carries every fact AND the house tone — dropping any
            // single one of these six arguments at the worker's call site reds this fact.
            var userContent = ExtractUserContent(Assert.Single(bodies));
            Assert.Contains("Tagline: Open at six", userContent);
            Assert.Contains("About: A retro diner on Main Street", userContent);
            Assert.Contains("Phone: (406) 222-0100", userContent);
            Assert.Contains("Address: 12 Main Street", userContent);
            Assert.Contains("Website: https://cravinsdiner.example", userContent);
            Assert.Contains("Tone: warm", userContent);
        }

        [Fact]
        public async Task ABriefsToneOutranksTheHouseToneAtTheWorker()
        {
            // Given the SAME sponsor, every fact still on file, but this time the BRIEF also carries
            // its own tone...
            var (handler, bodies) = CapturingHandler();
            var harness = AdSpotWorkerHarness.Build(Now, llmHandler: handler);
            harness.Briefs.AddEnabled(SponsorName, premise: "A deal too good to pass up", tone: "urgent");
            var sponsorId = harness.Briefs.SponsorIdsByBrand[SponsorName];
            harness.Sponsors.WithFacts(
                sponsorId, tagline: "Open at six", about: "A retro diner on Main Street",
                phone: "(406) 222-0100", address: "12 Main Street", website: "https://cravinsdiner.example",
                tone: "warm");

            // When the worker ticks...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the brief's own tone reaches the writer, and the house tone does not.
            var userContent = ExtractUserContent(Assert.Single(bodies));
            Assert.Contains("Tone: urgent", userContent);
            Assert.DoesNotContain("Tone: warm", userContent);
        }
    }
}
