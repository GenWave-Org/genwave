// STORY-182 — Listener-visible metadata carries no internal keys
//
// BDD specification — xUnit (SPEC F67.4). Statically pins gw_icy_song (engine/genwave.liq) as
// the StreamTitle builder wired into output.icecast, and pins its inputs to exactly
// artist/title/station_name — none of the internal metadata keys (track_id, station_id,
// on_air, on_air_timestamp, replay_gain) reach a listener-visible channel (ICY StreamTitle,
// Icecast status-page song string). AC2 (a one-time live ICY + status-page capture against
// the compose stack) is T26's wire acceptance, run once outside this suite; this guard pins
// the builder's shape thereafter so a future rename/change can't silently regress it.

using System.Text.RegularExpressions;
using Xunit;

namespace GenWave.Host.Tests.Specs;

public static class FeatureListenerMetadataDisclosure
{
    private static readonly string[] InternalMetadataKeys =
        ["track_id", "station_id", "on_air", "on_air_timestamp", "replay_gain"];

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent;

        if (dir is null) throw new InvalidOperationException("repo root (GenWave.sln) not found");
        return dir.FullName;
    }

    static string EngineScriptPath => Path.Combine(RepoRoot(), "engine", "genwave.liq");

    static string EngineScript => File.ReadAllText(EngineScriptPath);

    /// <summary>
    /// Extracts the source text of a Liquidsoap "def &lt;name&gt;(...) = ... end" block by
    /// tracking def/if/end nesting depth from the def's opening "=". A naive "stop at the next
    /// 'end'" would truncate at the first nested if/end inside the body (gw_icy_song's own
    /// artist/title fallback has one). Throws — rather than returning an empty or partial
    /// match — if the def or its matching end can't be located, so a rename or restructure of
    /// the builder breaks this spec loudly instead of the guard silently checking nothing.
    /// </summary>
    static string ExtractDefBlock(string source, string defName)
    {
        var start = Regex.Match(source, $@"\bdef\s+{Regex.Escape(defName)}\s*\([^)]*\)\s*=");
        if (!start.Success)
        {
            throw new InvalidOperationException(
                $"could not locate 'def {defName}(...)' in {EngineScriptPath} — " +
                "has the StreamTitle builder been renamed or restructured?");
        }

        var boundary = new Regex(@"\b(def|if|end)\b");
        var depth = 1; // the def itself is the first open block
        foreach (Match token in boundary.Matches(source, start.Index + start.Length))
        {
            depth += token.Value switch
            {
                "def" or "if" => 1,
                "end" => -1,
                _ => 0,
            };

            if (depth == 0)
                return source[start.Index..(token.Index + token.Length)];
        }

        throw new InvalidOperationException(
            $"found 'def {defName}(...)' in {EngineScriptPath} but no matching 'end' — " +
            "malformed Liquidsoap source, or the guard's block parser needs updating");
    }

    static string[] MetadataKeysAccessed(string block) =>
        Regex.Matches(block, "m\\[\\s*\"([^\"]+)\"\\s*\\]")
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Drops whole-line "# ..." comments before scanning for internal-key references, so a
    /// comment merely *discussing* a key (e.g. explaining why it's absent) can't produce a
    /// false failure. Liquidsoap string interpolation ("#{expr}") is untouched — this codebase
    /// only ever uses "#" as a comment marker at the start of a line, never trailing after code.
    /// </summary>
    static string StripLineComments(string block) =>
        string.Join('\n', block.Split('\n').Where(line => !line.TrimStart().StartsWith('#')));

    public static class ScenarioStreamTitleInputsPinned
    {
        [Fact]
        public static void StreamTitle_builder_consumes_only_artist_and_title()
        {
            // Given the engine's StreamTitle builder (engine/genwave.liq gw_icy_song)
            // When  its m[...] metadata accesses are enumerated by the guard
            // Then  only "artist" and "title" are consumed (F67.4)
            var block = ExtractDefBlock(EngineScript, "gw_icy_song");

            var keysAccessed = MetadataKeysAccessed(block);

            Assert.Equal(["artist", "title"], keysAccessed);
        }

        [Fact]
        public static void StreamTitle_builder_never_references_an_internal_metadata_key()
        {
            // Given the engine's StreamTitle builder (engine/genwave.liq gw_icy_song)
            // When  its full source block is scanned for the internal metadata keys
            // Then  none of track_id, station_id, on_air, on_air_timestamp, replay_gain
            //       appear anywhere in its code (comments aside) (F67.4)
            var block = StripLineComments(ExtractDefBlock(EngineScript, "gw_icy_song"));

            foreach (var internalKey in InternalMetadataKeys)
                Assert.DoesNotContain(internalKey, block, StringComparison.Ordinal);
        }

        [Fact]
        public static void Icecast_output_wires_icy_song_to_the_guarded_builder()
        {
            // Given the engine's output.icecast call
            // When  its icy_song= argument is inspected
            // Then  it points at gw_icy_song — the builder the two facts above guard — not
            //       some other, unguarded function (F67.4)
            Assert.Matches(@"icy_song\s*=\s*gw_icy_song\b", EngineScript);
        }
    }
}
