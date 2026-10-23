namespace GenWave.VoiceOff.Listening;

/// <summary><c>session/sessions.json</c>, written by <c>pair</c> and served to the page. It carries nothing but ids and clip paths.</summary>
internal sealed record SessionFile(int Cap, IReadOnlyList<SessionEntry> Sessions);
