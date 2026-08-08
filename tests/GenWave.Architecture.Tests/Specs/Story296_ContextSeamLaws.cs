// STORY-296 — The context seam exists: the laws cover the new surface (F107.1, F107.2)
using GenWave.Architecture.Tests.Support;

namespace GenWave.Architecture.Tests.Specs;

public static class FeatureContextSeamUnderTheLaws
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheNewProjectJoinsTheLaws
    {
        [Fact(Skip = "Pending T222 — see docs/PLAN.md")]
        public void L1ProjectListIncludesGenWaveContext()
        {
            // The framework-free inner-project list gains GenWave.Context; a deliberate
            // ASP.NET reference from it must go L1-red (mutation-checked at build).
            // Assert.Contains("GenWave.Context", L1InnerProjects.All);
            Assert.Fail("pending T222");
        }

        [Fact(Skip = "Pending T227 — see docs/PLAN.md")]
        public void L3SeamListCarriesTheWeatherProvider()
        {
            // HttpClientSeams.DesignatedSeams gains GenWave.Context.WeatherContextProvider
            // (or its typed-client factory site) in the same change that introduces it.
            // Assert.Contains(seams, s => s.Contains("WeatherContextProvider"));
            Assert.Fail("pending T227");
        }

        [Fact(Skip = "Pending T228 — see docs/PLAN.md")]
        public void L3SeamListCarriesTheHistoryProvider()
        {
            // Same rule for GenWave.Context.HistoryContextProvider.
            // Assert.Contains(seams, s => s.Contains("HistoryContextProvider"));
            Assert.Fail("pending T228");
        }
    }

    public sealed class ScenarioTheContractStaysClean
    {
        [Fact]
        public void IContextProviderLivesInAbstractions()
        {
            // The contract lives under the SAME legacy namespace every other Abstractions/ seam
            // interface does (ITtsSegmentSource, INextItemProvider, ...) — GenWave.Core.Abstractions,
            // not the assembly's own GenWave.Abstractions.* — the folder's established convention
            // (Playout/ is the one exception, its own newer namespace).
            var contractType = ProductionAssemblies.Abstractions.GetType("GenWave.Core.Abstractions.IContextProvider");
            Assert.NotNull(contractType);
            Assert.True(contractType.IsInterface);

            var keyProperty = contractType.GetProperty("Key");
            Assert.NotNull(keyProperty);
            Assert.Equal(typeof(string), keyProperty.PropertyType);

            // The one fetch method: GetMethods() also reports get_Key as a compiler "special name"
            // method, filtered out so exactly the fetch survives as the interface's single method.
            var fetchMethod = Assert.Single(contractType.GetMethods(), m => !m.IsSpecialName);
            Assert.Equal("FetchAsync", fetchMethod.Name);

            var contentType = ProductionAssemblies.Abstractions.GetType("GenWave.Core.Domain.ContextContent");
            Assert.NotNull(contentType);
            Assert.Equal(typeof(Task<>).MakeGenericType(contentType), fetchMethod.ReturnType);

            var parameter = Assert.Single(fetchMethod.GetParameters());
            Assert.Equal(typeof(CancellationToken), parameter.ParameterType);
        }

        [Fact]
        public void ContextContentIsAnImmutableRecord()
        {
            var contentType = ProductionAssemblies.Abstractions.GetType("GenWave.Core.Domain.ContextContent");
            Assert.NotNull(contentType);

            // "<Clone>$" is the one reflectable trace of the `record` keyword itself — the compiler
            // synthesizes it on every record, class or struct, positional or not.
            Assert.NotNull(contentType.GetMethod("<Clone>$"));

            // L4-immutability's own detector (AbstractionsImmutability), reused rather than
            // re-implemented — the exact mechanism ScenarioL4Immutability (Story291_ConventionLaws.cs)
            // already runs over the whole assembly and would catch this type in either way.
            var violations = AbstractionsImmutability.FindViolations([contentType]);
            Assert.Empty(violations);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioHostStaysEmpty
    {
        [Fact(Skip = "Pending T222 — see docs/PLAN.md")]
        public void HostContextReservationRemainsEmpty()
        {
            // The L5 tripwire's GenWave.Host.Context reservation must still match zero types
            // after the cycle builds — the subsystem was born outside (F105.4).
            // Assert.Empty(typesUnderReservedNamespace);
            Assert.Fail("pending T222");
        }
    }
}
