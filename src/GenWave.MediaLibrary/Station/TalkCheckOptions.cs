using System.ComponentModel.DataAnnotations;

namespace GenWave.MediaLibrary.Station;

/// <summary>Retention for <c>station.talk_check</c> (SPEC F220.29). Config section "TalkRules".</summary>
public sealed class TalkCheckOptions
{
    public const string Section = "TalkRules";

    /// <summary>Rows older than this many days are swept on every insert. Default 28 (the utterance table's window).</summary>
    [Range(1, 90)]
    public int CheckRetentionDays { get; set; } = 28;
}
