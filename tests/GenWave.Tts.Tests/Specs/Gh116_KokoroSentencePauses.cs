// gh-#116 — Engine-aware sentence pauses on the Kokoro request path
//
// BDD specification — xUnit. The pinned kokoro-fastapi v0.6.0 inserts ZERO silence at punctuation
// (whole blurbs render as one breathless chunk) but honors exact [pause:Ns] markup as true digital
// silence; piper-tts 1.6.0 has NO pause mechanism — a tag reaching Piper is SPOKEN ALOUD. So the
// tag is appended per engine at Kokoro request build (KokoroTtsSynthesizer + KokoroFallbackRenderer),
// below the NormalizingTtsSynthesizer chokepoint, and Piper hops always receive clean text.
// TtsSegmentSource's final cache key is computed from pre-synthesis copy text one seam ABOVE the
// engine split, so tagging never re-keys or double-caches a logical segment (pinned below).

namespace GenWave.Tts.Tests.Specs;

using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureKokoroSentencePauses
{
    // ------------------------------------------------------------------
    // Shared fixture helpers
    // ------------------------------------------------------------------

    const string Tag = " [pause:0.6s]";
    const double DefaultPause = 0.6;

    static (HttpClient Http, List<string> Bodies) WireCapture(HttpStatusCode status = HttpStatusCode.OK)
    {
        var bodies = new List<string>();
        var handler = new FakeHttpMessageHandler(async (request, ct) =>
        {
            bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
            return new HttpResponseMessage(status)
            {
                Content = new ByteArrayContent([1, 2, 3, 4]),
            };
        });
        return (new HttpClient(handler), bodies);
    }

    static TestOptionsMonitor<TtsOptions> Options(string cacheRoot, double? pauseSeconds = null) =>
        new(new TtsOptions
        {
            CacheRoot = cacheRoot,
            Format = "wav",
            SentencePauseSeconds = pauseSeconds ?? DefaultPause,
        });

    static TtsFallbackProfile KokoroHop() =>
        new() { Engine = DependencyNames.Kokoro, Endpoint = "http://backup-kokoro:8880", Voice = "" };

    static TtsFallbackProfile PiperHop() =>
        new() { Engine = DependencyNames.Piper, Endpoint = "http://piper:5000", Voice = "en_US-lessac-medium" };

    static string InputOf(string requestBody) =>
        JsonDocument.Parse(requestBody).RootElement.GetProperty("input").GetString() ?? "";

    // ------------------------------------------------------------------
    // HAPPY PATH — the insertion heuristic itself
    // ------------------------------------------------------------------

    public static class ScenarioSentenceBoundaryInsertion
    {
        [Fact]
        public static void One_pause_follows_each_sentence_final_period_bang_and_question()
        {
            var tagged = KokoroPauseMarkup.InsertSentencePauses(
                "First one. Second one! Third one? The end.", DefaultPause);

            // Every internal sentence boundary gets exactly one tag; the final "." gets none
            // (pinned separately below).
            Assert.Equal(
                $"First one.{Tag} Second one!{Tag} Third one?{Tag} The end.",
                tagged);
        }

        [Fact]
        public static void An_ellipsis_run_gets_exactly_one_pause_never_three()
        {
            var tagged = KokoroPauseMarkup.InsertSentencePauses("Wait... here it comes", DefaultPause);

            Assert.Equal($"Wait...{Tag} here it comes", tagged);
        }

        [Fact]
        public static void A_unicode_ellipsis_followed_by_a_period_still_gets_one_pause()
        {
            var tagged = KokoroPauseMarkup.InsertSentencePauses("Hold on…. almost there", DefaultPause);

            Assert.Equal($"Hold on….{Tag} almost there", tagged);
        }

        [Fact]
        public static void Mixed_terminal_punctuation_collapses_to_one_pause()
        {
            var tagged = KokoroPauseMarkup.InsertSentencePauses("Really?! You bet", DefaultPause);

            Assert.Equal($"Really?!{Tag} You bet", tagged);
        }
    }

    public static class ScenarioFinalPositionDecision
    {
        [Fact]
        public static void The_texts_final_sentence_ender_never_gets_a_pause()
        {
            // DECISION PINNED (gh-#116): no trailing pause. A pause after the last sentence is
            // not an audible gap — nothing follows it — just 0.6s of dead tail that would inflate
            // the clip's measured cue-out/DurationMs (the cue analyzer measures the rendered
            // file), deaden the crossfade into the next item, and play as flat dead air on the
            // preview/safe-segment paths that have no cue trim at all.
            var tagged = KokoroPauseMarkup.InsertSentencePauses("The end.", DefaultPause);

            Assert.Equal("The end.", tagged);
        }

        [Fact]
        public static void A_final_ellipsis_run_gets_no_pause_either()
        {
            var tagged = KokoroPauseMarkup.InsertSentencePauses("And away we go...", DefaultPause);

            Assert.Equal("And away we go...", tagged);
        }
    }

    public static class ScenarioCopyShapeSafety
    {
        [Fact]
        public static void Decimals_and_stylized_names_pass_through_untouched()
        {
            // The Story184 corpus shapes: mid-word punctuation ("101.5", "P!nk", "Ke$ha") is
            // never a sentence boundary — the run must be followed by whitespace.
            var text = "Tune to 101.5 for P!nk and Ke$ha today";

            Assert.Equal(text, KokoroPauseMarkup.InsertSentencePauses(text, DefaultPause));
        }

        [Fact]
        public static void A_dotted_single_letter_abbreviation_never_pauses_mid_sentence()
        {
            var tagged = KokoroPauseMarkup.InsertSentencePauses(
                "Doors at 9 a.m. tonight. Bring water", DefaultPause);

            // "a.m." keeps flowing; the real sentence boundary after "tonight." still pauses.
            Assert.Equal($"Doors at 9 a.m. tonight.{Tag} Bring water", tagged);
        }

        [Fact]
        public static void Zero_seconds_disables_insertion_entirely()
        {
            var text = "First one. Second one! The end.";

            Assert.Equal(text, KokoroPauseMarkup.InsertSentencePauses(text, 0));
        }

        [Fact]
        public static void A_comma_decimal_host_locale_still_formats_the_tag_with_a_dot()
        {
            // The wire contract is exactly [pause:N.Ns] — "[pause:0,6s]" would be spoken garbage.
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                var tagged = KokoroPauseMarkup.InsertSentencePauses("One. Two", DefaultPause);

                Assert.Equal("One. [pause:0.6s] Two", tagged);
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }
    }

    // ------------------------------------------------------------------
    // HAPPY PATH — per-engine wire shapes (real clients, captured requests)
    // ------------------------------------------------------------------

    public static class ScenarioEngineWireShapes
    {
        const string Copy = "First one. Second one! The end.";
        const string TaggedCopy = $"First one.{Tag} Second one!{Tag} The end.";

        [Fact]
        public static async Task The_primary_kokoro_request_carries_pause_tags()
        {
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var (http, bodies) = WireCapture();
                var synth = new KokoroTtsSynthesizer(http, Options(cacheRoot));

                await synth.SynthesizeAsync(Copy, "af_heart", CancellationToken.None);

                Assert.Equal(TaggedCopy, InputOf(Assert.Single(bodies)));
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }
        }

        [Fact]
        public static async Task A_kokoro_kind_fallback_hop_carries_the_same_pause_tags()
        {
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var (http, bodies) = WireCapture();
                var renderer = new KokoroFallbackRenderer(http, Options(cacheRoot));

                await renderer.RenderAsync(
                    KokoroHop(), new TtsRenderContext(Copy, "af_heart", Kind: null), CancellationToken.None);

                Assert.Equal(TaggedCopy, InputOf(Assert.Single(bodies)));
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }
        }

        [Fact]
        public static async Task A_kokoro_kind_fallback_hop_carries_the_same_pronunciation_rules()
        {
            // T137 review finding (P3): T134 made KokoroTtsSynthesizer read Rules from the context
            // while KokoroFallbackRenderer structurally could not — IFallbackProfileRenderer carried
            // no TtsRenderContext at all — so a kokoro-kind fallback hop rendered a DJ's own
            // catchphrase mispronounced. Proven at the T134 review: primary
            // "Say [MacLeod](m@'klaUd) now." vs the pre-fix fallback "Say MacLeod now." — the pinned
            // A_kokoro_kind_fallback_hop_carries_the_same_pause_tags fact above never caught it
            // (pauses still matched). T137 widened the interface to carry the full context down to
            // every hop; this pins RULES parity specifically.
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var (http, bodies) = WireCapture();
                var renderer = new KokoroFallbackRenderer(http, Options(cacheRoot));
                var context = new TtsRenderContext("Say MacLeod now.", "af_heart", Kind: null)
                    with { Rules = [new PronunciationRule("MacLeod", "MacLeod", "/m@'klaUd/")] };

                await renderer.RenderAsync(KokoroHop(), context, CancellationToken.None);

                Assert.Equal("Say [MacLeod](/m@'klaUd/) now.", InputOf(Assert.Single(bodies)));
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }
        }

        [Fact]
        public static async Task A_piper_hop_receives_the_clean_text_verbatim_never_a_tag()
        {
            // piper-tts has no pause mechanism — a tag reaching Piper is SPOKEN ALOUD (gh-#116).
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var (http, bodies) = WireCapture();
                var renderer = new PiperTtsSynthesizer(http, Options(cacheRoot));

                await renderer.RenderAsync(
                    PiperHop(), new TtsRenderContext(Copy, "af_heart", Kind: null), CancellationToken.None);

                var body = Assert.Single(bodies);
                Assert.Equal(Copy, body);
                Assert.DoesNotContain("[pause:", body);
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }
        }

        [Fact]
        public static async Task Setting_the_pause_to_zero_sends_the_clean_text_to_kokoro()
        {
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var (http, bodies) = WireCapture();
                var synth = new KokoroTtsSynthesizer(http, Options(cacheRoot, pauseSeconds: 0));

                await synth.SynthesizeAsync(Copy, "af_heart", CancellationToken.None);

                Assert.Equal(Copy, InputOf(Assert.Single(bodies)));
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }
        }

        [Fact]
        public static async Task A_kokoro_failure_rescued_by_the_piper_hop_flips_tagged_to_clean()
        {
            // End to end through the gh-#147 chain with the REAL engine clients: the failed
            // Kokoro attempt carried tags on the wire, the Piper rescue of the SAME render did
            // not — the per-engine split, exercised in one render.
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var (kokoroHttp, kokoroBodies) = WireCapture(HttpStatusCode.InternalServerError);
                var (piperHttp, piperBodies) = WireCapture();
                var router = new FallbackTtsSynthesizer(
                    new KokoroTtsSynthesizer(kokoroHttp, Options(cacheRoot)),
                    [new PiperTtsSynthesizer(piperHttp, Options(cacheRoot))],
                    new FakeDependencyHealth(),
                    new TestOptionsMonitor<TtsFallbackOptions>(
                        new TtsFallbackOptions { Endpoint = "http://piper:5000", Voice = "en_US-lessac-medium" }),
                    new CapturingLogger<FallbackTtsSynthesizer>());

                var path = await router.SynthesizeAsync(Copy, "af_heart", CancellationToken.None);

                Assert.NotNull(path);
                Assert.Equal(TaggedCopy, InputOf(Assert.Single(kokoroBodies)));
                Assert.Equal(Copy, Assert.Single(piperBodies));
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    // ------------------------------------------------------------------
    // HAPPY PATH — cache-key safety
    // ------------------------------------------------------------------

    public static class ScenarioCacheKeySafety
    {
        [Fact]
        public static async Task The_same_logical_segment_occupies_one_cache_slot_regardless_of_engine()
        {
            // TtsSegmentSource keys its final cache on the PRE-synthesis copy text (plus voice/
            // station/corrections fingerprints) — one seam above the engine split — so per-engine
            // pause tagging can never re-key or double-cache a segment. Pinned by rendering the
            // same segment twice: first via the primary, then with Kokoro marked unhealthy so the
            // chain WOULD route to Piper — the second render must hit the first render's slot
            // without any engine being asked to synthesize at all.
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var primary = new FakeTtsSynthesizer();
                var fallback = new FakeProfileRenderer(DependencyNames.Piper);
                var health = new FakeDependencyHealth();
                var router = new FallbackTtsSynthesizer(
                    primary, [fallback], health,
                    new TestOptionsMonitor<TtsFallbackOptions>(
                        new TtsFallbackOptions { Endpoint = "http://piper:5000", Voice = "en_US-lessac-medium" }),
                    new CapturingLogger<FallbackTtsSynthesizer>());
                var source = new TtsSegmentSource(
                    new TemplateCopyWriter(new PatterTemplateRenderer()),
                    router,
                    new FakeLoudnessAnalyzer(),
                    new FakeCueAnalyzer(),
                    NoCorrections.Provider(),
                    NoCorrections.PersonaCache(),
                    NoCorrections.PronunciationProvider(),
                    NoCorrections.PersonaPronunciationCache(),
                    Options(cacheRoot),
                    NullLogger<TtsSegmentSource>.Instance);
                var request = new SegmentRequest(
                    SegmentKind.StationId, "af_heart", "GenWave", null,
                    new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero), "test-station");

                var first = await source.RenderAsync(request, CancellationToken.None);
                health.Set(new DependencyHealthVerdict(
                    DependencyNames.Kokoro, Healthy: false, DateTimeOffset.UtcNow,
                    "connect failure", ConsecutiveFailureCount: 3));
                var second = await source.RenderAsync(request, CancellationToken.None);

                Assert.NotNull(first);
                Assert.NotNull(second);
                Assert.Equal(first!.MediaId, second!.MediaId);
                Assert.Equal(first.Locator, second.Locator);
                Assert.Equal(1, primary.CallCount);   // rendered once, by the primary
                Assert.Equal(0, fallback.CallCount);  // the piper route never synthesized — cache hit
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }
}
