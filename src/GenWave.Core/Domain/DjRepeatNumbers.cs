namespace GenWave.Core.Domain;

/// <summary>
/// One DJ's repeat numbers over the last 7 days (SPEC F218.29, F218.38): every repeat caught, how many of
/// those were fixed by cutting a sentence (<see cref="Trims"/>, the DJ kept), and how many lines aired as a
/// stock template instead (<see cref="Fallbacks"/>).
/// </summary>
public sealed record DjRepeatNumbers(long PersonaId, string PersonaName, int Catches, int Trims, int Fallbacks);
