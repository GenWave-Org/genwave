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
        [Fact]
        public void L1ProjectListIncludesGenWaveContext()
        {
            // The framework-free inner-project list gains GenWave.Context; a deliberate ASP.NET
            // reference from it would now go L1-red automatically — ScenarioL1FrameworkFreeInnerProjects
            // (Story290_DependencyLaws.cs) runs its forbidden-reference scan over EVERY entry in this
            // list, so this fact only needs to prove membership, not re-run that scan itself.
            var context = Assert.Single(
                ProductionAssemblies.InnerProjects, project => project.Label == "GenWave.Context");

            // Not a phantom label pointing at nothing: the anchor resolved a real, loadable assembly
            // with real types in it (the T212 seam-list lesson — a resolution fact that can't
            // discriminate proves nothing).
            Assert.NotEmpty(context.Assembly.GetTypes());
        }

        [Fact]
        public void L3SeamListCarriesTheWeatherProvider()
        {
            // HttpClientSeams.DesignatedSeams gains GenWave.Context.Weather.WeatherContextProvider
            // (the typed-client construction site) in the same change that introduces it.
            Assert.Contains(
                HttpClientSeams.DesignatedSeams, seam => seam.Contains("WeatherContextProvider", StringComparison.Ordinal));
        }

        [Fact]
        public void L3SeamListCarriesTheHistoryProvider()
        {
            // HttpClientSeams.DesignatedSeams gains GenWave.Context.History.HistoryContextProvider
            // (the typed-client construction site) in the same change that introduces it.
            Assert.Contains(
                HttpClientSeams.DesignatedSeams, seam => seam.Contains("HistoryContextProvider", StringComparison.Ordinal));
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
        [Fact]
        public void HostContextReservationRemainsEmpty()
        {
            // The L5 tripwire's GenWave.Host.Context reservation must still match zero types after
            // this cycle builds GenWave.Context — the subsystem was born OUTSIDE Host (F105.4,
            // gh-#378), and this build is the first real test of that: scoped to just the Context
            // reservation entry (not the full HostReservedNamespaces.Entries list Story292 already
            // covers) so this fact stays about T222 specifically, not a re-run of Story292's own proof.
            var contextReservation = Assert.Single(
                HostReservedNamespaces.Entries, entry => entry.ReservedNamespace == "GenWave.Host.Context");

            var violations = HostNamespaceTripwire.FindViolations(
                ProductionAssemblies.Host.GetTypes(), [contextReservation]);

            Assert.Empty(violations);
        }
    }
}
