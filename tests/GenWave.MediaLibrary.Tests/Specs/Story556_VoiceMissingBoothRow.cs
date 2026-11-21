// STORY-556 AC8 (booth-log half) — a DJ with no voice for the active engine is a voice-missing row (SPEC F219.9, PLAN T753)
//
// No database: stops at the in-memory channel BoothLogWriter.Publish writes into.

using System.Threading.Channels;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.MediaLibrary.Station;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureVoiceMissingBoothRow
{
    sealed class NullPersonaAccessor : IActivePersonaAccessor
    {
        public Task<Persona?> ResolveAsync(CancellationToken ct) => Task.FromResult<Persona?>(null);
    }

    public sealed class ScenarioADjHasNoVoiceForTheEngine
    {
        readonly Task<BoothLogAppendRequest> row = PublishAsync();

        static async Task<BoothLogAppendRequest> PublishAsync()
        {
            var channel = Channel.CreateBounded<BoothLogAppendRequest>(1);
            new BoothLogWriter(channel.Writer, new NullPersonaAccessor(), NullLogger<BoothLogWriter>.Instance)
                .Publish(new VoiceMissing("Quiet Quentin", 7));
            return await channel.Reader.ReadAsync();
        }

        [Fact]
        public async Task TheRowKindIsVoiceMissing() => Assert.Equal("voice-missing", (await row).Kind);

        [Fact]
        public async Task TheRowNamesThePersona() => Assert.Equal(7L, (await row).PersonaId);

        [Fact]
        public async Task TheSummaryNamesTheDj() => Assert.Contains("Quiet Quentin", (await row).Summary);
    }
}
