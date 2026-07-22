using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// <see cref="IPersonaTasteReader"/> double (STORY-213) — hands back a fixed set of
/// <see cref="TasteRule"/>s wrapped as <see cref="PersonaTasteEntry"/> rows, all attributed to the
/// requesting persona id and <see cref="PersonaTasteSource.Authored"/> (the source itself is not a
/// <see cref="PersonaRanker"/> scoring input, so which one is used here doesn't matter to any spec).
/// Wraps the exact <see cref="TasteRule"/> instances passed in — never a re-serialized copy — so a
/// spec that asserts on <see cref="PickResult.FiredRules"/> can rely on reference identity rather
/// than <see cref="TasteRule"/> equality, which its <see cref="IReadOnlyList{T}"/> fields don't
/// support structurally (T59 review caution).
/// </summary>
public sealed class FakePersonaTasteReader(IReadOnlyList<TasteRule> rules) : IPersonaTasteReader
{
    public Task<IReadOnlyList<PersonaTasteEntry>> ListAsync(long personaId, PersonaTasteSource? source, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PersonaTasteEntry>>(rules
            .Select(rule => new PersonaTasteEntry(
                Id: 0,
                PersonaId: personaId,
                Rule: rule,
                Source: PersonaTasteSource.Authored,
                CreatedAt: DateTime.UnixEpoch,
                UpdatedAt: DateTime.UnixEpoch))
            .ToList());
}
