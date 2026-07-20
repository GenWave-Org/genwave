namespace GenWave.Host.Api;

/// <summary>One row of <c>GET /api/tts/corrections-stats</c> (SPEC F68.7): a rule's fired count
/// since process start.</summary>
public sealed record CorrectionStatDto(string From, long Fired);
