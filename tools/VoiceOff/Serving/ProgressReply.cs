namespace GenWave.VoiceOff.Serving;

/// <summary>The answer to GET /progress.</summary>
internal sealed record ProgressReply(int Done, int Cap);
