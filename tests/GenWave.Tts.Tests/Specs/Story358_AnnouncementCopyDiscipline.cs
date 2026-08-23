// STORY-358 — The DJ says it: two fidelities, one fallback (SPEC F144.3/.4 · PLAN T342)
//
// BDD specification — xUnit. LlmCopyWriter.WriteAnnouncementAsync against a scripted completions
// handler (mirrors GenWave.Tts.Tests.Specs.Story350_ContextFactGate.cs's own BuildWriter idiom — a
// FakeHttpMessageHandler that scripts a reply PER CALL NUMBER, so a fact can prove the F138.4 re-ask
// ladder fires exactly once). The message is injected as an owner-trusted fact (SPEC F144.3, the F138
// gate's standing owner-trust rule); the ONE thing the gate additionally demands of a flavored
// announcement is that the case-folded message core survive in the rendered copy (the F68.4
// case-folded-survival precedent) — a drop rides the SAME re-ask ladder every other truth-gate
// violation does, and an exhausted ladder (or any other flavor-path failure) resolves to null, THE
// FALLBACK LAW's own signal for the caller to air the verbatim message instead (proven at the
// Orchestrator seam in GenWave.Orchestration.Tests.Specs.Story358_AnnouncementVendAndVerbatim).
//
// PLAN T342 review round 2 adds three more claim families this same file now pins: HIGH-1 (both
// sides of CheckContainment's substring test must normalize identically — a raw message's own
// double space/newline/apostrophe form must never make an otherwise-verbatim echo fail), HIGH-2
// (the owner-trust rule must also reach CheckClock's weekday/daypart half, not only CheckFacts'),
// and MEDIUM-4 (Hard degradation takes the verbatim floor immediately, zero LLM calls).

namespace GenWave.Tts.Tests.Specs;

using System.Net;
using System.Text;
using System.Text.Json;
using GenWave.Core.Domain;
using GenWave.Tts;
using GenWave.Tts.Tests.Fakes;
using Xunit;

public static class FeatureAnnouncementCopyDiscipline
{
    const string Message = "The garage sale starts at nine.";

    static SegmentRequest AnnouncementRequest() =>
        new(SegmentKind.Announcement, "af_heart", "GenWave", Track: null, DateTimeOffset.UtcNow, "test-station");

    /// <summary>Builds a REAL <see cref="LlmCopyWriter"/> against a fake completions handler that
    /// scripts its reply BY CALL NUMBER (1-based) — mirrors Story350_ContextFactGate.cs's own
    /// BuildWriter exactly, redefined here (the "redefine, don't cross-reference across spec files"
    /// convention) with only what THIS file's facts need. <paramref name="stationLocalNow"/> (PLAN
    /// T342 round 2, HIGH-2) rides a <see cref="FakeStationClockProvider"/> when supplied — null
    /// (the default) leaves the writer on its own system-clock fallback, unchanged for every fact
    /// that doesn't care which weekday it is. <paramref name="mode"/> (MEDIUM-4) rides a
    /// <see cref="FakeDegradationModeReader"/> — <see cref="DegradationMode.Normal"/> by default,
    /// the mode every fact that doesn't itself care about degradation wants.</summary>
    static (LlmCopyWriter Writer, List<string> RequestBodies) BuildWriter(
        Func<int, CancellationToken, Task<HttpResponseMessage>> respond, int timeoutSeconds = 5,
        DateTimeOffset? stationLocalNow = null, DegradationMode mode = DegradationMode.Normal)
    {
        var bodies = new List<string>();
        var handler = new FakeHttpMessageHandler(async (request, ct) =>
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            bodies.Add(body);
            return await respond(bodies.Count, ct);
        });
        var writer = new LlmCopyWriter(
            new TemplateCopyWriter(new PatterTemplateRenderer()),
            new SingleHandlerHttpClientFactory(handler),
            new TestOptionsMonitor<LlmOptions>(new LlmOptions
            {
                Endpoint = "http://fake-llm.local", Model = "test-model", TimeoutSeconds = timeoutSeconds,
                MaxCopyChars = 450,
            }),
            new LlmCopyStatusHolder(),
            new FakeActivePersonaAccessor(),
            new CapturingLogger<LlmCopyWriter>(),
            TimeProvider.System,
            new LlmCallRecorder(
                new LlmCallRing(new TestOptionsMonitor<LlmOptions>(new LlmOptions())),
                new LlmCallCauseCounters(TimeProvider.System)),
            new FakeDegradationModeReader { CurrentMode = mode },
            stationLocalNow is { } now ? new FakeStationClockProvider(now) : null);
        return (writer, bodies);
    }

    static Task<HttpResponseMessage> Ok(string content) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = CompletionsBody(content),
    });

    static Task<HttpResponseMessage> Unreachable() =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

    static async Task<HttpResponseMessage> DelayThenOk(TimeSpan delay, CancellationToken ct, string content = Message)
    {
        await Task.Delay(delay, ct);
        return await Ok(content);
    }

    static StringContent CompletionsBody(string content) => new(
        JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } }),
        Encoding.UTF8, "application/json");

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

    public sealed class ScenarioFlavoredCopyCarriesTheCore
    {
        [Fact]
        public async Task TheAiredCopyContainsTheCaseFoldedMessageCore()
        {
            // Given a reply that wraps the message in DJ color, case-folded rather than
            // byte-identical (the F68.4 survival precedent — the AIRED capitals differ)
            var (writer, bodies) = BuildWriter(
                (_, _) => Ok("Hey neighbors! THE GARAGE SALE STARTS AT NINE. Come on by!"));

            // When the flavored render resolves
            var result = await writer.WriteAnnouncementAsync(AnnouncementRequest(), Message, CancellationToken.None);

            // Then the case-folded core survives in the aired copy, and no re-ask was needed
            Assert.NotNull(result);
            Assert.Contains(Message, result, StringComparison.OrdinalIgnoreCase);
            Assert.Single(bodies);
        }

        [Fact]
        public async Task TheTruthGateRaisesNoFabricationViolationForTheMessageItself()
        {
            // Given a message that itself carries a digit run, and a reply that echoes it verbatim
            // as part of working the message in
            const string digitMessage = "Free hot dogs at 6pm, first come first served.";
            var (writer, bodies) = BuildWriter(
                (_, _) => Ok($"Hey everyone, don't miss it: {digitMessage}"));

            // When the render resolves
            var result = await writer.WriteAnnouncementAsync(
                AnnouncementRequest(), digitMessage, CancellationToken.None);

            // Then the gate never re-asks — the message is its OWN fact block (the standing
            // owner-trust rule, mechanical), so the digit "6" the copy repeats is supported by the
            // message that introduced it, never flagged as an invented claim
            Assert.NotNull(result);
            Assert.Single(bodies);
        }

        [Fact]
        public async Task CopyThatDropsTheCoreIsAGateRejectAndRidesTheReaskLadder()
        {
            // Given a first reply that drops the message entirely, and a second that includes it
            var (writer, bodies) = BuildWriter((call, _) => Ok(call == 1
                ? "Stay tuned for more great music coming your way!"
                : $"Quick note from the station: {Message}"));

            // When the render goes through the ladder
            var result = await writer.WriteAnnouncementAsync(AnnouncementRequest(), Message, CancellationToken.None);

            // Then exactly one re-ask fired, and the recovered reply is what airs
            Assert.Equal(2, bodies.Count);
            Assert.NotNull(result);
            Assert.Contains(Message, result, StringComparison.OrdinalIgnoreCase);

            // And the re-ask's own user prompt names the requirement it violated — a concrete ask
            // to correct, not a bare "try again"
            var reaskPrompt = ExtractUserContent(bodies[1]);
            Assert.Contains("announcement message", reaskPrompt, StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class ScenarioTheFallbackLaw
    {
        [Fact]
        public async Task AnExhaustedReaskLadderDegradesToTheVerbatimRead()
        {
            // Given BOTH the first reply and its re-ask drop the message core
            var (writer, bodies) = BuildWriter((_, _) => Ok("Great tunes all night long, stick around!"));

            // When the ladder exhausts its one re-ask
            var result = await writer.WriteAnnouncementAsync(AnnouncementRequest(), Message, CancellationToken.None);

            // Then this writer hands back null — THE FALLBACK LAW's own signal for the caller to air
            // the owner's verbatim message instead — never partial or violating copy
            Assert.Equal(2, bodies.Count);
            Assert.Null(result);
        }

        [Fact]
        public async Task AnUnreachableLlmDegradesToTheVerbatimRead()
        {
            // Given the completions endpoint is unreachable (a non-2xx status)
            var (writer, _) = BuildWriter((_, _) => Unreachable());

            // When the flavor attempt is made
            var result = await writer.WriteAnnouncementAsync(AnnouncementRequest(), Message, CancellationToken.None);

            // Then it resolves to null, the SAME signal an exhausted ladder produces
            Assert.Null(result);
        }

        [Fact]
        public async Task ABlownRenderBudgetDegradesToTheVerbatimRead()
        {
            // Given a reply that would arrive well past this render's own Llm:TimeoutSeconds budget
            var (writer, _) = BuildWriter((_, ct) => DelayThenOk(TimeSpan.FromSeconds(3), ct), timeoutSeconds: 1);

            // When the budget elapses mid-call
            var result = await writer.WriteAnnouncementAsync(AnnouncementRequest(), Message, CancellationToken.None);

            // Then it resolves to null — never a longer feeder hold, and never a stall
            Assert.Null(result);
        }

        [Fact]
        public async Task TheAnnouncementAirsInEveryDegradedCase()
        {
            // Given the three independent degrade triggers F144.4 names — an exhausted ladder, an
            // unreachable endpoint, and a blown render budget
            var (exhaustedLadder, _) = BuildWriter((_, _) => Ok("Great tunes all night long, stick around!"));
            var (unreachable, _) = BuildWriter((_, _) => Unreachable());
            var (budgetBlown, _) = BuildWriter((_, ct) => DelayThenOk(TimeSpan.FromSeconds(3), ct), timeoutSeconds: 1);

            // When each attempts the SAME flavored render
            var results = await Task.WhenAll(
                exhaustedLadder.WriteAnnouncementAsync(AnnouncementRequest(), Message, CancellationToken.None),
                unreachable.WriteAnnouncementAsync(AnnouncementRequest(), Message, CancellationToken.None),
                budgetBlown.WriteAnnouncementAsync(AnnouncementRequest(), Message, CancellationToken.None));

            // Then every one resolves to the SAME uniform null signal — which is exactly what lets
            // the caller's own single `?? message` cover all three triggers without knowing which one
            // fired (SPEC F144.6's "still airs verbatim within one break cycle" acceptance).
            Assert.All(results, Assert.Null);
        }
    }

    // HIGH-1 (PLAN T342 review round 2) — CopyClaims.CheckContainment now normalizes BOTH sides
    // (the aired copy AND the raw owner message) identically before the substring test: the SAME
    // ApplyCopyHygiene collapse the aired copy already went through, plus the F68.4 fold-both-sides
    // apostrophe discipline. Red-proof: reverting CheckContainment to compare `copy` against the RAW
    // `requiredCore` (no hygiene, no apostrophe fold) turns every fact below red — the double-space
    // and newline facts because hygiene never runs on the raw side, the apostrophe facts because a
    // straight/curly mismatch no longer folds to the same glyph.
    public sealed class ScenarioBothSidesNormalizeForContainment
    {
        [Fact]
        public async Task AMessageWithAnInternalDoubleSpaceSurvivesAVerbatimEcho()
        {
            // Given an owner message typed with an internal double space
            const string message = "Free  parking today only.";
            var (writer, bodies) = BuildWriter((_, _) => Ok($"Hey folks, quick note: {message} See you there!"));

            // When the flavored render resolves
            var result = await writer.WriteAnnouncementAsync(AnnouncementRequest(), message, CancellationToken.None);

            // Then the double space collapses on BOTH sides before the substring test, so the
            // otherwise byte-for-byte echo passes containment on the FIRST call — no re-ask needed
            Assert.NotNull(result);
            Assert.Single(bodies);
        }

        [Fact]
        public async Task AMessageWithAnEmbeddedNewlineSurvivesAVerbatimEcho()
        {
            // Given an owner message with an embedded newline
            const string message = "Free parking today only.\nSee you there!";
            var (writer, bodies) = BuildWriter((_, _) => Ok($"Hey folks — {message}"));

            // When the flavored render resolves
            var result = await writer.WriteAnnouncementAsync(AnnouncementRequest(), message, CancellationToken.None);

            // Then the newline flattens to a space on BOTH sides (the SAME NewlinePattern hygiene the
            // aired copy already carries), so the echo passes containment on the FIRST call
            Assert.NotNull(result);
            Assert.Single(bodies);
        }

        [Fact]
        public async Task AStraightApostropheInTheMessageSurvivesACurlyEchoInTheReply()
        {
            // Given an owner message typed with a straight apostrophe (U+0027)
            const string message = "Don't miss the block party tonight.";
            // And a reply that echoes it with the curly apostrophe (U+2019) instead
            var (writer, bodies) = BuildWriter(
                (_, _) => Ok("Hey everyone — Don’t miss the block party tonight. See you there!"));

            var result = await writer.WriteAnnouncementAsync(AnnouncementRequest(), message, CancellationToken.None);

            // Then the apostrophe form folds to the same glyph on both sides, so the echo passes
            // containment on the FIRST call despite the mismatched form
            Assert.NotNull(result);
            Assert.Single(bodies);
        }

        [Fact]
        public async Task ACurlyApostropheInTheMessageSurvivesAStraightEchoInTheReply()
        {
            // Given an owner message typed (or pasted) with the curly apostrophe (U+2019)
            const string message = "Don’t miss the block party tonight.";
            // And a reply that echoes it with the straight apostrophe (U+0027) instead
            var (writer, bodies) = BuildWriter(
                (_, _) => Ok("Hey everyone — Don't miss the block party tonight. See you there!"));

            var result = await writer.WriteAnnouncementAsync(AnnouncementRequest(), message, CancellationToken.None);

            // Then the inverse cross-combination folds identically and also passes on the FIRST call
            Assert.NotNull(result);
            Assert.Single(bodies);
        }
    }

    // MEDIUM-B (PLAN T342 review round 3) — CopyClaims.CheckContainment treats an EMPTY (or
    // whitespace-only) normalized core as a violation, never a vacuous pass. A message that is pure
    // stage-direction/markdown markup ("*urgent*", "[Reminder]") hygiene-strips to nothing, and
    // string.Contains("") is true for ANY copy — so before this fix, the gate silently waved through
    // whatever the model wrote and the owner's own message never reached air at all (no violation, no
    // verbatim floor, a silent no-op). Red-proof: reverting the empty-core guard in CheckContainment
    // turns the fact below red — bodies.Count drops from 2 (the exhausted ladder) to 1 (a false
    // first-call pass), and result no longer contains the raw "*urgent*" text.
    public sealed class ScenarioAnEmptyNormalizedCoreIsAViolation
    {
        [Fact]
        public async Task AnAllMarkupMessageRidesTheLadderToExhaustionAndDegradesToTheVerbatimRead()
        {
            // Given an owner message that is pure stage-direction markup — ApplyCopyHygiene strips it
            // to an empty string
            const string message = "*urgent*";
            // And a scripted reply, on both the first call and the re-ask, with no relation to the
            // message at all
            var (writer, bodies) = BuildWriter(
                (_, _) => Ok("Stay tuned for more great music coming your way!"));

            // When the flavored render goes through the ladder
            var result = await writer.WriteAnnouncementAsync(AnnouncementRequest(), message, CancellationToken.None);

            // Then the empty normalized core is a violation on EVERY attempt (never a vacuous pass),
            // so the one re-ask fires and still fails, exhausting the ladder — the writer hands back
            // null, THE FALLBACK LAW's signal for the caller to air the raw "*urgent*" message verbatim
            Assert.Equal(2, bodies.Count);
            Assert.Null(result);
        }
    }

    // HIGH-2 (PLAN T342 review round 2) — the owner-trust rule (SPEC F144.3) now reaches
    // CopyClaims.CheckClock, not only CheckFacts: a weekday/daypart claim that falls entirely inside
    // a literal quote of the owner's own message is exempt from the clock check, exactly like the
    // existing track-title exemption. Red-proof: dropping the ownerMessage argument CheckTruthGate
    // now passes to CheckClock (reverting it to trackTitle-only) turns the FIRST fact below red — the
    // owner's own "this Saturday" would then rides the re-ask ladder and never clears it, since the
    // ladder's one re-ask asks the model to correct a "mistake" that was never the model's to begin
    // with.
    public sealed class ScenarioOwnerTrustReachesTheClockCheck
    {
        static readonly DateTimeOffset Wednesday = new(2026, 8, 19, 15, 0, 0, TimeSpan.Zero);

        [Fact]
        public async Task AThisWeekdayInTheMessageAirsOnADifferentWeekdayWithNoReask()
        {
            // Given the owner's own message names a present-frame weekday, and the station's actual
            // local clock is a completely different day
            const string message = "Bake sale this Saturday at the community hall.";
            var (writer, bodies) = BuildWriter(
                (_, _) => Ok($"Hey neighbors! {message} Come on by."), stationLocalNow: Wednesday);

            // When the flavored render resolves
            var result = await writer.WriteAnnouncementAsync(AnnouncementRequest(), message, CancellationToken.None);

            // Then the gate stays quiet — the owner's own "this Saturday" is exempt from the clock
            // check (it falls entirely inside the message's own literal text), so the FIRST reply
            // airs with no re-ask, even on a Wednesday
            Assert.NotNull(result);
            Assert.Contains(message, result, StringComparison.OrdinalIgnoreCase);
            Assert.Single(bodies);
        }

        [Fact]
        public async Task AnLlmAddedWeekdayOutsideTheMessageStillRidesTheReaskLadder()
        {
            // Given a message that names no day at all, and a station clock that is a Wednesday
            const string message = "The garage sale starts at nine.";
            var (writer, bodies) = BuildWriter((call, _) => Ok(call == 1
                // First reply invents a weekday claim OUTSIDE any quote of the message
                ? $"It's Saturday, folks! {message}"
                // Second (re-ask) reply drops the invented claim
                : $"Quick note from the station: {message}"), stationLocalNow: Wednesday);

            // When the render goes through the ladder
            var result = await writer.WriteAnnouncementAsync(AnnouncementRequest(), message, CancellationToken.None);

            // Then the LLM-added "It's Saturday" — outside the owner's own message span — still
            // rejects and rides the F138.4 re-ask ladder exactly as before this fix: the owner-trust
            // exemption only ever covers the message's OWN literal words, never anything the model
            // adds beyond it (the Q2e "never even ask" direction is preserved for genuine additions)
            Assert.Equal(2, bodies.Count);
            Assert.NotNull(result);
            Assert.Contains(message, result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task AnLlmAddedDigitOutsideTheMessageStillRidesTheReaskLadder()
        {
            // Given a message that carries no digit at all
            const string message = "The garage sale starts this weekend.";
            var (writer, bodies) = BuildWriter((call, _) => Ok(call == 1
                // First reply invents a digit the message never stated
                ? $"Starting at 9 sharp, folks! {message}"
                // Second (re-ask) reply drops the invented digit
                : $"Quick note from the station: {message}"));

            // When the render goes through the ladder
            var result = await writer.WriteAnnouncementAsync(AnnouncementRequest(), message, CancellationToken.None);

            // Then the LLM-added digit "9" — unsupported by the message, the announcement's own fact
            // block (SPEC F144.3) — still rejects and rides the ladder, exactly as before this task
            Assert.Equal(2, bodies.Count);
            Assert.NotNull(result);
            Assert.Contains(message, result, StringComparison.OrdinalIgnoreCase);
        }
    }

    // MEDIUM-4 (PLAN T342 review round 2) — the Hard-degradation ruling: in DegradationMode.Hard,
    // WriteAnnouncementAsync skips the flavor attempt entirely and returns null immediately, with
    // ZERO LLM calls — the owner's own message still airs, through the caller's own F144.4 verbatim
    // fallback. Red-proof: deleting the degradationMode.CurrentMode guard at the top of
    // WriteAnnouncementAsync turns the FIRST fact below red (bodies.Count goes from 0 to 1).
    public sealed class ScenarioHardDegradationTakesTheVerbatimFloorImmediately
    {
        [Fact]
        public async Task HardModeMakesZeroLlmCallsAndResolvesToNull()
        {
            // Given the station is in Hard degradation
            var (writer, bodies) = BuildWriter(
                (_, _) => Ok(Message), mode: DegradationMode.Hard);

            // When a flavored render is attempted
            var result = await writer.WriteAnnouncementAsync(AnnouncementRequest(), Message, CancellationToken.None);

            // Then it resolves to null — the caller's own signal to air the verbatim message — and
            // the fake completions handler was never invoked at all
            Assert.Null(result);
            Assert.Empty(bodies);
        }

        [Fact]
        public async Task NormalModeStillAttemptsTheFlavorRender()
        {
            // Given the station is in ordinary Normal degradation (the BuildWriter default)
            var (writer, bodies) = BuildWriter((_, _) => Ok($"Hey neighbors! {Message}"));

            // When a flavored render is attempted
            var result = await writer.WriteAnnouncementAsync(AnnouncementRequest(), Message, CancellationToken.None);

            // Then the flavor attempt fires exactly as it always has — this ruling changes Hard mode
            // alone, never Normal
            Assert.NotNull(result);
            Assert.Single(bodies);
        }

        [Fact]
        public async Task SoftModeStillAttemptsTheFlavorRender()
        {
            // Given the station is in Soft degradation — announcements are not a cadence-gated kind
            // (SPEC F144.1's own "cadence-independent" ruling), so Soft's own cadence throttle, which
            // lives entirely inside DegradationGatedCopyWriter's ordinary ISegmentCopyWriter path,
            // never applies here at all
            var (writer, bodies) = BuildWriter(
                (_, _) => Ok($"Hey neighbors! {Message}"), mode: DegradationMode.Soft);

            // When a flavored render is attempted
            var result = await writer.WriteAnnouncementAsync(AnnouncementRequest(), Message, CancellationToken.None);

            // Then the flavor attempt fires exactly as it does in Normal mode
            Assert.NotNull(result);
            Assert.Single(bodies);
        }
    }
}
