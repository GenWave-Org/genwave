namespace GenWave.VoiceOff.Serving;

/// <summary>The body of POST /wince. Read with <see cref="Listening.StrictJson"/>, so an unknown member or a non-number count is refused.</summary>
internal sealed record WinceRequest(string ReelId, int Count);
