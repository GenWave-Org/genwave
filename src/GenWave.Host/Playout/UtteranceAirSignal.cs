namespace GenWave.Host.Playout;

/// <summary>
/// The utterance air-stamp queue's own payload (SPEC F218.3, STORY-532, PLAN T685). A dedicated
/// record, not a bare <see langword="long"/>: <c>Channel{long}</c> and its reader/writer are already
/// registered by the enrichment and listener-request queues, and the last registration of a closed
/// generic wins, so a bare-long queue here would silently hijack theirs. Same reasoning as
/// <see cref="MediaRotationAiredSignal"/>.
/// </summary>
sealed record UtteranceAirSignal(long UtteranceId);
