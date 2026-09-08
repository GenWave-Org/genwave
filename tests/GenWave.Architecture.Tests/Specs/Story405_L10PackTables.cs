// STORY-405 — L10 knows the new pack tables (SPEC F170.3 · PLAN T428)

using GenWave.Architecture.Tests.Support;

namespace GenWave.Architecture.Tests.Specs;

/// <summary>
/// STORY-405 AC3 and SPEC F170.3 both say "L10's root list gains station.jingle_pack,
/// station.voice_pack, station.voice_pack_voice" — but L10's real root list
/// (<see cref="FeatureNamespaceCycleFreedom.ProjectRoots"/>) enumerates NAMESPACES, so a table
/// name added there would be a vacuous pin that asserts a string sits in a list the check never
/// reads. This class pins what AC3 is actually reaching for instead: the three tables genuinely
/// exist on the station_svc side of the db/22 role boundary, and the namespace-cycle law's own
/// root list already covers every project this epic touched.
/// </summary>
public static class FeatureL10RootListKnowsThePackTables
{
    static string ReadDb45Text()
        => File.ReadAllText(Path.Combine(SolutionLocator.Root(), "db", "45-jingle-voice-pack-migration.sh"));

    public sealed class ScenarioTheThreePackTablesLiveInTheStationSchema
    {
        [Fact]
        public void Db45CreatesStationJinglePack()
            => Assert.Contains(
                "create table if not exists station.jingle_pack (",
                ReadDb45Text(),
                StringComparison.Ordinal);

        [Fact]
        public void Db45CreatesStationVoicePack()
            => Assert.Contains(
                "create table if not exists station.voice_pack (",
                ReadDb45Text(),
                StringComparison.Ordinal);

        [Fact]
        public void Db45CreatesStationVoicePackVoice()
            => Assert.Contains(
                "create table if not exists station.voice_pack_voice (",
                ReadDb45Text(),
                StringComparison.Ordinal);
    }

    public sealed class ScenarioEachTableFollowsTheDb22RoleBoundary
    {
        [Fact]
        public void AllThreePackTablesAreCreatedWhileActingAsStationSvcNeverLibrarySvc()
        {
            // db/22's own boundary rule ("station_svc has no grant into the library schema,
            // library_svc none into station") is enforced in db/45 not by a literal GRANT line —
            // there is none — but by `set role`: everything created between the `set role
            // station_svc;` switch and the next `set role library_svc;` switch runs, and is
            // therefore owned, as station_svc. Pinning the real mechanism, not the wording AC3
            // assumed.
            var text = ReadDb45Text();

            var stationRoleIndex = text.IndexOf("set role station_svc;", StringComparison.Ordinal);
            var libraryRoleIndex = text.IndexOf("set role library_svc;", StringComparison.Ordinal);
            Assert.True(stationRoleIndex >= 0, "db/45 no longer sets role station_svc before the pack tables.");
            Assert.True(libraryRoleIndex > stationRoleIndex, "db/45's library_svc switch must follow its station_svc block.");

            foreach (var createStatement in new[]
            {
                "create table if not exists station.jingle_pack (",
                "create table if not exists station.voice_pack (",
                "create table if not exists station.voice_pack_voice (",
            })
            {
                var statementIndex = text.IndexOf(createStatement, StringComparison.Ordinal);
                Assert.InRange(statementIndex, stationRoleIndex, libraryRoleIndex);
            }
        }
    }

    public sealed class ScenarioTheArchitectureSuiteKnowsEveryProjectTheEpicTouched
    {
        [Fact]
        public void L10SweepsEveryProjectTheEpicTouched()
        {
            // Replaces the original "the L1-L10 suite runs green after the pin" fact: that claim
            // is already covered by every other law's own theory data. What this epic actually
            // needs pinned is narrower — the namespace-cycle law's root list still names every
            // project the epic touched, so a future edit that quietly drops one of them from
            // FeatureNamespaceCycleFreedom.ProjectRoots goes red here.
            IEnumerable<string> roots = FeatureNamespaceCycleFreedom.ProjectRoots;

            foreach (var projectTheEpicTouched in new[]
            {
                "GenWave.Core",
                "GenWave.MediaLibrary",
                "GenWave.Host",
                "GenWave.Ads",
                "GenWave.Tts",
                "GenWave.Loudness",
            })
            {
                Assert.Contains(projectTheEpicTouched, roots);
            }
        }
    }
}
