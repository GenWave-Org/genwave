namespace GenWave.VoiceOff.Listening;

/// <summary>One listening session of <c>sessions.json</c>: ids are "1", "2", and so on.</summary>
internal sealed record SessionEntry(string Id, IReadOnlyList<SessionPair> Pairs);
