// STORY-050 — Re-enrichment contract (IAdminMediaReenrichment + ReenrichFields)
//
// BDD specification — xUnit. Pure reflection over Core (no I/O).
// Mirrors the Story046 LibraryWriteContract reflection style.

using System.Reflection;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Core.Tests.Specs;

public static class FeatureReenrichmentContract
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioReenrichFieldsFlagsEnum
    {
        [Fact]
        public void IsDecoratedWithFlagsAttribute()
        {
            Assert.NotNull(typeof(ReenrichFields).GetCustomAttribute<FlagsAttribute>());
        }

        [Fact]
        public void DefinesNoneCueEnergyLoudnessAndTags()
        {
            Assert.Equal(0, (int)ReenrichFields.None);
            Assert.Equal(1, (int)ReenrichFields.Cue);
            Assert.Equal(2, (int)ReenrichFields.Energy);
            Assert.Equal(4, (int)ReenrichFields.Loudness);
            Assert.Equal(8, (int)ReenrichFields.Tags);
        }

        [Fact]
        public void DefinesAllAsTheBitwiseUnionOfTheSixFields()
        {
            // xUnit2000: constant/literal goes first (expected); computed union is the actual.
            // Updated by X3 (SPEC F46.4): Bpm joined the union alongside its four siblings.
            // Updated by X5 (SPEC F48.6): Year joined alongside its five siblings.
            Assert.Equal(
                ReenrichFields.All,
                ReenrichFields.Cue | ReenrichFields.Energy | ReenrichFields.Loudness | ReenrichFields.Tags
                    | ReenrichFields.Bpm | ReenrichFields.Year);
        }
    }

    public sealed class ScenarioIAdminMediaReenrichmentContractLivesInCore
    {
        [Fact]
        public void TypeIsInCoreAbstractionsAndIsAnInterface()
        {
            Assert.Equal("GenWave.Core.Abstractions", typeof(IAdminMediaReenrichment).Namespace);
            Assert.True(typeof(IAdminMediaReenrichment).IsInterface);
        }

        [Fact]
        public void ScheduleAsyncSignatureMatchesIdFieldsScopeCancellationToken()
        {
            var m = typeof(IAdminMediaReenrichment).GetMethod("ScheduleAsync")!;
            Assert.Equal(typeof(Task<ReenrichResult>), m.ReturnType);
            var p = m.GetParameters();
            Assert.Equal(4, p.Length);
            Assert.Equal(typeof(string), p[0].ParameterType);
            Assert.Equal(typeof(ReenrichFields), p[1].ParameterType);
            Assert.Equal(typeof(LibraryScope), p[2].ParameterType);
            Assert.Equal(typeof(CancellationToken), p[3].ParameterType);
        }

        [Fact]
        public void ScheduleBulkAsyncSignatureMatchesFilterFieldsScopeCancellationToken()
        {
            var m = typeof(IAdminMediaReenrichment).GetMethod("ScheduleBulkAsync")!;
            Assert.Equal(typeof(Task<int>), m.ReturnType);
            var p = m.GetParameters();
            Assert.Equal(4, p.Length);
            Assert.Equal(typeof(MediaQuery), p[0].ParameterType);
            Assert.Equal(typeof(ReenrichFields), p[1].ParameterType);
            Assert.Equal(typeof(LibraryScope), p[2].ParameterType);
            Assert.Equal(typeof(CancellationToken), p[3].ParameterType);
        }
    }

    public sealed class ScenarioReenrichResultDiscriminatesOutcomes
    {
        // SPEC F43.3 (Epic V, closes gitea-#203) supersedes this fact: OutOfScope was retired from the
        // single-row reenrich path (scope is a curation filter, not an access gate) and, being
        // fully unused once bulk reenrich (which never returned ReenrichResult) is accounted for,
        // was deleted from the enum outright rather than kept as a dead member.
        [Fact]
        public void DistinguishesScheduledNotFound()
        {
            var names = Enum.GetNames<ReenrichResult>().ToHashSet();
            Assert.Contains("Scheduled", names);
            Assert.Contains("NotFound", names);
        }
    }
}
