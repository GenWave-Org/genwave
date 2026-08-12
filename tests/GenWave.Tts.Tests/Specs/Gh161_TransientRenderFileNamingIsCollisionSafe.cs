// gh-#161 — T138 live wire smoke, Finding 2: transient render-file naming must never collide
//
// Root cause (live-observed on the dev stack, WARN "TTS render failed for LeadIn/af_nova",
// System.IO.FileNotFoundException at File.Move): KokoroTtsSynthesizer.RenderAsync wrote its
// engine response to a path CONTENT-ADDRESSED on (speech, voice) — a deterministic hash, not a
// per-call-unique name. That file is ALWAYS transient (TtsSegmentSource.RenderAsync moves it into
// its own final cache slot via File.Move; TtsPreviewController deletes it outright after
// streaming), so the hash bought no caching benefit it could ever cash in. It did buy a bug: two
// concurrent renders of IDENTICAL (speech, voice) — reachable any time an evergreen template
// phrase or a degraded-LLM canned reply repeats verbatim across two segments the Orchestrator
// kicks off back-to-back with nothing awaited in between (SPEC F44.2) — wrote to, and then raced
// to File.Move away, the exact same on-disk path. Whichever render's File.Move won the race
// deleted the file out from under the other, whose own File.Move then threw
// FileNotFoundException on a path that had existed a moment earlier. KokoroFallbackRenderer and
// PiperTtsSynthesizer shared the identical content-addressed-transient-file pattern (same
// reasoning, same latent bug, never yet observed in the wild) and are fixed and pinned here too.
//
// The fix: every one of the three engine adapters now names its transient write with a fresh,
// per-call Guid — never a function of what is being rendered — so two renders of identical
// content can no longer share a filename to race on, structurally.

namespace GenWave.Tts.Tests.Specs;

using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureTransientRenderFileNamingIsCollisionSafe
{
    static FakeHttpMessageHandler OkHandler(byte[] responseBytes) =>
        new((request, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(responseBytes),
        }));

    public static class ScenarioKokoroPrimary
    {
        [Fact]
        public static async Task Two_concurrent_renders_of_identical_text_and_voice_never_share_a_transient_path()
        {
            // The exact live-observed shape: two segments (e.g. LeadIn and BackAnnounce) whose
            // copy happens to be byte-identical — a fixed/degraded-LLM reply is not disambiguated
            // by segment kind — rendered concurrently, in the SAME voice.
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var opts = new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" });
                var synth = new KokoroTtsSynthesizer(new HttpClient(OkHandler([1, 2, 3, 4])), opts);
                const string Text = "That was MacLeod spinning something special for us.";

                var first = synth.SynthesizeAsync(Text, "af_nova", CancellationToken.None);
                var second = synth.SynthesizeAsync(Text, "af_nova", CancellationToken.None);
                var paths = await Task.WhenAll(first, second);

                Assert.NotEqual(paths[0], paths[1]);
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }
        }

        [Fact]
        public static async Task Moving_both_renders_own_temp_file_to_the_same_final_slot_never_throws()
        {
            // The regression this whole file exists to pin: reproduces TtsSegmentSource's own
            // File.Move(synthPath, finalPath, overwrite: true) call for BOTH renders of an
            // identical (text, voice) pair landing on the identical final cache key — the exact
            // shape a real duplicate-copy render produces. Before the fix this failed with
            // FileNotFoundException the moment the second Move ran against a synthPath the first
            // Move had already claimed (same shared, content-addressed temp path). Each Move can
            // only succeed if its own source file genuinely exists first, so this single spec also
            // subsumes "both renders left a file on disk for their own path" (review finding 5 —
            // that fact asserted nothing a revert to content-addressed naming would ever fail: both
            // renders shared one path either way, so the file it wrote to always existed).
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var opts = new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" });
                var synth = new KokoroTtsSynthesizer(new HttpClient(OkHandler([1, 2, 3, 4])), opts);
                const string Text = "That was MacLeod spinning something special for us.";
                var finalPath = Path.Combine(cacheRoot, "final.wav");

                var first = await synth.SynthesizeAsync(Text, "af_nova", CancellationToken.None);
                var second = await synth.SynthesizeAsync(Text, "af_nova", CancellationToken.None);

                var exception = Record.Exception(() =>
                {
                    File.Move(first, finalPath, overwrite: true);
                    File.Move(second, finalPath, overwrite: true);
                });

                Assert.Null(exception);
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    public static class ScenarioKokoroFallbackHop
    {
        [Fact]
        public static async Task Two_concurrent_hop_renders_of_identical_text_and_voice_never_share_a_transient_path()
        {
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var opts = new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" });
                var renderer = new KokoroFallbackRenderer(new HttpClient(OkHandler([1, 2, 3, 4])), opts);
                var profile = new TtsFallbackProfile { Engine = DependencyNames.Kokoro, Endpoint = "http://backup-kokoro:8880", Voice = "" };
                var context = new TtsRenderContext("Say MacLeod now.", "af_heart", Kind: null);

                var first = renderer.RenderAsync(profile, context, CancellationToken.None);
                var second = renderer.RenderAsync(profile, context, CancellationToken.None);
                var paths = await Task.WhenAll(first, second);

                Assert.NotEqual(paths[0], paths[1]);
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    public static class ScenarioPiperHop
    {
        [Fact]
        public static async Task Two_concurrent_piper_renders_of_identical_text_never_share_a_transient_path()
        {
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var opts = new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" });
                var renderer = new PiperTtsSynthesizer(new HttpClient(OkHandler([1, 2, 3, 4])), opts);
                var profile = new TtsFallbackProfile { Engine = DependencyNames.Piper, Endpoint = "http://piper:5000", Voice = "" };
                var context = new TtsRenderContext("Say MacLeod now.", "af_heart", Kind: null);

                var first = renderer.RenderAsync(profile, context, CancellationToken.None);
                var second = renderer.RenderAsync(profile, context, CancellationToken.None);
                var paths = await Task.WhenAll(first, second);

                Assert.NotEqual(paths[0], paths[1]);
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    // Review finding 6: a failed File.Move must never leave the engine's transient write behind
    // as a permanent orphan under CacheRoot's top level — nothing ever sweeps it there (only
    // blurbsDir entries are, on a retention timer; see TtsSegmentSource.SweepBlurbs's own
    // remarks). Mirrors SafeSegmentAuthor's own all-or-nothing cleanup discipline.
    public static class ScenarioOrphanedTransientsAreCleanedUp
    {
        static SegmentRequest StationIdRequest() =>
            new(SegmentKind.StationId, "af_heart", "GenWave", null, DateTimeOffset.UtcNow, "test-station");

        [Fact]
        public static async Task A_failed_move_leaves_no_orphaned_transient_file()
        {
            // This scenario's own point (review finding D): the transient the failed Move would
            // otherwise have left behind is cleaned up, not orphaned. That a failed Move also makes
            // the render itself return null is the ordinary F92.4 exception-to-null degradation
            // TtsSegmentSource applies to ANY render-time failure — already pinned generically
            // elsewhere (Story005_TtsSegmentSource.cs) — so it is not re-asserted here.
            var (_, lastSynthPath) = await RunAFailedMoveAsync();

            Assert.False(File.Exists(lastSynthPath));
        }

        [Fact]
        public static async Task A_failed_move_still_degrades_the_render_to_null_not_a_crash()
        {
            var (result, _) = await RunAFailedMoveAsync();

            Assert.Null(result);
        }

        static async Task<(MediaItem? Result, string? LastSynthPath)> RunAFailedMoveAsync()
        {
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var synth = new FakeTtsSynthesizer();
                var opts = new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" });
                var source = new TtsSegmentSource(
                    new TemplateCopyWriter(new PatterTemplateRenderer()), synth, new FakeLoudnessAnalyzer(),
                    new FakeCueAnalyzer(), NoCorrections.Provider(), NoCorrections.PersonaCache(),
                    NoCorrections.PronunciationProvider(), NoCorrections.PersonaPronunciationCache(), opts,
                    NullLogger<TtsSegmentSource>.Instance);
                var request = StationIdRequest();

                // A first render discovers the deterministic destination path for this exact
                // (copy, voice, station) combination, then that destination is occupied by a
                // DIRECTORY rather than a file — File.Exists reports false for it (so the second
                // render still takes the synth-and-move branch, not the cache-hit branch), and
                // File.Move onto an existing directory fails.
                var first = await source.RenderAsync(request, CancellationToken.None);
                Assert.NotNull(first);
                var finalPath = first.Locator;
                File.Delete(finalPath);
                Directory.CreateDirectory(finalPath);

                var second = await source.RenderAsync(request, CancellationToken.None);

                return (second, synth.LastReturnedPath);
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }
}
