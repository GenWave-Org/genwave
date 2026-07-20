using System.ComponentModel.DataAnnotations;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// Retention configuration for accrued persona memory (SPEC F71.6, STORY-194): a per-(persona, kind)
/// cap on <c>accrued</c> rows only — <c>authored</c> rows are exempt from both the cap count and its
/// eviction, so an operator's hand-written bits/callbacks are never silently deleted by the accrual
/// mill. Config section "Persona:Memory".
/// </summary>
public sealed class PersonaMemoryOptions
{
    public const string Section = "Persona:Memory";

    /// <summary>
    /// Max accrued rows retained per (persona, kind); the oldest beyond this many are evicted in the
    /// same transaction as the newest insert (<see cref="PersonaMemoryRepository.RecordAsync"/>).
    /// </summary>
    [Range(1, int.MaxValue)]
    public int CapPerKind { get; set; } = 50;
}
