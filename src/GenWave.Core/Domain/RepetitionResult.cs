namespace GenWave.Core.Domain;

/// <summary>Outcome of <see cref="Abstractions.IRepetitionCheck"/>.</summary>
public sealed record RepetitionResult(IReadOnlyList<RepetitionHit> Hits)
{
    /// <summary>Nothing repeated.</summary>
    public static RepetitionResult None { get; } = new([]);

    /// <summary>True when at least one phrase repeated.</summary>
    public bool IsHit => Hits.Count > 0;
}
