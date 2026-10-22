namespace GenWave.VoiceOff.Listening;

/// <summary>One line of <c>winces.jsonl</c>: Dean's wince count for a reel. The last line per reelId counts.</summary>
internal sealed record WinceEntry(string ReelId, int Count, DateTimeOffset At);
