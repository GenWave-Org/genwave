// STORY-570 AC8 (booth-log half) — a flag that changes the outcome is a rule-flagged row (SPEC F220.16, PLAN T800, T801)
//
// No database: stops at the in-memory channel BoothLogWriter.Publish writes into, like Story534_RepeatCaughtBoothRow.cs.

using System.Threading.Channels;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.MediaLibrary.Station;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureRuleFlaggedBoothRow
{
    sealed class NullPersonaAccessor : IActivePersonaAccessor
    {
        public Task<Persona?> ResolveAsync(CancellationToken ct) => Task.FromResult<Persona?>(null);
    }

    static async Task<BoothLogAppendRequest> PublishAndCaptureAsync(StationEvent evt)
    {
        var channel = Channel.CreateBounded<BoothLogAppendRequest>(1);
        new BoothLogWriter(channel.Writer, new NullPersonaAccessor(), NullLogger<BoothLogWriter>.Instance).Publish(evt);
        return await channel.Reader.ReadAsync();
    }

    public sealed class ScenarioAFlagReAsksTheLine : IAsyncLifetime
    {
        BoothLogAppendRequest row = null!;

        public async Task InitializeAsync() =>
            row = await PublishAndCaptureAsync(new RuleFlagged(7, "No repeated phrases", "DJ Nova", RuleFlagOutcome.ReAsked));

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheRowKindIsRuleFlagged() => Assert.Equal("rule-flagged", row.Kind);

        [Fact]
        public void TheRowNamesThePersona() => Assert.Equal(7L, row.PersonaId);

        [Fact]
        public void TheSummaryNamesTheRuleTheDjAndTheOutcome() =>
            Assert.Equal("No repeated phrases flagged DJ Nova's line — re-asked", row.Summary);
    }

    public sealed class ScenarioTheRowEndsWithTheOutcome
    {
        [Theory]
        [InlineData(RuleFlagOutcome.ReAsked, "No repeated phrases flagged DJ Nova's line — re-asked")]
        [InlineData(RuleFlagOutcome.Template, "No repeated phrases flagged DJ Nova's line — template")]
        [InlineData(RuleFlagOutcome.Dropped, "No repeated phrases flagged DJ Nova's line — dropped")]
        public async Task TheSummaryEndsWithTheOutcome(RuleFlagOutcome outcome, string expected)
        {
            var row = await PublishAndCaptureAsync(new RuleFlagged(7, "No repeated phrases", "DJ Nova", outcome));

            Assert.Equal(expected, row.Summary);
        }
    }

    public sealed class ScenarioAFlagReAsksTheChat : IAsyncLifetime
    {
        BoothLogAppendRequest row = null!;

        public async Task InitializeAsync() =>
            row = await PublishAndCaptureAsync(
                new RuleFlagged(7, "No repeated phrases", "DJ Nova", RuleFlagOutcome.ReAsked) { Talk = RuleFlagTalk.Chat });

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSummaryNamesTheRuleTheHostAndTheChat() =>
            Assert.Equal("No repeated phrases flagged DJ Nova's chat — re-asked", row.Summary);

        [Fact]
        public void TheRowKindIsRuleFlagged() => Assert.Equal("rule-flagged", row.Kind);
    }

    public sealed class ScenarioADroppedChat : IAsyncLifetime
    {
        BoothLogAppendRequest row = null!;

        public async Task InitializeAsync() =>
            row = await PublishAndCaptureAsync(
                new RuleFlagged(7, "No repeated phrases", "DJ Nova", RuleFlagOutcome.Dropped) { Talk = RuleFlagTalk.Chat });

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSummaryEndsWithDropped() =>
            Assert.Equal("No repeated phrases flagged DJ Nova's chat — dropped", row.Summary);
    }

    public sealed class ScenarioAFailedAd : IAsyncLifetime
    {
        BoothLogAppendRequest row = null!;

        public async Task InitializeAsync() =>
            row = await PublishAndCaptureAsync(
                new RuleFlagged(null, "The right phone number", "Cravin's Diner", RuleFlagOutcome.Failed) { Talk = RuleFlagTalk.Ad });

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSummaryNamesTheRuleTheSponsorAndTheAd() =>
            Assert.Equal("The right phone number flagged Cravin's Diner's ad — failed", row.Summary);

        [Fact]
        public void TheRowIsKeyedOnNoPersona() => Assert.Null(row.PersonaId);
    }

    // T804a (F220.9) — a shaper or recorder that threw is a rule-broken row with no outcome suffix
    public sealed class ScenarioABrokenRule : IAsyncLifetime
    {
        BoothLogAppendRequest shapeRow = null!;

        public async Task InitializeAsync() =>
            shapeRow = await PublishAndCaptureAsync(new RuleBroken(7, "Fresh taste notes", "DJ Nova", RuleBrokenStep.Shape));

        public Task DisposeAsync() => Task.CompletedTask;

        [Theory]
        [InlineData(RuleBrokenStep.Shape, RuleFlagTalk.Line, "Fresh taste notes could not shape DJ Nova's line")]
        [InlineData(RuleBrokenStep.Record, RuleFlagTalk.Chat, "Fresh taste notes could not record DJ Nova's chat")]
        public async Task TheSummaryNamesTheRuleTheStepTheDjAndTheTalk(RuleBrokenStep step, RuleFlagTalk talk, string expected)
        {
            var row = await PublishAndCaptureAsync(new RuleBroken(7, "Fresh taste notes", "DJ Nova", step) { Talk = talk });

            Assert.Equal(expected, row.Summary);
        }

        [Fact]
        public void TheRowKindIsRuleBroken() => Assert.Equal("rule-broken", shapeRow.Kind);

        [Fact]
        public void TheRowNamesThePersona() => Assert.Equal(7L, shapeRow.PersonaId);
    }

    public sealed class ScenarioABrokenAdRule : IAsyncLifetime
    {
        BoothLogAppendRequest row = null!;

        public async Task InitializeAsync() =>
            row = await PublishAndCaptureAsync(
                new RuleBroken(null, "Fresh taste notes", "Cravin's Diner", RuleBrokenStep.Record) { Talk = RuleFlagTalk.Ad });

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSummaryNamesTheSponsorAndTheAd() =>
            Assert.Equal("Fresh taste notes could not record Cravin's Diner's ad", row.Summary);

        [Fact]
        public void TheRowIsKeyedOnNoPersona() => Assert.Null(row.PersonaId);
    }
}
