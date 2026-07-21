// STORY-208 — Card export carries character, never memory
//
// BDD specification — xUnit (SPEC F79.1, F79.2). PLAN T56 adds TasteRule types and the card
// `taste[]` field to GenWave.Abstractions; these contract specs pin the serialization law the
// export/import endpoints (Story208_PersonaExport / Story209_PersonaImport, Host.Tests) rely on.

using System.Text.Json;
using GenWave.Core.Domain;

namespace GenWave.Core.Tests.Specs;

/// <summary>
/// The exact <see cref="PersonaCard"/> shape before T56 (SPEC F71.1) — no <c>taste</c> member.
/// Stands in for a station/tool that hasn't upgraded yet: <see cref="FeatureCardTasteContract.
/// ScenarioForwardCompatibleReader.TasteLessReaderShapeStillDeserializesTasteCard"/> pins that this
/// shape still deserializes a taste-bearing card's JSON without error (F79.2's forward-compat
/// promise, read in the direction of an old reader meeting new data).
/// </summary>
file sealed record PreTasteCardShape(
    int SchemaVersion,
    string Name,
    string Tagline,
    string Soul,
    IReadOnlyList<string> Quirks,
    VoiceSpec Voice,
    double EnergyDisposition,
    IReadOnlyList<string> Lore,
    IReadOnlyList<PersonaCorrection> Corrections);

public static class FeatureCardTasteContract
{
    /// <summary>
    /// A fully-populated card (every F71.1 field plus <c>taste[]</c>) shared by both scenarios below.
    /// The two <see cref="TasteRule"/>s sit at F82.1's inclusive weight edges (+1.0/-1.0) — a
    /// negative weight is a first-class dislike, not an error (SPEC F82.1) — and the Sunday-morning
    /// rule mirrors the PRD's Sunday-Zeppelin acceptance shape (day-of-week + hour gate).
    /// </summary>
    static PersonaCard BuildFullyPopulatedCardWithTaste() =>
        new(
            SchemaVersion: 1,
            Name: "DJ Nova",
            Tagline: "Late-night frequencies.",
            Soul: "Grew up chasing pirate radio signals across the north of England.",
            Quirks: ["hums between sentences", "never says the actual time"],
            Voice: new VoiceSpec(Engine: "kokoro", VoiceId: "af_heart", Pace: 1.05, Language: "en"),
            EnergyDisposition: 0.4,
            Lore: ["Started broadcasting from a garden shed in 2003."],
            Corrections: [new PersonaCorrection(From: "MacLeod", To: "muh-CLOUD")],
            Taste:
            [
                new TasteRule(
                    Predicate: new TastePredicate(Artist: "Led Zeppelin", Genre: null, Tag: null),
                    Context: new TasteContext(DaysOfWeek: [DayOfWeek.Sunday], StartHour: 6, EndHour: 12),
                    Weight: 1.0),
                new TasteRule(
                    Predicate: new TastePredicate(Artist: null, Genre: "Smooth Jazz", Tag: null),
                    Context: new TasteContext(DaysOfWeek: [], StartHour: null, EndHour: null),
                    Weight: -1.0),
            ]);

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public static class ScenarioCardWithAuthoredTaste
    {
        // Arrange (T56): a PersonaCard populated with every card field plus taste[] of
        // TasteRule{predicate, context, weight} — serialize → deserialize round-trip.

        [Fact]
        public static void CardRoundTripsByteStableWithTaste()
        {
            var json = PersonaCardSerializer.Serialize(BuildFullyPopulatedCardWithTaste());

            var card = PersonaCardSerializer.Deserialize(json);
            Assert.NotNull(card);

            Assert.Equal(json, PersonaCardSerializer.Serialize(card));
        }

        [Fact]
        public static void SchemaVersionMajorStaysOne()
        {
            var json = PersonaCardSerializer.Serialize(BuildFullyPopulatedCardWithTaste());

            var card = PersonaCardSerializer.Deserialize(json);
            Assert.NotNull(card);

            Assert.Equal(1, card.SchemaVersion);
        }

        [Fact]
        public static void TasteRuleWeightIsBoundedOnDeserialize()
        {
            var json = PersonaCardSerializer.Serialize(BuildFullyPopulatedCardWithTaste());

            var card = PersonaCardSerializer.Deserialize(json);
            Assert.NotNull(card);

            Assert.All(card.Taste ?? [], rule => Assert.InRange(rule.Weight, -1.0, 1.0));
        }
    }

    public static class ScenarioForwardCompatibleReader
    {
        // Arrange: card JSON carrying an unknown top-level field (a future minor's addition).

        /// <summary>
        /// A taste-bearing card's JSON with an extra top-level member no current
        /// <see cref="PersonaCard"/> property maps to — simulating a future minor's additive field
        /// arriving at a station that hasn't upgraded yet (F79.2).
        /// </summary>
        static string CardJsonWithUnknownTopLevelField()
        {
            var json = PersonaCardSerializer.Serialize(BuildFullyPopulatedCardWithTaste());
            return json.Insert(1, "\"futureField\":\"unrecognized-by-this-station\",");
        }

        [Fact]
        public static void UnknownFieldsAreTolerated()
        {
            var card = PersonaCardSerializer.Deserialize(CardJsonWithUnknownTopLevelField());

            Assert.NotNull(card);
        }

        [Fact]
        public static void TasteLessReaderShapeStillDeserializesTasteCard()
        {
            var json = PersonaCardSerializer.Serialize(BuildFullyPopulatedCardWithTaste());

            var card = JsonSerializer.Deserialize<PreTasteCardShape>(json, PersonaCardSerializer.Options);

            Assert.NotNull(card);
        }
    }
}
