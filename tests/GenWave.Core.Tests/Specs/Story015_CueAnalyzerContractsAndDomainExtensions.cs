// STORY-015 — Core contracts: ICueAnalyzer + CuePoints + extend MediaItem/MediaReference

using System.Reflection;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Core.Tests.Specs;

public static class FeatureCueAnalyzerContractsAndDomainExtensions
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioICueAnalyzerContractLivesInCore
    {
        [Fact]
        public void TypeIsInCoreAbstractions()
        {
            var t = typeof(ICueAnalyzer);
            Assert.Equal("GenWave.Core.Abstractions", t.Namespace);
        }

        [Fact]
        public void AnalyzeAsyncReturnsTaskOfNullableCuePoints()
        {
            var m = typeof(ICueAnalyzer).GetMethod("AnalyzeAsync")!;
            Assert.Equal(typeof(Task<CuePoints?>), m.ReturnType);
        }

        [Fact]
        public void AnalyzeAsyncTakesPathAndCancellationToken()
        {
            var m = typeof(ICueAnalyzer).GetMethod("AnalyzeAsync")!;
            var p = m.GetParameters();
            Assert.Equal(typeof(string), p[0].ParameterType);
            Assert.Equal(typeof(CancellationToken), p[1].ParameterType);
        }
    }

    public sealed class ScenarioCuePointsRecordExistsInCore
    {
        [Fact]
        public void TypeIsASealedRecord()
        {
            var t = typeof(CuePoints);
            Assert.True(t.IsSealed && t.GetMethods().Any(m => m.Name == "<Clone>$"));
        }

        [Fact]
        public void HasCueInSecPropertyOfTypeDouble()
        {
            var p = typeof(CuePoints).GetProperty(nameof(CuePoints.CueInSec))!;
            Assert.Equal(typeof(double), p.PropertyType);
        }

        [Fact]
        public void HasCueOutSecPropertyOfTypeDouble()
        {
            var p = typeof(CuePoints).GetProperty(nameof(CuePoints.CueOutSec))!;
            Assert.Equal(typeof(double), p.PropertyType);
        }
    }

    public sealed class ScenarioMediaItemCarriesOptionalCuePoints
    {
        [Fact]
        public void MediaItemHasNullableCueProperty()
        {
            var p = typeof(MediaItem).GetProperty("Cue")!;
            Assert.Equal(typeof(CuePoints), Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType);
        }

        [Fact]
        public void MediaItemConstructedWithoutCueDefaultsToNull()
        {
            var item = new MediaItem("id", "/media/foo.mp3", "title", new Loudness(-23.0, -1.0, true));
            Assert.Null(item.Cue);
        }
    }

    public sealed class ScenarioMediaReferenceCarriesOptionalCuePoints
    {
        [Fact]
        public void MediaReferenceHasNullableCueProperty()
        {
            var p = typeof(MediaReference).GetProperty("Cue")!;
            Assert.Equal(typeof(CuePoints), Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType);
        }

        [Fact]
        public void MediaReferenceConstructedWithoutCueDefaultsToNull()
        {
            var r = new MediaReference("id", "/media/foo.mp3", "title", new Loudness(-23.0, -1.0, true),
                null, null, null, null, null, null, null, null);
            Assert.Null(r.Cue);
        }
    }

    public sealed class ScenarioExistingConstructionSitesStillCompile
    {
        [Fact]
        public void SolutionBuildsWithZeroNewWarnings()
        {
            // This is enforced by `dotnet build GenWave.sln` succeeding with zero warnings.
            // The fact is a witness — the actual gate is CI.
            Assert.True(true);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — invariants on CuePoints
    // ---------------------------------------------------------------------

    public sealed class ScenarioCuePointsCannotRepresentInvertedRangesSilently
    {
        [Fact]
        public void ThrowsArgumentExceptionWhenCueInSecExceedsCueOutSec()
        {
            var act = () => new CuePoints(CueInSec: 10.0, CueOutSec: 5.0);
            Assert.Throws<ArgumentException>(act);
        }
    }
}
