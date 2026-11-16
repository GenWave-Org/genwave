namespace GenWave.Core.Domain;

/// <summary>One extracted personal claim a persona keeps (SPEC F218.18): a <c>life</c> row, source <c>extracted</c>.</summary>
/// <param name="PersonaId">The persona whose life it is.</param>
/// <param name="Content">The claim, phrased from that persona's side.</param>
public sealed record NewLifeClaim(long PersonaId, string Content);
