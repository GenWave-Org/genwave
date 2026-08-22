// gh-#612 — engine: the push-honesty guard.
//
// BDD specification — xUnit. Liquidsoap's q.push replies with a success-shaped RID before resolving
// the URI, so a push of a nonexistent path "succeeds" and dies engine-side at an unsweepable
// severity-3 line. The api and the engine share the same media mounts, so MediaExistencePushGuard
// answers the engine's question locally: a fully-qualified locator with no file behind it is
// declined (null result, one WARN, inner control never contacted); everything else — existing
// files, and locator shapes File.Exists was never fit to judge — passes through untouched.

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Engine;
using Microsoft.Extensions.Logging.Abstractions;
using CoreLoudness = GenWave.Core.Domain.Loudness;

namespace GenWave.Host.Tests.Specs;

public static class FeatureMediaExistencePushGuard
{
    sealed class RecordingControl : ILiquidsoapControl
    {
        public List<MediaItem> Pushed { get; } = [];

        public Task<string?> OnAirNewestAsync(CancellationToken ct) => Task.FromResult<string?>(null);

        public Task<EngineMetadata> MetadataAsync(string rid, CancellationToken ct) =>
            Task.FromResult(new EngineMetadata(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));

        public Task<EnginePushResult?> PushAsync(MediaItem item, double gainDb, CancellationToken ct)
        {
            Pushed.Add(item);
            return Task.FromResult<EnginePushResult?>(new EnginePushResult("42", ArtworkUrl: null));
        }
    }

    static MediaItem Item(string locator) =>
        new("m1", locator, "title-m1", new CoreLoudness(-16.0, -1.0, Measurable: true));

    static MediaExistencePushGuard Guard(RecordingControl inner) =>
        new(inner, NullLogger<MediaExistencePushGuard>.Instance);

    public sealed class ScenarioMissingFile
    {
        [Fact]
        public async Task APushOfANonexistentPathIsDeclinedWithoutContactingTheEngine()
        {
            var inner = new RecordingControl();
            var missing = Path.Combine(Path.GetTempPath(), $"gh612-{Guid.NewGuid():N}.mp3");

            var result = await Guard(inner).PushAsync(Item(missing), 0.0, CancellationToken.None);

            Assert.Null(result);       // declined per the ILiquidsoapControl.PushAsync null contract
            Assert.Empty(inner.Pushed);   // the engine never saw a request it would have killed silently
        }
    }

    public sealed class ScenarioExistingFile
    {
        [Fact]
        public async Task APushOfAnExistingFilePassesThroughUntouched()
        {
            var inner = new RecordingControl();
            var present = Path.Combine(Path.GetTempPath(), $"gh612-{Guid.NewGuid():N}.mp3");
            await File.WriteAllBytesAsync(present, [0x00]);
            try
            {
                var result = await Guard(inner).PushAsync(Item(present), 0.0, CancellationToken.None);

                Assert.NotNull(result);
                Assert.Equal("42", result.Rid);
                var pushed = Assert.Single(inner.Pushed);
                Assert.Equal(present, pushed.Locator);
            }
            finally
            {
                File.Delete(present);
            }
        }
    }

    public sealed class ScenarioUnjudgeableLocatorShape
    {
        [Fact]
        public async Task ANonFullyQualifiedLocatorIsNotThisGuardsQuestionAndPassesThrough()
        {
            // A future URI-shaped or relative locator: File.Exists cannot honestly judge it, so the
            // guard must not decline it — the engine remains the authority for every shape but the
            // absolute container path this incident class is made of.
            var inner = new RecordingControl();

            var result = await Guard(inner).PushAsync(Item("relative/never-made.mp3"), 0.0, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Single(inner.Pushed);
        }
    }
}
