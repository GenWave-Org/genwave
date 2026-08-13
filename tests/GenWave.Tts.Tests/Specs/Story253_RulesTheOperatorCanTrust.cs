// STORY-253 — Rules the operator can trust (gh-#284)
//
// SPEC F97.3–F97.6. Three things this pins, each of which has bitten before:
//
//   1. PRECEDENCE FLIPS. Shipped F71.7 has station corrections winning over card corrections
//      on an identical `from`. F97.4 reverses it — the persona wins — and the reversal covers
//      the WHOLE correction family, literal corrections included, because two precedence rules
//      over one merged surface is not a thing operators remember correctly six months on.
//      An operator who needs to override a bad imported rule edits the card, which import
//      already made a local copy of (F90).
//
//   2. OBSERVABILITY MOVES TO INFORMATION. F68.7 specified that a firing correction "logs at
//      debug". Debug does not reach the fleet log store at all, so "is my rule working?" has
//      been unanswerable in the field since it shipped. F97.5 amends it.
//
//   3. RULES RIDE WITH THE REQUEST. A segment can render across a segment boundary, after the
//      ambient on-air persona has already flipped to the incoming DJ — the exact failure
//      F92.2's HandoffContext exists to prevent. Resolved rules therefore travel on the
//      render context, never re-read from an accessor inside the adapter.

namespace GenWave.Tts.Tests.Specs;

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Tts.Tests.Fakes;
// GenWave.Tts.PronunciationRule (this file's ambient, unqualified `PronunciationRule` throughout)
// and GenWave.Core.Domain.PronunciationRule are two distinct mirrored types (see Story252's own
// remarks on the same collision) — a blanket `using GenWave.Core.Domain;` would silently rebind
// every existing unqualified `PronunciationRule` reference above to the wrong one. Aliasing only
// the names this scenario needs keeps the rest of the file's resolution untouched.
using TtsRenderContext = GenWave.Core.Domain.TtsRenderContext;
using SegmentRequest = GenWave.Core.Domain.SegmentRequest;
using SegmentKind = GenWave.Core.Domain.SegmentKind;
using Persona = GenWave.Core.Domain.Persona;
using PersonaCard = GenWave.Core.Domain.PersonaCard;
using VoiceSpec = GenWave.Core.Domain.VoiceSpec;
using ContextPronunciationRule = GenWave.Core.Domain.PronunciationRule;

public static class FeatureRulesTheOperatorCanTrust
{
    public static class ScenarioTwoSourcesMerge
    {
        [Fact]
        public static void A_station_only_rule_survives_the_merge()
        {
            // Given a station-only rule and an empty card
            var station = PronunciationRuleSet.Create([new PronunciationRule("Reykjavík", "Reykjavík", "/ˈreɪkjaviːk/")]);

            // When the two sources merge
            var merged = PronunciationRuleSet.Merge(station, PronunciationRuleSet.Empty);

            // Then the station rule still fires
            Assert.Single(merged.Match("Live from Reykjavík tonight."));
        }

        [Fact]
        public static void A_card_only_rule_survives_the_merge()
        {
            // Given a card-only rule and an empty station set
            var card = PronunciationRuleSet.Create([new PronunciationRule("MacLeod", "MacLeod", "/məˈklaʊd/")]);

            // When the two sources merge
            var merged = PronunciationRuleSet.Merge(PronunciationRuleSet.Empty, card);

            // Then the card rule still fires
            Assert.Single(merged.Match("Say MacLeod now."));
        }

        [Fact]
        public static void Operator_input_is_escaped_before_it_becomes_a_matcher()
        {
            // F68.5 posture carried over (F97.3): a pattern containing a regex metacharacter — "."
            // would match ANY character if compiled unescaped — matches only its literal text.
            var rule = new PronunciationRule("9.5", "9.5", "/naɪn.../");
            var merged = PronunciationRuleSet.Merge(
                PronunciationRuleSet.Create([rule]), PronunciationRuleSet.Empty);

            // When matching text where an unescaped "." would ALSO match "9x5"
            var matches = merged.Match("The score was 9x5 not 9.5.");

            // Then only the literal "9.5" occurrence matches
            Assert.Single(matches);
        }

        [Fact]
        public static void Matching_is_bounded_by_a_timeout()
        {
            // Given a pathological backtracking pattern (Create always Regex.Escapes operator/card
            // text, which defangs catastrophic backtracking by construction — FromRawPattern is a
            // test-only seam that exercises the timeout-and-skip mechanism directly, mirroring
            // SpeechCorrectionSet.FromRawPattern)
            var merged = PronunciationRuleSet.Merge(
                PronunciationRuleSet.FromRawPattern(@"(a+)+$", "/x/"), PronunciationRuleSet.Empty);
            var input = new string('a', 35) + "!";

            // When matching runs against adversarial input
            var exception = Record.Exception(() => merged.Match(input));

            // Then matching aborts at the timeout without an unhandled exception
            Assert.Null(exception);
        }
    }

    public static class ScenarioThePersonaWins
    {
        [Fact]
        public static void The_card_rule_applies_on_an_identical_pattern_and_word()
        {
            // Given a station rule and a card rule for the same (pattern, word)
            var station = PronunciationRuleSet.Create([new PronunciationRule("MacLeod", "MacLeod", "/stationIpa/")]);
            var card = PronunciationRuleSet.Create([new PronunciationRule("MACLEOD", "MACLEOD", "/cardIpa/")]);

            // When the two sources merge
            var merged = PronunciationRuleSet.Merge(station, card);

            // Then the card's phoneme is the one that fires — canonical (slash-free) form: Create
            // canonicalizes Ipa before compiling (T138 review), and the operator-authored
            // "/cardIpa/" above canonicalizes to "cardIpa".
            Assert.Equal("cardIpa", Assert.Single(merged.Match("Say MacLeod now.")).Rule.Ipa);
        }

        [Fact]
        public static void The_shadowed_station_rule_is_not_also_applied()
        {
            // Given a station rule and a card rule sharing the identical (pattern, word)
            var station = PronunciationRuleSet.Create([new PronunciationRule("MacLeod", "MacLeod", "/stationIpa/")]);
            var card = PronunciationRuleSet.Create([new PronunciationRule("MacLeod", "MacLeod", "/cardIpa/")]);

            // When the two sources merge
            var merged = PronunciationRuleSet.Merge(station, card);

            // Then exactly one rule survives the conflict — not both, in either order
            Assert.Single(merged.Rules);
        }

        [Fact]
        public static void The_card_wins_the_F97_2_heteronym_flagship_case()
        {
            // Executed case from review — the entire reason the Pattern/Word split exists. The
            // station has a blanket rule for the bare word "read"; the card has a MORE SPECIFIC
            // rule scoped to the phrase "have read" that disambiguates the past-tense reading.
            // These are different (Pattern, Word) pairs, so an identity-only merge lets the
            // station's blanket rule claim the span first and the card's rule never fires. Card
            // rules ordered ahead of station rules fixes that: the card's more specific rule now
            // gets first crack at the text, exactly like an operator-authored "specific rule
            // first" ordering would.
            var station = PronunciationRuleSet.Create([new PronunciationRule("read", "read", "/stn/")]);
            var card = PronunciationRuleSet.Create([new PronunciationRule("have read", "read", "/card/")]);

            var merged = PronunciationRuleSet.Merge(station, card);

            // Then the card's phoneme wins where its more specific pattern also matches —
            // canonical form (see the sibling fact above for why).
            Assert.Equal("card", Assert.Single(merged.Match("I have read it.")).Rule.Ipa);
        }

        [Fact]
        public static void The_flip_covers_literal_corrections_too()
        {
            // ⚠️ This REVERSES shipped F71.7 (SpeechCorrectionProvider.BuildMerged): a station
            // correction and a card correction for the same From
            var station = SpeechCorrectionSet.Create([new SpeechCorrection("MacLeod", "station-way")]);
            var cardCorrections = new List<SpeechCorrection> { new("MACLEOD", "card-way") };

            // When the merged set is built
            var merged = SpeechCorrectionProvider.BuildMerged(station, cardCorrections);
            var result = merged.Apply("MacLeod is on air.", out _);

            // Then the card correction wins, not the station one
            Assert.Equal("card-way is on air.", result);
        }
    }

    // T274 round-2 review finding R2: PronunciationRuleResolver.ResolveForRender (GenWave.Tts) is
    // its OWN seam — the ONE place both TtsSegmentSource's on-air render and the admin preview
    // resolve rules through — and an inverted-precedence mutation planted INSIDE it (station/card
    // swapped at its own BuildMerged call) left the whole solution green: every existing fact
    // above pins PronunciationRuleSet.Merge directly, never the resolver's own composition of it.
    // These pin the resolver's OUTPUT, not the lower-level Merge it delegates to.
    public static class ScenarioTheResolverComposesThePersonaOverStationPrecedence
    {
        [Fact]
        public static void An_identity_colliding_card_rule_wins_the_resolved_output()
        {
            var station = PronunciationRuleSet.Create([new PronunciationRule("MacLeod", "MacLeod", "/stationIpa/")]);
            var card = new List<PronunciationRule> { new("MacLeod", "MacLeod", "/cardIpa/") };

            var resolved = PronunciationRuleResolver.ResolveForRender(station, card);

            // Canonical (slash-free) form: PronunciationRuleSet.Create canonicalizes Ipa before
            // compiling (T138 review) — the resolver's output carries that same canonical form.
            Assert.Equal("cardIpa", Assert.Single(resolved).Ipa);
        }

        [Fact]
        public static void A_card_rule_sorts_ahead_of_a_station_rule_in_the_resolved_output()
        {
            // Non-colliding rules — both survive — so ordering (not identity) is what this pins:
            // PronunciationRuleSet.Match's first-rule-claims-the-span overlap policy (F97.3) means
            // this ORDER, not merely presence, is what decides an overlapping-span contest one
            // layer down (KokoroSpeechMarkup/KokoroFallbackRenderer), so a resolver-level ordering
            // bug is invisible to every fact that only checks set membership.
            var station = PronunciationRuleSet.Create([new PronunciationRule("Zenith", "Zenith", "/stationIpa/")]);
            var card = new List<PronunciationRule> { new("Nova", "Nova", "/cardIpa/") };

            var resolved = PronunciationRuleResolver.ResolveForRender(station, card);

            Assert.Equal("Nova", resolved[0].Pattern);
        }
    }

    public static class ScenarioAFiringRuleIsVisibleInTheField
    {
        /// <summary>
        /// Renders through the REAL KokoroTtsSynthesizer with a rule that actually matches its own
        /// spoken text (never through TtsSegmentSource — that end-to-end wiring is
        /// ScenarioRulesRideWithTheRequest's job above; this scenario pins the observability
        /// contract at the render adapter itself) — a fresh render per fact, mirroring
        /// ScenarioRulesRideWithTheRequest's own "each fact re-arranges" shape in this file rather
        /// than a shared IAsyncLifetime fixture.
        /// </summary>
        static async Task<(CapturingLogger<PronunciationRuleHitReporter> Logger, PronunciationRuleHitStats Stats)>
            RenderWithFiringRuleAsync()
        {
            var stats = new PronunciationRuleHitStats();
            var logger = new CapturingLogger<PronunciationRuleHitReporter>();
            var reporter = new PronunciationRuleHitReporter(stats, logger);
            var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) }));
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var synth = new KokoroTtsSynthesizer(
                    new HttpClient(handler),
                    new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" }),
                    reporter);
                var context = new TtsRenderContext("Here is MacLeod.", "af_heart", SegmentKind.LeadIn)
                    with { Rules = [new ContextPronunciationRule("MacLeod", "MacLeod", "/məˈklaʊd/")] };

                await synth.SynthesizeAsync(context, CancellationToken.None);
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }

            return (logger, stats);
        }

        [Fact]
        public static async Task The_line_is_emitted_at_information_not_debug()
        {
            // The whole point: Debug never reaches Loki, so Debug is indistinguishable
            // from no logging at all (F68.7 as amended by F97.5).
            var (logger, _) = await RenderWithFiringRuleAsync();

            Assert.Contains(logger.Entries, e => e.Level == LogLevel.Information);
            Assert.DoesNotContain(logger.Entries, e => e.Level is LogLevel.Debug or LogLevel.Warning);
        }

        [Fact]
        public static async Task The_line_names_the_rule_that_fired()
        {
            var (logger, _) = await RenderWithFiringRuleAsync();

            Assert.Contains(logger.Entries, e =>
                e.Level == LogLevel.Information && e.Message.Contains("MacLeod", StringComparison.Ordinal));
        }

        [Fact]
        public static async Task The_line_names_the_speech_kind()
        {
            var (logger, _) = await RenderWithFiringRuleAsync();

            Assert.Contains(logger.Entries, e =>
                e.Level == LogLevel.Information
                && e.Message.Contains(nameof(SegmentKind.LeadIn), StringComparison.Ordinal));
        }

        [Fact]
        public static async Task That_rules_counter_increments()
        {
            var (_, stats) = await RenderWithFiringRuleAsync();

            Assert.Equal(1, Assert.Single(stats.Snapshot()).Fired);
        }
    }

    public static class ScenarioAFiredHitIsNeverDoubleCounted
    {
        [Fact]
        public static async Task A_hop_that_succeeds_after_the_primary_fails_counts_the_hit_once()
        {
            // Given a primary that fails (500) and a configured Kokoro-kind fallback hop that
            // succeeds — the SAME rule, on the SAME text, so BOTH engines' KokoroSpeechMarkup pass
            // finds the identical match. A naive "report at markup-composition time" (the pre-review
            // ordering) would report from the primary before its own POST ever fails, then report
            // AGAIN from the hop that actually airs the line — counter=2 for one aired line (the
            // probe finding this fact pins). Reporting only after EnsureSuccessStatusCode makes the
            // primary's own report unreachable when it throws, so only the hop that actually spoke
            // ever counts.
            var stats = new PronunciationRuleHitStats();
            var logger = new CapturingLogger<PronunciationRuleHitReporter>();
            var reporter = new PronunciationRuleHitReporter(stats, logger);
            var primaryHandler = new FakeHttpMessageHandler(
                (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
            var hopHandler = new FakeHttpMessageHandler((_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) }));
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var ttsOptions = new TestOptionsMonitor<TtsOptions>(
                    new TtsOptions { CacheRoot = cacheRoot, Format = "wav" });
                var primary = new KokoroTtsSynthesizer(new HttpClient(primaryHandler), ttsOptions, reporter);
                var hop = new KokoroFallbackRenderer(new HttpClient(hopHandler), ttsOptions, reporter);
                var fallbackOptions = new TestOptionsMonitor<TtsFallbackOptions>(new TtsFallbackOptions
                {
                    Profiles =
                    [
                        new TtsFallbackProfile
                        {
                            Engine = DependencyNames.Kokoro, Endpoint = "http://backup-kokoro:8880", Voice = "",
                        },
                    ],
                });
                var router = new FallbackTtsSynthesizer(
                    primary, [hop], new FakeDependencyHealth(), fallbackOptions,
                    NullLogger<FallbackTtsSynthesizer>.Instance);
                var context = new TtsRenderContext("Here is MacLeod.", "af_heart", SegmentKind.LeadIn)
                    with { Rules = [new ContextPronunciationRule("MacLeod", "MacLeod", "/məˈklaʊd/")] };

                await router.SynthesizeAsync(context, CancellationToken.None);

                // Then the one line that actually aired (rendered by the hop, after the primary's
                // 500) is counted exactly once.
                Assert.Equal(1, Assert.Single(stats.Snapshot()).Fired);
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    public static class ScenarioRulesRideWithTheRequest
    {
        static PersonaCard CardWithPronunciation(ContextPronunciationRule rule) =>
            new(
                PersonaCard.CurrentSchemaVersion, "Test Persona", "", "", [],
                new VoiceSpec("kokoro", "af_heart", 1.0, "en"), EnergyDisposition: 0, [], [],
                Pronunciations: [rule]);

        static string InputOf(string requestBody) =>
            JsonDocument.Parse(requestBody).RootElement.GetProperty("input").GetString() ?? "";

        /// <summary>
        /// Answers <c>cardA</c> on its first call, then <c>cardB</c> on every call after —
        /// simulating an active-persona flip landing DURING a render, exactly the window
        /// <see cref="ActivePersonaPronunciationRulesCache.RefreshIfStaleAsync"/> reads through once
        /// per render (SPEC F97.6, F92.2's HandoffContext lesson applied to TTS rules).
        /// </summary>
        sealed class OneShotThenFlipsPersonaAccessor(PersonaCard cardA, PersonaCard cardB) : IActivePersonaAccessor
        {
            int calls;

            public Task<Persona?> ResolveAsync(CancellationToken ct) => Task.FromResult<Persona?>(null);

            public Task<PersonaCard?> ResolveCardAsync(CancellationToken ct) =>
                Task.FromResult<PersonaCard?>(Interlocked.Increment(ref calls) == 1 ? cardA : cardB);
        }

        [Fact]
        public static async Task A_boundary_crossing_render_uses_the_authoring_personas_rules()
        {
            // Given persona A's card is the one and only answer this render's own resolve reads —
            // any LATER read (which nothing downstream should ever make, but this accessor is
            // scripted to prove it if it did) would already answer persona B instead.
            var ruleA = new ContextPronunciationRule("MacLeod", "MacLeod", "/personaA/");
            var ruleB = new ContextPronunciationRule("MacLeod", "MacLeod", "/personaB/");
            var accessor = new OneShotThenFlipsPersonaAccessor(CardWithPronunciation(ruleA), CardWithPronunciation(ruleB));
            var requests = new List<string>();
            var handler = new FakeHttpMessageHandler(async (request, ct) =>
            {
                requests.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) };
            });
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var source = new TtsSegmentSource(
                    new FakeSegmentCopyWriter("Say MacLeod now."),
                    new KokoroTtsSynthesizer(new HttpClient(handler), new TestOptionsMonitor<TtsOptions>(
                        new TtsOptions { CacheRoot = cacheRoot, Format = "wav" })),
                    new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(),
                    NoCorrections.Provider(), NoCorrections.PersonaCache(),
                    NoCorrections.PronunciationProvider(), new ActivePersonaPronunciationRulesCache(accessor, TimeProvider.System),
                    NoCorrections.PersonaPaceCache(),
                    new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" }),
                    NullLogger<TtsSegmentSource>.Instance);
                var request = new SegmentRequest(
                    SegmentKind.StationId, "af_heart", "GenWave", null, DateTimeOffset.UtcNow, "test-station");

                await source.RenderAsync(request, CancellationToken.None);

                // Then the markup carries persona A's phonemes — the one resolve this render made —
                // never persona B's, even though the accessor would already answer B by now.
                Assert.Contains("/personaA/", InputOf(Assert.Single(requests)));
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }
        }

        [Fact]
        public static async Task The_adapter_never_reads_an_ambient_persona_accessor()
        {
            // KokoroFallbackRenderer's constructor (unlike TtsSegmentSource's) carries no
            // IActivePersonaAccessor, no PronunciationRuleProvider, no rules-provider of any kind —
            // there is nothing here for it to read even if it wanted to. A render whose context
            // already carries resolved rules (SPEC F97.6, "resolved upstream, at TtsSegmentSource")
            // is therefore structurally unaffected by whatever the ambient world does next.
            var context = new TtsRenderContext("Say MacLeod now.", "af_heart", Kind: null)
                with { Rules = [new ContextPronunciationRule("MacLeod", "MacLeod", "/resolvedUpstream/")] };
            var requests = new List<string>();
            var handler = new FakeHttpMessageHandler(async (request, ct) =>
            {
                requests.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) };
            });
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var renderer = new KokoroFallbackRenderer(
                    new HttpClient(handler),
                    new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" }));
                var profile = new TtsFallbackProfile { Engine = DependencyNames.Kokoro, Endpoint = "http://backup-kokoro:8880", Voice = "" };

                await renderer.RenderAsync(profile, context, CancellationToken.None);

                Assert.Contains("/resolvedUpstream/", InputOf(Assert.Single(requests)));
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }
        }

        [Fact]
        public static async Task The_hop_reports_its_own_fired_rule_hit()
        {
            // A kokoro-kind fallback hop, rendered directly (never through the primary or
            // FallbackTtsSynthesizer) — PronunciationRuleHitReporter.Report must be a REAL call on
            // this class's own render path, not merely referenced (review finding, PLAN T142: a
            // mutant that neuters the call to `_ = ruleHits;` was fully green before this fact).
            var stats = new PronunciationRuleHitStats();
            var logger = new CapturingLogger<PronunciationRuleHitReporter>();
            var reporter = new PronunciationRuleHitReporter(stats, logger);
            var context = new TtsRenderContext("Say MacLeod now.", "af_heart", SegmentKind.BackAnnounce)
                with { Rules = [new ContextPronunciationRule("MacLeod", "MacLeod", "/məˈklaʊd/")] };
            var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) }));
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var renderer = new KokoroFallbackRenderer(
                    new HttpClient(handler),
                    new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" }),
                    reporter);
                var profile = new TtsFallbackProfile { Engine = DependencyNames.Kokoro, Endpoint = "http://backup-kokoro:8880", Voice = "" };

                await renderer.RenderAsync(profile, context, CancellationToken.None);

                Assert.Equal(1, Assert.Single(stats.Snapshot()).Fired);
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    // -------------------------------------------------------------------------------------
    // SAD PATH
    // -------------------------------------------------------------------------------------
    public static class ScenarioPreviewsAndMalformedData
    {
        /// <summary>
        /// Exercises KokoroTtsSynthesizer's CONTEXT-AWARE overload with the REAL admin-preview
        /// shape — <c>Kind: null</c>, <c>Rules</c> left at its own empty default — rather than the
        /// plain two-arg overload (review finding, PLAN T142): that plain overload is DEAD on the
        /// production preview path. <c>TtsPreviewController</c> calls
        /// <c>NormalizingTtsSynthesizer</c>'s own plain <c>SynthesizeAsync(text, voice, ct)</c>,
        /// which immediately wraps its arguments into a <c>TtsRenderContext</c> and relays THAT
        /// context, unchanged in shape, through <c>FallbackTtsSynthesizer</c> down to the primary —
        /// so a preview request never actually reaches the primary's plain two-arg overload; only
        /// its context-aware one. Building the context directly here (rather than standing up the
        /// full <c>NormalizingTtsSynthesizer</c>/<c>FallbackTtsSynthesizer</c> chain) is the
        /// smallest arrangement that reaches that exact real shape.
        /// </summary>
        static async Task<(CapturingLogger<PronunciationRuleHitReporter> Logger, PronunciationRuleHitStats Stats)>
            PreviewRenderAsync()
        {
            var stats = new PronunciationRuleHitStats();
            var logger = new CapturingLogger<PronunciationRuleHitReporter>();
            var reporter = new PronunciationRuleHitReporter(stats, logger);
            var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) }));
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var synth = new KokoroTtsSynthesizer(
                    new HttpClient(handler),
                    new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" }),
                    reporter);
                // Kind: null, Rules at its own default (empty) — exactly what
                // NormalizingTtsSynthesizer's plain overload constructs for a preview (see remarks).
                var context = new TtsRenderContext("Here is MacLeod.", "af_heart", Kind: null);

                await synth.SynthesizeAsync(context, CancellationToken.None);
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }

            return (logger, stats);
        }

        [Fact]
        public static async Task A_preview_never_increments_a_counter()
        {
            // Mirrors the existing F68.7 preview carve-out: previews are operator-explicit
            // and must not pollute on-air observability.
            var (_, stats) = await PreviewRenderAsync();

            Assert.Empty(stats.Snapshot());
        }

        [Fact]
        public static async Task A_preview_emits_no_information_line()
        {
            var (logger, _) = await PreviewRenderAsync();

            Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Information);
        }

        [Fact]
        public static void A_pattern_carrying_a_newline_cannot_forge_a_log_line()
        {
            // A card-authored rule is imported-card content, the catalog log-forging precedent
            // (CodeQL cs/log-forging): PronunciationRuleHitReporter.Report must neutralize it before
            // it ever reaches a log call, not merely happen to look clean today (review finding,
            // PLAN T142: a mutant that dropped LogSanitize.Strip was fully green before this fact).
            // Hand-built PronunciationMatch — bypassing PronunciationRuleSet.Match entirely — is the
            // narrowest way to exercise Report's OWN sanitization independent of whatever the regex
            // matcher would or wouldn't ever let through a live pattern.
            var stats = new PronunciationRuleHitStats();
            var logger = new CapturingLogger<PronunciationRuleHitReporter>();
            var reporter = new PronunciationRuleHitReporter(stats, logger);
            var rule = new PronunciationRule("Mac\nLeod", "Mac\nLeod", "/ipa/");

            reporter.Report([new PronunciationMatch(0, rule.Word.Length, rule)], SegmentKind.LeadIn, isAudition: false);

            Assert.DoesNotContain(logger.Entries, e => e.Message.Contains('\n'));
        }

        [Fact]
        public static void Malformed_rule_settings_degrade_to_an_empty_set()
        {
            // Given a station rule whose Word does not occur inside its own Pattern, and a card
            // rule with a blank Pattern — both malformed (F97.1), mirroring the existing
            // corrections-parsing degrade-not-throw posture (F68.5)
            var station = PronunciationRuleSet.Create([new PronunciationRule("MacLeod", "Rutherford", "/x/")]);
            var card = PronunciationRuleSet.Create([new PronunciationRule("", "", "/y/")]);

            // When the two malformed sources merge
            var merged = PronunciationRuleSet.Merge(station, card);

            // Then no rule compiled from either side
            Assert.Empty(merged.Rules);
        }

        [Fact]
        public static void A_render_continues_unruled_when_the_set_is_empty()
        {
            // Given a merge of two empty rule sets (no station rules, no card rules)
            var merged = PronunciationRuleSet.Merge(PronunciationRuleSet.Empty, PronunciationRuleSet.Empty);

            // When matching runs against ordinary text
            var matches = merged.Match("The show goes on regardless.");

            // Then nothing is annotated — the render proceeds unruled
            Assert.Empty(matches);
        }
    }
}
