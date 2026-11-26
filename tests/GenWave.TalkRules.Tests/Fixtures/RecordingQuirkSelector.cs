using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>Hands out a fixed pick and remembers each call, so a spec can see whether, and with what, the rule asked.</summary>
internal sealed class RecordingQuirkSelector(QuirkSelection pick) : IQuirkSelector
{
    public List<(long? PersonaId, ShiftKey? ShiftKey, bool Spend)> Calls { get; } = [];

    public Task<QuirkSelection> SelectAsync(long? personaId, PersonaCard? card, ShiftKey? shiftKey, bool spend, CancellationToken ct)
    {
        Calls.Add((personaId, shiftKey, spend));
        return Task.FromResult(pick);
    }
}
