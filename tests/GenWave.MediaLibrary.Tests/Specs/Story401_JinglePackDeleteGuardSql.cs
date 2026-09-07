// STORY-401 — the jingle-pack uninstall guard, structurally (SPEC F165.6 · PLAN T414)
//
// Story401_VoicePackDeleteGuardSql.cs (one repository over) pins a SINGLE guarded DELETE statement,
// because station.voice_pack and its own referencing tables all live behind the SAME station_svc
// role. A jingle pack cannot follow that shape: the rows a delete removes are library.media rows
// (library_svc), the reference it must refuse on is an active station.ad_spot.bed_media_id
// (station_svc), and no cross-schema grant exists between the two roles (db/42's own header
// remarks; db/45's own header remarks on this exact column pair — both cited in full on
// JinglePackUpsertResult's and JinglePackDeleteResult's own "Honest boundary" remarks). There is
// therefore no single statement to pin the way the voice-pack fact does. What this file pins
// instead: JinglePackRepository.ActiveBedReferencesSql is the ONE place the guard's own reference
// check lives (both UpsertAsync and DeleteAsync call it — no inline second copy), and neither of the
// two delete statements it gates (DeleteMediaSql, DeletePackSql) carries a guard clause of its own —
// proving the guard was never quietly duplicated, or worse, silently dropped, into either delete.

using GenWave.MediaLibrary.Station;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureJinglePackDeleteGuardSql
{
    public sealed class ScenarioTheGuardLivesInOnePlace
    {
        // Given JinglePackRepository's own guard-read text, When it is inspected directly.
        [Fact]
        public void TheGuardReadChecksBedMediaIdAgainstTheActiveAdSpotStateSet() =>
            Assert.Contains(
                "s.bed_media_id = any(@mediaIds) and s.state in ('draft', 'approved', 'rendering', 'ready')",
                JinglePackRepository.ActiveBedReferencesSql, StringComparison.Ordinal);

        [Fact]
        public void ItContainsExactlyOneReferenceToStationAdSpot() =>
            Assert.Equal(1, Occurrences(JinglePackRepository.ActiveBedReferencesSql, "station.ad_spot"));

        // Given the two delete statements the guard-read above gates, When they are inspected
        // directly, Then neither carries its own inline guard clause — a mutation that tried to
        // "helpfully" embed a cross-schema condition into either would not even be valid SQL against
        // library_svc's own grants, but a mutation that quietly DROPPED the shared guard-read call and
        // replaced it with an unconditional delete would leave both delete statements looking
        // unchanged; this fact instead pins that the delete statements stay guard-free, so the ONLY
        // place left for a guard to be missing from is the shared call site itself — the one this
        // file's other facts, plus Story399_JinglePackInstall.cs's/Story401_PackUninstallGuards.cs's
        // own real-Postgres behavioral facts, already cover.
        [Fact]
        public void NeitherDeleteStatementCarriesItsOwnGuardClause()
        {
            Assert.DoesNotContain("not exists", JinglePackRepository.DeleteMediaSql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("not exists", JinglePackRepository.DeletePackSql, StringComparison.OrdinalIgnoreCase);
        }

        // Given JinglePackRepository.cs's own DeleteAsync method body, When its source text is read
        // directly (the Story129_SafePathLevelMatching.cs RepoRoot idiom, applied to C# source rather
        // than a Liquidsoap script) — Then the guard-read call appears BEFORE either delete statement
        // is issued. Behavioral facts alone (Story401_PackUninstallGuards.cs's real-Postgres arc)
        // cannot tell "checked, then deleted" apart from "deleted, then checked" when both still land
        // on the same 409/204 outcome for a reference inserted well before DELETE is ever called. This
        // fact pins DeleteAsync's OWN internal ordering only — the separate restore-before-delete
        // ordering inside JinglePackController.UnwindWritten is pinned by its own text-pin fact in
        // Host.Tests (FeatureJinglePackControllerUnwindOrdering, Story399_JinglePackInstall.cs), next
        // to that method's own source, following the same idiom.
        [Fact]
        public void DeleteAsyncChecksActiveBedReferencesBeforeDeletingEitherRow()
        {
            var body = DeleteAsyncBodyText();
            var guardIndex = body.IndexOf("ActiveBedReferencesSql", StringComparison.Ordinal);
            var mediaDeleteIndex = body.IndexOf("DeleteMediaSql", StringComparison.Ordinal);
            var packDeleteIndex = body.IndexOf("DeletePackSql", StringComparison.Ordinal);

            Assert.True(guardIndex >= 0, "ActiveBedReferencesSql not referenced in DeleteAsync's own body");
            Assert.True(mediaDeleteIndex >= 0, "DeleteMediaSql not referenced in DeleteAsync's own body");
            Assert.True(packDeleteIndex >= 0, "DeletePackSql not referenced in DeleteAsync's own body");
            Assert.True(guardIndex < mediaDeleteIndex, "guard-read must precede the library.media delete");
            Assert.True(guardIndex < packDeleteIndex, "guard-read must precede the station.jingle_pack delete");
        }
    }

    static string DeleteAsyncBodyText()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var sourceText = File.ReadAllText(Path.Combine(
            repoRoot, "src", "GenWave.MediaLibrary", "Station", "JinglePackRepository.cs"));

        const string signature = "public async Task<JinglePackDeleteResult> DeleteAsync(";
        var signatureStart = sourceText.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(signatureStart >= 0, "DeleteAsync method signature not found in JinglePackRepository.cs");

        var bodyOpenBrace = sourceText.IndexOf('{', signatureStart + signature.Length);
        Assert.True(bodyOpenBrace >= 0, "DeleteAsync's own opening brace not found in JinglePackRepository.cs");

        // Brace-match from the signature's own first '{' to its OWN matching '}' (mirrors
        // Story399_JinglePackInstall.cs's own UnwindWrittenBodyText idiom, T414 review round 3 finding
        // 2) — a plain sourceText[start..] slice to EOF bound today only because DeleteAsync happens to
        // be the LAST member declared in JinglePackRepository.cs; any member declared after it would
        // vacuously satisfy the ">= 0" guards above without pinning a single reference inside
        // DeleteAsync's own body.
        var depth = 0;
        var bodyCloseBrace = -1;
        for (var i = bodyOpenBrace; i < sourceText.Length; i++)
        {
            if (sourceText[i] == '{')
                depth++;
            else if (sourceText[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    bodyCloseBrace = i;
                    break;
                }
            }
        }
        Assert.True(bodyCloseBrace > bodyOpenBrace, "DeleteAsync's own closing brace not found in JinglePackRepository.cs");

        return sourceText[bodyOpenBrace..(bodyCloseBrace + 1)];
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
