// STORY-534 AC6 (booth-log half) — a caught repeat is a repeat-caught row (SPEC F218.13, PLAN T693)
//
// No database: stops at the in-memory channel BoothLogWriter.Publish writes into, like
// Story329_CrosstalkBoothStamp.cs.

using System.Threading.Channels;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.MediaLibrary.Station;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureRepeatCaughtBoothRow
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

    public sealed class ScenarioACaughtRepeatIsLogged
    {
        [Fact]
        public async Task TheRowKindIsRepeatCaught()
        {
            var row = await PublishAndCaptureAsync(new RepeatCaught(7, "golden hour groove"));

            Assert.Equal("repeat-caught", row.Kind);
        }

        [Fact]
        public async Task TheRowNamesThePersona()
        {
            var row = await PublishAndCaptureAsync(new RepeatCaught(7, "golden hour groove"));

            Assert.Equal(7L, row.PersonaId);
        }

        [Fact]
        public async Task TheSummaryNamesThePhrase()
        {
            var row = await PublishAndCaptureAsync(new RepeatCaught(7, "golden hour groove"));

            Assert.Equal("Repeat caught: \"golden hour groove\"", row.Summary);
        }
    }

    // F218.38 (STORY-545, T714) — the row ends with what the writer did about the catch
    public sealed class ScenarioTheRowEndsWithTheOutcome
    {
        [Theory]
        [InlineData(RepeatOutcome.Trimmed, "Repeat caught: \"golden hour groove\" — trimmed")]
        [InlineData(RepeatOutcome.ReAsked, "Repeat caught: \"golden hour groove\" — re-asked")]
        [InlineData(RepeatOutcome.Template, "Repeat caught: \"golden hour groove\" — template")]
        [InlineData(RepeatOutcome.Dropped, "Repeat caught: \"golden hour groove\" — dropped")]
        public async Task TheSummaryEndsWithTheOutcome(RepeatOutcome outcome, string expected)
        {
            var row = await PublishAndCaptureAsync(new RepeatCaught(7, "golden hour groove", outcome));

            Assert.Equal(expected, row.Summary);
        }
    }
}
