// STORY-533 — A once-a-shift quirk fires once a shift (gh-#293 gh-#920 · SPEC F218.7 · PLAN T688)
//
// Crosstalk is stocked ahead and can air in another shift (or be discarded), so it never carries a
// maxPerShift quirk and never writes a spend row; plain quirks still reach its prompt (F71.3).

namespace GenWave.Tts.Tests.Specs;

using GenWave.Core.Domain;
using GenWave.Memory;
using GenWave.Tts.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using static GenWave.Tts.Tests.Fakes.QuirkSpecHarness;

public static class FeatureAOnceAShiftQuirkFiresOnceAShiftCrosstalk
{
    const string CappedQuirk = "Signs off with a bird fact";
    const string PlainQuirk = "Hums between sentences";

    static readonly QuirkRule CappedRule = new(CappedQuirk, MaxPerShift: 1, PerBreakChance: 1.0);

    static PersonaCard CardWith(bool capped) =>
        capped
            ? Card([CappedQuirk, PlainQuirk], [CappedRule])
            : Card(PlainQuirk);

    static List<string> WriteScripts(int count, PersonaCard host, PersonaCard neighbor, RecordingQuirkLedger ledger)
    {
        var prompts = new List<string>();
        var http = CapturingHttp(prompts, "HOST: Welcome back.\nNEIGHBOR: Glad to be here.");
        var writer = new CrosstalkScriptWriter(
            http,
            new TestOptionsMonitor<LlmOptions>(new LlmOptions { Endpoint = "http://fake-llm.local", Model = "m", TimeoutSeconds = 5, MaxCopyChars = 450 }),
            new TestOptionsMonitor<CrosstalkOptions>(new CrosstalkOptions()),
            new LlmCallRecorder(
                new LlmCallRing(new TestOptionsMonitor<LlmOptions>(new LlmOptions())),
                new LlmCallCauseCounters(TimeProvider.System)),
            new FakeDegradationModeReader(),
            NullLogger<CrosstalkScriptWriter>.Instance,
            TimeProvider.System,
            new QuirkSelector(ledger, new Random(1)));

        var request = new CrosstalkExchangeRequest(
            host, neighbor, "GenWave", ShowName: "Night Shift", Daypart: null, StationLocalNow: DateTimeOffset.UtcNow);
        for (var i = 0; i < count; i++)
            writer.WriteExchangeAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        return prompts;
    }

    public sealed class ScenarioAHostCardWithACappedQuirk
    {
        // Given a host card with a capped rule (maxPerShift 1, perBreakChance 1.0) and a plain quirk
        // When  ten crosstalk scripts are written
        // Then  the capped quirk is in no posted prompt
        // And   no spend row is recorded
        // And   the plain quirk is still in the prompts

        readonly RecordingQuirkLedger ledger = new();
        readonly List<string> prompts;

        public ScenarioAHostCardWithACappedQuirk() =>
            prompts = WriteScripts(10, CardWith(capped: true), CardWith(capped: false), ledger);

        [Fact]
        public void TheCappedQuirkIsInNoPrompt() => Assert.DoesNotContain(prompts, p => p.Contains(CappedQuirk));

        [Fact]
        public void NoSpendRowIsRecorded() => Assert.Empty(ledger.Spends);

        [Fact]
        public void ThePlainQuirkIsStillInThePrompts() => Assert.Contains(prompts, p => p.Contains(PlainQuirk));
    }

    public sealed class ScenarioBNeighborCardWithACappedQuirk
    {
        // Given a neighbour card with a capped rule and a plain quirk
        // When  ten crosstalk scripts are written
        // Then  the capped quirk is in no posted prompt
        // And   no spend row is recorded

        readonly RecordingQuirkLedger ledger = new();
        readonly List<string> prompts;

        public ScenarioBNeighborCardWithACappedQuirk() =>
            prompts = WriteScripts(10, CardWith(capped: false), CardWith(capped: true), ledger);

        [Fact]
        public void TheCappedQuirkIsInNoPrompt() => Assert.DoesNotContain(prompts, p => p.Contains(CappedQuirk));

        [Fact]
        public void NoSpendRowIsRecorded() => Assert.Empty(ledger.Spends);
    }
}
