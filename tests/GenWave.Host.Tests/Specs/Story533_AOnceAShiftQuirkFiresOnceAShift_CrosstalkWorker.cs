// STORY-533 — A once-a-shift quirk fires once a shift (gh-#293 gh-#920 · SPEC F218.7 · PLAN T688)
//
// Entry point: CrosstalkStockWorker.TickOnceAsync with the real QuirkSelector. Crosstalk is stocked
// ahead (it can air in another shift, or be discarded), so a maxPerShift quirk never reaches its
// prompt and no spend row is written.

namespace GenWave.Host.Tests.Specs;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;
using GenWave.Memory;

public static class FeatureAOnceAShiftQuirkFiresOnceAShiftCrosstalkWorker
{
    const string CappedQuirk = "Signs off with a bird fact";
    const string PlainQuirk = "Hums between sentences";

    public sealed class ScenarioATickStocksACardWithACappedQuirk
    {
        // Given a worker wired to the real QuirkSelector and cards with a capped rule plus a plain quirk
        // When  a tick generates an exchange
        // Then  the capped quirk is in no posted prompt
        // And   no spend row is recorded
        // And   the plain quirk is in the posted prompt

        readonly RecordingQuirkLedger ledger = new();
        readonly List<string> prompts = [];

        async Task TickAsync()
        {
            var card = new PersonaCard(
                1, "Host DJ", "", "Soul line.", [CappedQuirk, PlainQuirk], new VoiceSpec("kokoro", "af_heart", 1.0, "en"),
                EnergyDisposition: 0, [], [])
            {
                QuirkRules = [new QuirkRule(CappedQuirk, MaxPerShift: 1, PerBreakChance: 1.0)],
            };
            var now = new DateTimeOffset(2026, 1, 5, 12, 0, 0, TimeSpan.Zero); // a Monday noon
            var (worker, _, _, _, _, _) = await CrosstalkWorkerHarness.BuildAsync(
                now, "morning-drive", "Morning Drive", replyContent: "HOST: Hi there.", // discards: no render to block on
                quirkSelector: new QuirkSelector(ledger, new Random(1)), card: card, postedPrompts: prompts);
            await worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task TheCappedQuirkIsInNoPostedPrompt()
        {
            await TickAsync();
            Assert.DoesNotContain(prompts, p => p.Contains(CappedQuirk));
        }

        [Fact]
        public async Task NoSpendRowIsRecorded()
        {
            await TickAsync();
            Assert.Empty(ledger.Spends);
        }

        [Fact]
        public async Task ThePlainQuirkIsInThePostedPrompt()
        {
            await TickAsync();
            Assert.Contains(prompts, p => p.Contains(PlainQuirk));
        }
    }
}
