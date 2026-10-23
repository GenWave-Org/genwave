namespace GenWave.VoiceOff.Serving;

/// <summary>The body of GET /wince: the reels in manifest order.</summary>
internal sealed record WinceReply(IReadOnlyList<WinceReelReply> Reels);
