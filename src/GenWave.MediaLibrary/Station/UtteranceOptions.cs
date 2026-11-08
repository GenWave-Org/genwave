using System.ComponentModel.DataAnnotations;

namespace GenWave.MediaLibrary.Station;

/// <summary>Retention configuration for <c>station.utterance</c> (SPEC F218, STORY-532). Config section "Memory".</summary>
public sealed class UtteranceOptions
{
    public const string Section = "Memory";

    /// <summary>Rows older than this many days are swept on every insert. Default 28.</summary>
    [Range(1, int.MaxValue)]
    public int UtteranceRetentionDays { get; set; } = 28;
}
