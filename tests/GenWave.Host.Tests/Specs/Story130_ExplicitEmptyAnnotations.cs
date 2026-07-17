// STORY-130 — Output metadata never carries a stale artist (Epic U / SPEC F38, closes gitea-#199)
//
// BDD specification — xUnit. Authored PENDING at /plan time (2026-07-13, house rule since Epic S).
// Grounding (source-verified against the pinned Liquidsoap v2.4.4 tag, MEMORY.md 2026-07-13): the
// telnet metadata ring never merges keys — the bleed carrier is key ABSENCE plus file-tag fill at
// request resolution; a present-but-empty key blocks the fill and propagates end-to-end. U2 makes
// the builder's artist field unconditional; these facts pin the byte shape. The live
// reproduce-then-cure proof is U1(a)/U7(a) in Story133.

using GenWave.Core.Domain;
using GenWave.Host.Engine;

namespace GenWave.Host.Tests.Specs;

public static class FeatureExplicitEmptyAnnotations
{
    static readonly GenWave.Core.Domain.Loudness DefaultLoudness = new(-16.0, -1.0, Measurable: true);

    /// <summary>Repo root, resolved relative to the test assembly's build output (Story074/102/107's convention).</summary>
    static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    static string EngineScriptText =>
        File.ReadAllText(Path.Combine(RepoRoot, "engine", "genwave.liq"));

    public sealed class ScenarioArtistIsAlwaysStamped
    {
        [Fact]
        public void AnItemWithAnArtistCarriesItInTheAnnotation()
        {
            // F38.1 — a non-empty catalog artist is stamped verbatim (escaped).
            var item = new MediaItem("1", "/media/1.mp3", "Title", DefaultLoudness, Artist: "The Band");

            var result = LiquidsoapAnnotationBuilder.Build(item, 0.0, "st-01", "GenWave");

            Assert.Contains("artist=\"The Band\"", result, StringComparison.Ordinal);
        }

        [Fact]
        public void AnItemWithNullArtistCarriesAnExplicitlyEmptyArtistField()
        {
            // F38.1 — Artist=null now stamps artist="" instead of omitting the key entirely, ending
            // the omit-when-empty discipline that let a prior track's file tag fill the gap (gitea-#199).
            var item = new MediaItem("2", "/media/2.mp3", "Title", DefaultLoudness, Artist: null);

            var result = LiquidsoapAnnotationBuilder.Build(item, 0.0, "st-01", "GenWave");

            Assert.Contains("artist=\"\",", result, StringComparison.Ordinal);
        }

        [Fact]
        public void AnItemWithWhitespaceArtistCarriesAnExplicitlyEmptyArtistField()
        {
            // F38.1 — whitespace-only is treated the same as null (IsNullOrWhiteSpace), not stamped
            // as literal whitespace.
            var item = new MediaItem("3", "/media/3.mp3", "Title", DefaultLoudness, Artist: "   ");

            var result = LiquidsoapAnnotationBuilder.Build(item, 0.0, "st-01", "GenWave");

            Assert.Contains("artist=\"\",", result, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioNumericFieldsKeepOmitWhenEmpty
    {
        [Fact]
        public void NullCueOmitsBothLiqCueFields()
        {
            // F38.2 — numeric engine-consumed fields stay omit-when-empty: absence is the engine's
            // documented fallback signal, and an empty string is not a parseable float.
            var item = new MediaItem("4", "/media/4.mp3", "Title", DefaultLoudness, Cue: null);

            var result = LiquidsoapAnnotationBuilder.Build(item, 0.0, "st-01", "GenWave");

            Assert.DoesNotContain("liq_cue_in", result, StringComparison.Ordinal);
            Assert.DoesNotContain("liq_cue_out", result, StringComparison.Ordinal);
        }

        [Fact]
        public void NullEnergiesOmitBothGwEnergyFields()
        {
            // F38.2 — same fallback-signal contract for energy fields.
            var item = new MediaItem("5", "/media/5.mp3", "Title", DefaultLoudness,
                IntroEnergy: null, OutroEnergy: null);

            var result = LiquidsoapAnnotationBuilder.Build(item, 0.0, "st-01", "GenWave");

            Assert.DoesNotContain("gw_intro_energy", result, StringComparison.Ordinal);
            Assert.DoesNotContain("gw_outro_energy", result, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioTheFeederTreatsPresentButEmptyAsAbsent
    {
        [Fact]
        public void AnEmptyArtistValueParsesAsNull()
        {
            // F38.4 — the feeder's shipped parse (a.Length > 0) already treats present-but-empty as
            // absent; this is the shipped behavior, now exercised by the new always-stamped shape.
            var meta = new EngineMetadata(new Dictionary<string, string>
            {
                ["title"] = "Quiet Track",
                ["artist"] = string.Empty,
                ["replay_gain"] = "0.00 dB",
            });

            var (_, artist, _) = meta.ExtractAnnotations();

            Assert.Null(artist);
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioEscapingHoldsOnTheUnconditionalField
    {
        [Fact]
        public void AQuoteBearingArtistIsEscapedInTheAnnotation()
        {
            // F38.1's AC4 — the unconditional field still runs through the shipped Escape rules:
            // backslashes double up, quotes are backslash-escaped.
            var item = new MediaItem("6", "/media/6.mp3", "Title", DefaultLoudness,
                Artist: "Guns N\" Roses");

            var result = LiquidsoapAnnotationBuilder.Build(item, 0.0, "st-01", "GenWave");

            Assert.Contains("artist=\"Guns N\\\" Roses\"", result, StringComparison.Ordinal);
        }

        [Fact]
        public void ANewlineBearingArtistCannotSplitTheTelnetCommand()
        {
            // F38.1's AC4 — CR/LF in an artist value are replaced with spaces so the always-present
            // field cannot split the single-line telnet command.
            var item = new MediaItem("7", "/media/7.mp3", "Title", DefaultLoudness,
                Artist: "Line1\nLine2\rEnd");

            var result = LiquidsoapAnnotationBuilder.Build(item, 0.0, "st-01", "GenWave");

            Assert.DoesNotContain('\n', result);
            Assert.DoesNotContain('\r', result);
        }
    }

    public sealed class ScenarioNoMetadataMapEntersTheEngineScript
    {
        [Fact]
        public void TheEngineScriptContainsNoMetadataMap()
        {
            // U7-converted (2026-07-13) — real, always-run, non-Skip repo-content assertion
            // (Story102/107/S8/T11's grep-assert idiom), no live stack needed. F38.4 house rule:
            // metadata.map's strip=true packet deletion is the spike-(a) freeze mechanism
            // (source-verified 2026-07-13, MEMORY.md) — Epic U's engine.liq diff never introduced
            // one (also pinned independently in Story133's engine-diff regression fact).
            Assert.DoesNotContain("metadata.map", EngineScriptText, StringComparison.Ordinal);
        }
    }
}
