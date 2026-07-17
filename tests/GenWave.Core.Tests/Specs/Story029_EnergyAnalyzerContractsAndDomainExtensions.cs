// STORY-029 — Core contracts: IEnergyAnalyzer + EnergyPoints + extend MediaItem/MediaReference
//
// BDD specification — xUnit. Pure reflection over Core.

using System.Reflection;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Core.Tests.Specs;

public static class FeatureEnergyAnalyzerContractsAndDomainExtensions
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioIEnergyAnalyzerContractLivesInCore
    {
        [Fact]
        public void TypeIsInCoreAbstractions()
        {
            var t = typeof(IEnergyAnalyzer);
            Assert.Equal("GenWave.Core.Abstractions", t.Namespace);
        }

        [Fact]
        public void IsAnInterface()
        {
            Assert.True(typeof(IEnergyAnalyzer).IsInterface);
        }

        [Fact]
        public void AnalyzeAsyncReturnsNullableEnergyPointsTask()
        {
            var m = typeof(IEnergyAnalyzer).GetMethod("AnalyzeAsync")!;
            Assert.Equal(typeof(Task<EnergyPoints?>), m.ReturnType);
        }

        [Fact]
        public void AnalyzeAsyncTakesPathCueInCueOutAndCancellationToken()
        {
            // params: (string path, double? cueInSec, double? cueOutSec, CancellationToken ct)
            var m = typeof(IEnergyAnalyzer).GetMethod("AnalyzeAsync")!;
            var p = m.GetParameters();
            Assert.Equal(4, p.Length);
            Assert.Equal(typeof(string), p[0].ParameterType);
            Assert.Equal(typeof(double?), p[1].ParameterType);
            Assert.Equal(typeof(double?), p[2].ParameterType);
            Assert.Equal(typeof(CancellationToken), p[3].ParameterType);
        }
    }

    public sealed class ScenarioEnergyPointsValueType
    {
        [Fact]
        public void EnergyPointsIsASealedRecord()
        {
            Assert.True(typeof(EnergyPoints).IsSealed);
        }

        [Fact]
        public void HasIntroEnergyDouble()
        {
            Assert.Equal(typeof(double), typeof(EnergyPoints).GetProperty("IntroEnergy")!.PropertyType);
        }

        [Fact]
        public void HasOutroEnergyDouble()
        {
            Assert.Equal(typeof(double), typeof(EnergyPoints).GetProperty("OutroEnergy")!.PropertyType);
        }
    }

    public sealed class ScenarioDomainDtosCarryNullableEnergy
    {
        [Fact]
        public void MediaItemHasNullableIntroEnergy()
        {
            Assert.Equal(typeof(double?), typeof(MediaItem).GetProperty("IntroEnergy")!.PropertyType);
        }

        [Fact]
        public void MediaItemHasNullableOutroEnergy()
        {
            Assert.Equal(typeof(double?), typeof(MediaItem).GetProperty("OutroEnergy")!.PropertyType);
        }

        [Fact]
        public void MediaReferenceHasNullableIntroEnergy()
        {
            Assert.Equal(typeof(double?), typeof(MediaReference).GetProperty("IntroEnergy")!.PropertyType);
        }

        [Fact]
        public void MediaReferenceHasNullableOutroEnergy()
        {
            Assert.Equal(typeof(double?), typeof(MediaReference).GetProperty("OutroEnergy")!.PropertyType);
        }
    }
}
