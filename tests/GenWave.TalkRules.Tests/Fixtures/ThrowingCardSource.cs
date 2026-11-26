using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>A card source whose store is down.</summary>
internal sealed class ThrowingCardSource : IPersonaCardByIdSource
{
    public Task<PersonaCard?> GetCardAsync(long personaId, CancellationToken ct) =>
        throw new InvalidOperationException("fixture card store failure");
}
