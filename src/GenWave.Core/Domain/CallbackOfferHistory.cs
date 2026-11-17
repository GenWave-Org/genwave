namespace GenWave.Core.Domain;

/// <summary>
/// How long ago a persona last asked for a callback (SPEC F218.44), on the store's clock. Null = never.
/// </summary>
/// <param name="SinceLastUsed">Elapsed since the last ask whose line carried the memory.</param>
/// <param name="SinceLastAsk">Elapsed since the last ask of any outcome.</param>
public sealed record CallbackOfferHistory(TimeSpan? SinceLastUsed, TimeSpan? SinceLastAsk);
