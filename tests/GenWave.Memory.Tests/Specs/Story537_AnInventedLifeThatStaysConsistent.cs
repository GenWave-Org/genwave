// STORY-537 — An invented life that stays consistent (gh-#293 gh-#920 · SPEC F218 · PLAN T700 T704)
//
// BDD specification — xUnit. Extraction, recall and the cap live here; AC2 (render not delayed) lives in tests/GenWave.Tts.Tests/Specs/Story537_TheAirPathIsNotDelayed.cs.
// Every fact is pending at plan time (2026-11-08); the named task activates its facts, watches them fail,
// then makes them pass.

using GenWave.Core.Domain;
using GenWave.Memory.Tests.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Memory.Tests.Specs;

public static class FeatureAnInventedLifeThatStaysConsistent
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — A claim is extracted and stored
    public sealed class ScenarioAClaimIsExtractedAndStored
    {
        // entry point: LifeExtractionService fed by accepted copies (fake LLM)
        // Given an accepted copy "my beagle Gus howled through the whole intro"
        // Given a fake LLM returning [{"claim":"has a beagle named Gus","about":"self","category":"home"}]
        // When  the extractor runs

        readonly LifeHarness harness = new("""[{"claim":"has a beagle named Gus","about":"self","category":"home"}]""");

        public ScenarioAClaimIsExtractedAndStored() =>
            harness.RunAsync("My beagle Gus howled through the whole intro").GetAwaiter().GetResult();

        [Fact]
        public void ThePersonaGainsAPersonaMemoryRowKindLifeSourceExtractedContentHasABeagleNamedGus()
        {
            var row = Assert.Single(harness.Store.Rows);
            Assert.Equal((LifeHarness.Sal, MemoryKinds.Life, "extracted", "has a beagle named Gus"), (row.PersonaId, row.Kind, row.Source, row.Content));
        }

        [Theory]
        [InlineData("```json\n[{\"claim\":\"has a beagle named Gus\",\"about\":\"self\"}]\n```")]
        public async Task AFencedReplyIsStillRead(string fenced)
        {
            var other = new LifeHarness(fenced);

            await other.RunAsync("Gus howled");

            Assert.Single(other.Store.Rows);
        }

        [Fact]
        public void TheModelWasShownTheCopy() => Assert.Contains("beagle Gus howled", Assert.Single(harness.Llm.UserPrompts));
    }

    // AC2 (the real queue) — the model is slow, the caller is not
    public sealed class ScenarioTheModelIsSlow
    {
        [Fact]
        public async Task EnqueueReturnsWhileTheModelIsStillWorkingAndTheClaimLandsOnceItFinishes()
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var store = new InMemoryLifeStore();
            var service = new LifeExtractionService(
                new FakeLifeClaimLlm("""[{"claim":"has a beagle named Gus","about":"self","category":"home"}]""", _ => release.Task),
                store, new FakePersonaRoster((LifeHarness.Sal, "Sal")), new FakeLlmBatchGate(), new FakeTimeProvider(),
                new CapturingLogger<LifeExtractionService>());
            var drain = service.RunAsync(ex => throw ex, CancellationToken.None);

            service.Enqueue(new AcceptedCopy(LifeHarness.Sal, "Sal", "Gus howled"));
            Assert.Empty(store.Rows);

            release.SetResult();
            service.Complete();
            await drain;
            Assert.Single(store.Rows);
        }
    }

    // AC2 (the real queue) — a full queue sheds the newest copy, loudly enough to find
    public sealed class ScenarioTheQueueIsFull
    {
        [Fact]
        public void ACopyPastTheCapacityIsLoggedAndNotThrown()
        {
            var log = new CapturingLogger<LifeExtractionService>();
            var service = new LifeExtractionService(
                new FakeLifeClaimLlm("[]"), new InMemoryLifeStore(), new FakePersonaRoster((LifeHarness.Sal, "Sal")),
                new FakeLlmBatchGate(), new FakeTimeProvider(), log);
            for (var n = 0; n < LifeExtractionService.QueueCapacity; n++)
                service.Enqueue(new AcceptedCopy(LifeHarness.Sal, "Sal", $"copy {n}"));
            Assert.Empty(log.Entries);

            service.Enqueue(new AcceptedCopy(LifeHarness.Sal, "Sal", "one too many"));

            Assert.Equal(1, service.DroppedCopies);
            Assert.Contains(log.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("dropped a copy from Sal", StringComparison.Ordinal));
        }

        [Fact]
        public void AStalledModelDoesNotFloodTheLog()
        {
            var log = new CapturingLogger<LifeExtractionService>();
            var service = new LifeExtractionService(
                new FakeLifeClaimLlm("[]"), new InMemoryLifeStore(), new FakePersonaRoster((LifeHarness.Sal, "Sal")),
                new FakeLlmBatchGate(), new FakeTimeProvider(), log);
            for (var n = 0; n < LifeExtractionService.QueueCapacity + 9; n++)
                service.Enqueue(new AcceptedCopy(LifeHarness.Sal, "Sal", $"copy {n}"));

            Assert.Equal(9, service.DroppedCopies);
            Assert.Single(log.Entries);
        }
    }

    // AC3 — No duplicates
    public sealed class ScenarioNoDuplicates
    {
        // Given an existing life row "has a beagle named Gus"
        // When  the same claim is extracted again

        [Fact]
        public async Task TheRowCountIsUnchanged()
        {
            var harness = new LifeHarness("""[{"claim":"has a beagle named Gus","about":"self","category":"home"}]""");
            harness.Store.Seed(LifeHarness.Sal, "has a beagle named Gus");

            await harness.RunAsync("Gus howled again");

            Assert.Single(harness.Store.Rows);
        }

        [Fact]
        public async Task ADifferentCaseOrPunctuationIsTheSameClaim()
        {
            var harness = new LifeHarness("""[{"claim":"Has a  beagle named Gus!","about":"self","category":"home"}]""");
            harness.Store.Seed(LifeHarness.Sal, "has a beagle named Gus");

            await harness.RunAsync("Gus howled again");

            Assert.Single(harness.Store.Rows);
        }

        [Fact]
        public async Task TheSameClaimForAnotherPersonaIsStored()
        {
            var harness = new LifeHarness("""[{"claim":"has a beagle named Gus","about":"self","category":"home"}]""");
            harness.Store.Seed(LifeHarness.Flip, "has a beagle named Gus");

            await harness.RunAsync("Gus howled again");

            Assert.Equal(2, harness.Store.Rows.Count);
        }
    }

    // AC4 — An outing is shared
    public sealed class ScenarioAnOutingIsShared
    {
        // Given persona Sal's copy yields {"claim":"grabbed tacos with Flip after the show","about":"Captain Flip","category":"people"}
        // When  the extractor stores it

        readonly LifeHarness harness = new("""[{"claim":"grabbed tacos with Flip after the show","about":"Captain Flip","category":"people"}]""");

        public ScenarioAnOutingIsShared() =>
            harness.RunAsync("Tacos with Flip last night, folks").GetAwaiter().GetResult();

        [Fact]
        public void OneMemoryEventOfKindOutingExists() => Assert.Equal(
            [(MemoryEventKinds.Outing, "Sal and Captain Flip: grabbed tacos with Flip after the show")],
            harness.Store.Happenings.Select(h => (h.Kind, h.Summary)));

        [Fact]
        public void SalAndFlipEachGainALifeRowPointingAtItPhrasedFromTheirOwnSide()
        {
            var eventId = Assert.Single(harness.Store.Happenings).Id;
            Assert.Equal(
                [(LifeHarness.Sal, "grabbed tacos with Flip after the show", (long?)eventId),
                 (LifeHarness.Flip, "grabbed tacos with Sal after the show", eventId)],
                harness.Store.Rows.Select(r => (r.PersonaId, r.Content, r.EventId)));
        }

        [Fact]
        public async Task AClaimThatNamesNoOneInItsTextGetsTheSpeakerAppendedForTheOtherSide()
        {
            var other = new LifeHarness("""[{"claim":"went bowling after the show","about":"Flip","category":"people"}]""");

            await other.RunAsync("Bowling last night");

            Assert.Equal(["went bowling after the show", "went bowling after the show, with Sal"], other.Store.Rows.Select(r => r.Content));
        }

        [Fact]
        public async Task ANameThatIsNotOnTheRosterIsAClaimAboutTheSpeakerAlone()
        {
            var other = new LifeHarness("""[{"claim":"visits her sister Karen on Sundays","about":"Karen","category":"people"}]""");

            await other.RunAsync("Sunday with Karen");

            Assert.Empty(other.Store.Happenings);
            Assert.Equal([(LifeHarness.Sal, "visits her sister Karen on Sundays")], other.Store.Rows.Select(r => (r.PersonaId, r.Content)));
        }
    }

    // AC5 — Forget
    public sealed class ScenarioForget
    {
        // Given a life row with forgotten_at set an hour ago
        // When  the prompt is built (asserted on the MemoryPacket; the prompt block lands in T701)

        readonly MemoryPacket packet;

        public ScenarioForget()
        {
            var store = new InMemoryPersonaMemory(new FakeTimeProvider());
            store.Add(MemoryKinds.Life, "has a beagle named Gus", source: "extracted", forgottenAgo: TimeSpan.FromHours(1));
            store.Add(MemoryKinds.Life, "plays the banjo", source: "extracted");
            packet = new MemoryRecallSource(store).TakeAsync(1, MemoryBudget.Standard, CancellationToken.None).GetAwaiter().GetResult();
        }

        [Fact]
        public void TheRowIsNotInWhatYouRemember() => Assert.DoesNotContain("has a beagle named Gus", packet.Life.Select(m => m.Content));

        [Fact]
        public void AnUnforgottenLifeRowIsStillRemembered() => Assert.Equal(["plays the banjo"], packet.Life.Select(m => m.Content));

        [Fact]
        public void ItsClaimIsInTheNeverMentionList() => Assert.Equal(["has a beagle named Gus"], packet.NeverMention);
    }

    // AC6 — Forgotten fades
    public sealed class ScenarioForgottenFades
    {
        // Given a life row forgotten 8 days ago
        // When  the prompt is built (asserted on the MemoryPacket; the prompt block lands in T701)

        readonly MemoryPacket packet;

        public ScenarioForgottenFades()
        {
            var store = new InMemoryPersonaMemory(new FakeTimeProvider());
            store.Add(MemoryKinds.Life, "has a beagle named Gus", source: "extracted", forgottenAgo: TimeSpan.FromDays(8));
            store.Add(MemoryKinds.Life, "grew up in Red Deer", source: "extracted", forgottenAgo: TimeSpan.FromDays(6));
            packet = new MemoryRecallSource(store).TakeAsync(1, MemoryBudget.Standard, CancellationToken.None).GetAwaiter().GetResult();
        }

        [Fact]
        public void ItsClaimIsInNeitherBlock() =>
            Assert.DoesNotContain("has a beagle named Gus", packet.Life.Select(m => m.Content).Concat(packet.NeverMention));

        [Fact]
        public void ARowForgottenSixDaysAgoIsStillInTheNeverMentionList() => Assert.Equal(["grew up in Red Deer"], packet.NeverMention);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC7 — Garbage is nothing
    public sealed class ScenarioGarbageIsNothing
    {
        // Given the fake LLM returns prose, not JSON
        // When  the extractor runs

        readonly LifeHarness harness = new("Sure! Gus the beagle sounds lovely.");

        public ScenarioGarbageIsNothing() =>
            harness.RunAsync("My beagle Gus howled through the whole intro").GetAwaiter().GetResult();

        [Fact]
        public void NoRowIsWritten() => Assert.Empty(harness.Store.Rows);

        [Fact]
        public void ADebugLogLineRecordsIt() => Assert.Contains(
            harness.Log.Entries, e => e.Level == LogLevel.Debug && e.Message.Contains("not a JSON array", StringComparison.Ordinal));

        [Theory]
        [InlineData("[]")]
        [InlineData("""{"claim":"has a beagle","about":"self"}""")]
        [InlineData("""[{"about":"self"},42,"x",{"claim":""}]""")]
        public async Task AnEmptyOrMisshapenReplyStoresNothing(string reply)
        {
            var other = new LifeHarness(reply);

            await other.RunAsync("anything");

            Assert.Empty(other.Store.Rows);
        }
    }

    // Review addition (T704 round 1, F85.3) — The gate says not now
    public sealed class ScenarioTheBatchGateDeclines
    {
        // Given the offline-batch gate declines
        // When  the extractor runs

        readonly LifeHarness harness = new("""[{"claim":"has a beagle named Gus","about":"self"}]""", gateAllows: false);

        public ScenarioTheBatchGateDeclines() =>
            harness.RunAsync("My beagle Gus howled through the whole intro").GetAwaiter().GetResult();

        [Fact]
        public void TheModelIsNotCalled() => Assert.Empty(harness.Llm.UserPrompts);

        [Fact]
        public void NoRowIsWritten() => Assert.Empty(harness.Store.Rows);

        [Fact]
        public void OneDebugLineRecordsTheSkip() =>
            Assert.Single(harness.Log.Entries, e => e.Level == LogLevel.Debug && e.Message.Contains("skipped", StringComparison.Ordinal));
    }

    // AC8 — The cap
    public sealed class ScenarioTheCap
    {
        // Given 100 life rows, the oldest never aired
        // When  a new claim is stored

        [Fact]
        public async Task TheOldestNeverAiredRowIsEvicted()
        {
            var harness = new LifeHarness("""[{"claim":"plays the banjo","about":"self","category":"habit"}]""");
            for (var n = 1; n <= LifeMemoryRules.Cap; n++)
                harness.Store.Seed(LifeHarness.Sal, $"claim {n}", aired: n == 1);

            await harness.RunAsync("Banjo night");

            var contents = harness.Store.Rows.Select(r => r.Content).ToList();
            Assert.Equal(LifeMemoryRules.Cap, contents.Count);
            Assert.DoesNotContain("claim 2", contents);
            Assert.Contains("claim 1", contents);
            Assert.Contains("plays the banjo", contents);
        }
    }
}
