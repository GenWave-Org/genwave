// gh-#291 — matched dislikes stay in the pick diagnostics
//
// BDD specification — xUnit. The chosen gh-#291 shape, pinned from the ranker side: PersonaRanker
// keeps EVERY matched rule in PickResult.FiredRules regardless of weight sign. The booth-log pick
// stamp persists each fired rule's signed weight (SPEC F86.1, Story217 pins a -0.3 rule riding the
// stamp) and the admin UI chips render that sign — a fired dislike is honest, consumed diagnostic
// data. The prompt seam (GenWave.Tts.LlmPromptBuilder.DescribeFiredRules, pinned in
// Gh291_DislikeTasteColor) is the ONLY place dislikes are kept out of spoken color — never here.

using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Abstractions.Playout;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureFiredRulesKeepDislikes
{
    public static class ScenarioDislikedButWinningPick
    {
        // Arrange (Story213's shapes): a dislike rule matches the pool's only candidate, which
        // therefore wins anyway; exploration roll 0.99 stays above the floor (never exploration).
        static readonly TasteContext AnyTime = new(DaysOfWeek: [], StartHour: null, EndHour: null);

        static readonly TasteRule DislikeRule = new(
            new TastePredicate(Artist: "Nickelback", Genre: null, Tag: null), AnyTime, Weight: -0.8);

        static async Task<PickResult?> PickAsync()
        {
            var disliked = new PersonaRankCandidate(
                MediaId: "disliked", Artist: "Nickelback", Genre: null, Moods: [], Energy: 0.5, RotationScore: 1.0);
            var ranker = new PersonaRanker(
                new FakePersonaTasteReader([DislikeRule]), new StubRandomSource(0.99),
                TimeProvider.System, new PersonaRankerOptions(), NullLogger<PersonaRanker>.Instance);

            return await ranker.PickAsync(
                personaId: 1, energyDisposition: 0.0, new EnergyRange(0.0, 1.0), [disliked], CancellationToken.None);
        }

        [Fact]
        public static async Task TheMatchedDislikeRidesFiredRules()
        {
            var result = await PickAsync();

            Assert.NotNull(result);
            Assert.Contains(DislikeRule, result.FiredRules);
        }
    }
}
