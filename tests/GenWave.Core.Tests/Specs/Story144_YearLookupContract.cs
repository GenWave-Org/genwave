// STORY-144 — Missing release years filled from MusicBrainz (Epic X / SPEC F48, closes gitea-#208) —
// Core contract half. The HTTP client half lives in
// MediaLibrary.Tests/Specs/Story144_MusicBrainzYearLookup.cs; the claim/pacing pipeline in
// MediaLibrary.Tests/Specs/Story144_YearLookupPipeline.cs; the settings keys in
// Host.Tests/Specs/Story144_YearLookupSettingsKeys.cs.
//
// BDD specification — xUnit. Authored PENDING at /plan time (2026-07-14, house rule since Epic S).

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Core.Tests.Specs;

public static class FeatureYearLookupContract
{
    public sealed class ScenarioTheLookupSeamIsNarrowAndNullable
    {
        /// <summary>
        /// A fake, in-memory <see cref="IYearLookup"/> — enough to prove the seam is a legal,
        /// callable contract without touching any HTTP code (that belongs to MusicBrainzYearLookup /
        /// MediaLibrary.Tests).
        /// </summary>
        sealed class FakeYearLookup(int? result) : IYearLookup
        {
            public Task<int?> TryLookupAsync(string artist, string title, string? album, CancellationToken ct)
                => Task.FromResult(result);
        }

        [Fact]
        public void TheSeamTakesTagStringsAndReturnsANullableYear()
        {
            var m = typeof(IYearLookup).GetMethod("TryLookupAsync")!;
            Assert.Equal(typeof(Task<int?>), m.ReturnType);

            var p = m.GetParameters();
            Assert.Equal(4, p.Length);
            Assert.Equal(typeof(string), p[0].ParameterType);
            Assert.Equal(typeof(string), p[1].ParameterType);
            Assert.Equal(typeof(string), p[2].ParameterType);
            Assert.Equal(typeof(CancellationToken), p[3].ParameterType);
        }

        [Fact]
        public async Task NoConfidentMatchIsNullNotAnException()
        {
            IYearLookup lookup = new FakeYearLookup(result: null);
            var year = await lookup.TryLookupAsync("Some Artist", "Some Title", null, CancellationToken.None);
            Assert.Null(year);
        }
    }

    public sealed class ScenarioReenrichGainsAYearToken
    {
        // Core.Tests references only GenWave.Core (no Host project reference), so the string-token
        // half of "year" (ReenrichFieldsParser's allowlist switch) is exercised in Host.Tests instead —
        // this scenario proves the Core-level half of the contract: ReenrichFields.Year is a legal,
        // distinct bit flag and All includes it (SPEC F48.6). Mirrors Story142's BpmAnalyzerContract
        // precedent (ScenarioReenrichGainsABpmToken) exactly.

        [Fact]
        public void YearParsesAsALegalReenrichField()
        {
            Assert.True(Enum.IsDefined(typeof(ReenrichFields), ReenrichFields.Year));
            Assert.Equal(32, (int)ReenrichFields.Year);
            Assert.NotEqual(ReenrichFields.None, ReenrichFields.Year);
        }

        [Fact]
        public void AllIncludesYear()
        {
            Assert.True(ReenrichFields.All.HasFlag(ReenrichFields.Year));
        }

        /// <summary>
        /// The SQL arm's "sentinel only, value untouched" behavior (SPEC F48.6) is not provable from
        /// Core — it lives in <c>MediaRepository.BuildReenrichSetClauses</c> (MediaLibrary project),
        /// pinned by <c>MediaLibrary.Tests/Specs/Story144_YearLookupPipeline.cs</c>'s
        /// <c>AYearReenrichResetNullsOnlyTheSentinelValueUntouched</c> fact. What IS provable here:
        /// <see cref="ReenrichFields.Year"/> occupies its own bit, disjoint from every sibling flag —
        /// so a caller requesting only <c>Year</c> cannot accidentally also carry another flag's
        /// column-reset semantics into the SQL builder.
        /// </summary>
        [Fact]
        public void TheYearResetGroupTargetsTheSentinelNotTheValue()
        {
            Assert.Equal(ReenrichFields.None, ReenrichFields.Year & ReenrichFields.Cue);
            Assert.Equal(ReenrichFields.None, ReenrichFields.Year & ReenrichFields.Energy);
            Assert.Equal(ReenrichFields.None, ReenrichFields.Year & ReenrichFields.Loudness);
            Assert.Equal(ReenrichFields.None, ReenrichFields.Year & ReenrichFields.Tags);
            Assert.Equal(ReenrichFields.None, ReenrichFields.Year & ReenrichFields.Bpm);
        }
    }
}
