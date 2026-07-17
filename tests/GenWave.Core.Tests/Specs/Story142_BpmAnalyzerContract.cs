// STORY-142 — BPM measured at enrichment (Epic X / SPEC F46, closes gitea-#190) — Core contract half.
// The aubio parse half lives in MediaLibrary.Tests/Specs/Story142_AubioBpmAnalyzer.cs; the
// enrichment/backfill half in MediaLibrary.Tests/Specs/Story142_BpmEnrichmentAndBackfill.cs.
//
// BDD specification — xUnit. Authored PENDING at /plan time (2026-07-14, house rule since Epic S).

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Core.Tests.Specs;

public static class FeatureBpmAnalyzerContract
{
    public sealed class ScenarioTheAnalyzerSeamMirrorsItsSiblings
    {
        /// <summary>
        /// A fake, in-memory <see cref="IBpmAnalyzer"/> — enough to prove the seam is a legal,
        /// callable contract without touching any process-invocation code (that belongs to
        /// AubioBpmAnalyzer / MediaLibrary.Tests).
        /// </summary>
        sealed class FakeBpmAnalyzer(double? result) : IBpmAnalyzer
        {
            public Task<double?> AnalyzeAsync(string path, double? cueInSec, double? cueOutSec, CancellationToken ct)
                => Task.FromResult(result);
        }

        [Fact]
        public void TheSeamAcceptsTheCueWindowAndReturnsANullableTempo()
        {
            var m = typeof(IBpmAnalyzer).GetMethod("AnalyzeAsync")!;
            Assert.Equal(typeof(Task<double?>), m.ReturnType);

            var p = m.GetParameters();
            Assert.Equal(4, p.Length);
            Assert.Equal(typeof(string), p[0].ParameterType);
            Assert.Equal(typeof(double?), p[1].ParameterType);
            Assert.Equal(typeof(double?), p[2].ParameterType);
            Assert.Equal(typeof(CancellationToken), p[3].ParameterType);
        }

        [Fact]
        public async Task ANullResultIsALegalOutcomeNotAnError()
        {
            IBpmAnalyzer analyzer = new FakeBpmAnalyzer(result: null);
            var tempo = await analyzer.AnalyzeAsync("/media/track.flac", 1.0, 200.0, CancellationToken.None);
            Assert.Null(tempo);
        }
    }

    public sealed class ScenarioReenrichGainsABpmToken
    {
        // Core.Tests references only GenWave.Core (no Host project reference), so the string-token
        // half of "bpm" (ReenrichFieldsParser's allowlist switch) is exercised in Host.Tests instead —
        // this scenario proves the Core-level half of the contract: ReenrichFields.Bpm is a legal,
        // distinct bit flag and All includes it (SPEC F46.4). Mirrors Story050's reflection idiom.

        [Fact]
        public void BpmParsesAsALegalReenrichField()
        {
            Assert.True(Enum.IsDefined(typeof(ReenrichFields), ReenrichFields.Bpm));
            Assert.Equal(16, (int)ReenrichFields.Bpm);
            Assert.NotEqual(ReenrichFields.None, ReenrichFields.Bpm);
        }

        [Fact]
        public void AllIncludesBpm()
        {
            Assert.True(ReenrichFields.All.HasFlag(ReenrichFields.Bpm));
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioUnknownTokensStillFourHundred
    {
        [Fact]
        public void ABogusFieldTokenStillFailsParsing()
        {
            // The F20.10 unknown-token 400 contract lives in Host's ReenrichFieldsParser (an explicit
            // allowlist switch — exercised directly in Host.Tests/Specs/Story051_ReenrichmentEndpoints.cs),
            // which is unreachable from this Core-only project. What IS provable here: adding Bpm did
            // not silently widen the Core enum's closed vocabulary — a bogus token like "tempo" still
            // has no corresponding defined member for the parser's switch to accidentally match.
            // Updated by X5 (SPEC F48.6): Year joined the enum's closed vocabulary alongside Bpm.
            var names = Enum.GetNames<ReenrichFields>().ToHashSet();
            Assert.Equal(
                new HashSet<string> { "None", "Cue", "Energy", "Loudness", "Tags", "Bpm", "Year", "All" },
                names);
        }
    }
}
