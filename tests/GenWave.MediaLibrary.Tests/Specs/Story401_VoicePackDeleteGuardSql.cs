// STORY-401 — the voice-pack uninstall DELETE carries its own guard, structurally (SPEC F164.6 · PLAN T413)
//
// Pure text assertion, no Postgres needed (the RotFindingRepository/Story378 "join stays on the
// library side" idiom, one repository over): tests/GenWave.Host.Tests/Specs/Story401_PackUninstallGuards.cs
// proves the GUARD BEHAVIOUR end to end against real Postgres, but every one of its facts inserts the
// referencing row BEFORE calling DELETE — a mutation that replaces the single guarded DELETE with an
// advisory pre-`SELECT count(*)` plus an unguarded DELETE would still pass every one of those facts
// (T413 review round 1 finding F3). This file pins the STATEMENT ITSELF instead:
// VoicePackRepository.DeleteGuardSql is the ONE place the guard text lives (DeleteAsync references it
// directly, never a second inline copy), so a mutation weakening the guard has nowhere else to hide.

using GenWave.MediaLibrary.Station;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureVoicePackDeleteGuardSql
{
    public sealed class ScenarioTheGuardIsPartOfTheSingleDeleteStatement
    {
        // Given VoicePackRepository's own DELETE guard text, When it is inspected directly.
        [Fact]
        public void ItContainsExactlyOneDeleteFromStationVoicePack() =>
            Assert.Equal(1, Occurrences(VoicePackRepository.DeleteGuardSql, "delete from station.voice_pack"));

        [Fact]
        public void ItContainsExactlyTwoNotExistsClauses() =>
            Assert.Equal(2, Occurrences(VoicePackRepository.DeleteGuardSql, "not exists"));

        [Fact]
        public void OneNotExistsClauseGuardsAgainstAnActiveAdSpot() =>
            Assert.Contains("station.ad_spot", VoicePackRepository.DeleteGuardSql, StringComparison.Ordinal);

        [Fact]
        public void TheOtherNotExistsClauseGuardsAgainstAPersonaReference() =>
            Assert.Contains("station.persona", VoicePackRepository.DeleteGuardSql, StringComparison.Ordinal);
    }

    static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
