using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>Answers one card (or none) for any persona id.</summary>
internal sealed class OneCardSource(PersonaCard? card) : IPersonaCardByIdSource
{
    public Task<PersonaCard?> GetCardAsync(long personaId, CancellationToken ct) => Task.FromResult(card);
}
