// STORY-560 AC2 (booth-log half) — a late talk is a talk-dropped-late row (SPEC F219.26, PLAN T767)
//
// No database: stops at the in-memory channel BoothLogWriter.Publish writes into.

using System.Threading.Channels;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.MediaLibrary.Station;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureTalkDroppedLateBoothRow
{
    sealed class NullPersonaAccessor : IActivePersonaAccessor
    {
        public Task<Persona?> ResolveAsync(CancellationToken ct) => Task.FromResult<Persona?>(null);
    }

    public sealed class ScenarioATalkRanPastTheBudget
    {
        readonly Task<BoothLogAppendRequest> row = PublishAsync();

        static async Task<BoothLogAppendRequest> PublishAsync()
        {
            var channel = Channel.CreateBounded<BoothLogAppendRequest>(1);
            new BoothLogWriter(channel.Writer, new NullPersonaAccessor(), NullLogger<BoothLogWriter>.Instance)
                .Publish(new TalkDroppedLate("LeadIn", "Flip"));
            return await channel.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        }

        [Fact]
        public async Task TheRowKindIsTalkDroppedLate() => Assert.Equal("talk-dropped-late", (await row).Kind);

        [Fact]
        public async Task TheSummaryNamesTheDj() => Assert.Contains("Flip", (await row).Summary);
    }
}
