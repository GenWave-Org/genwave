// gh-#150 — personas: DJs should occasionally mention their own name.
//
// BDD specification — xUnit. Real radio DJs occasionally say their own name on air. On a
// SelfNameMentionProbability fraction of persona-voiced breaks, the persona section carries one
// extra instruction line asking the DJ to work their own name in naturally. The roll itself is
// taken at the call site (LlmCopyWriter) — the builder stays pure — so these specs drive the
// mentionOwnName parameter directly and pin the persona gate: no persona, no line, whatever the
// roll said.

using GenWave.Core.Domain;

namespace GenWave.Tts.Tests.Specs;

public static class FeatureDjSelfNameMention
{
    static Persona BuildPersona() => new(1, "DJ Nova", "", "", "", DateTime.UtcNow, DateTime.UtcNow);

    static PersonaCard BuildCard(string soul = "A washed-up 90s radio jock chasing one more big break.") =>
        new(
            SchemaVersion: 1,
            Name: "DJ Nova",
            Tagline: "",
            Soul: soul,
            Quirks: [],
            Voice: new VoiceSpec(Engine: "", VoiceId: "", Pace: 1.0, Language: "en"),
            EnergyDisposition: 0,
            Lore: [],
            Corrections: []);

    public static class ScenarioRolledTrueOnAPersonaVoicedBreak
    {
        [Fact]
        public static void The_section_asks_the_dj_to_work_their_own_name_in()
        {
            // Given a persona-voiced break whose roll came up true
            var section = LlmPromptBuilder.BuildPersonaSection(BuildPersona(), BuildCard(), mentionOwnName: true);

            // Then the name line rides beneath the persona section, naming the DJ
            Assert.NotNull(section);
            Assert.Contains("Name note: your on-air name is DJ Nova", section);
            Assert.Contains("work your own name", section);
        }
    }

    public static class ScenarioRolledFalseOnAPersonaVoicedBreak
    {
        [Fact]
        public static void The_section_carries_no_name_line()
        {
            // Given the same persona on a break whose roll came up false
            var section = LlmPromptBuilder.BuildPersonaSection(BuildPersona(), BuildCard(), mentionOwnName: false);

            // Then the section is untouched — no name line
            Assert.NotNull(section);
            Assert.DoesNotContain("Name note:", section);
        }
    }

    public static class ScenarioNoPersonaNeverCarriesTheLine
    {
        [Fact]
        public static void No_persona_yields_no_line_regardless_of_the_roll()
        {
            // Given no persona at all, on a rolled-true break
            var section = LlmPromptBuilder.BuildPersonaSection(persona: null, card: null, mentionOwnName: true);

            // Then there is no section for the line to ride on
            Assert.Null(section);
        }

        [Fact]
        public static void A_persona_with_nothing_to_show_stays_neutral_even_when_rolled_true()
        {
            // Given a named persona whose soul and quirks are both empty — the "neutral otherwise"
            // half of F35.2: such a persona falls back to the neutral scaffold, and the name line
            // is a rider on an actual persona section, never a section by itself
            var section = LlmPromptBuilder.BuildPersonaSection(BuildPersona(), BuildCard(soul: ""), mentionOwnName: true);

            Assert.Null(section);
        }
    }
}
