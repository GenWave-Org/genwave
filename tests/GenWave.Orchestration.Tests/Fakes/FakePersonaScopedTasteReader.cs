using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// <see cref="IPersonaTasteReader"/> double keyed by persona id (STORY-241, PLAN T120) — unlike
/// <see cref="FakePersonaTasteReader"/> (a single fixed rule set for whichever persona id asks), this
/// one hands back a DIFFERENT rule set per persona, so a spec proving "the ranker's rung 0 observes
/// whichever persona is on air right now" can tell the two personas' picks apart by which taste rule
/// fired. An id with no registered rules resolves to an empty rule set (a persona-off/no-opinion
/// answer, never a fault).
/// </summary>
sealed class FakePersonaScopedTasteReader : IPersonaTasteReader
{
    readonly Dictionary<long, IReadOnlyList<TasteRule>> rulesByPersonaId = [];

    public void SetRules(long personaId, IReadOnlyList<TasteRule> rules) => rulesByPersonaId[personaId] = rules;

    public Task<IReadOnlyList<PersonaTasteEntry>> ListAsync(long personaId, PersonaTasteSource? source, CancellationToken ct)
    {
        var rules = rulesByPersonaId.TryGetValue(personaId, out var found) ? found : [];
        IReadOnlyList<PersonaTasteEntry> entries = rules
            .Select(rule => new PersonaTasteEntry(
                Id: 0,
                PersonaId: personaId,
                Rule: rule,
                Source: PersonaTasteSource.Authored,
                CreatedAt: DateTime.UnixEpoch,
                UpdatedAt: DateTime.UnixEpoch))
            .ToList();
        return Task.FromResult(entries);
    }
}
