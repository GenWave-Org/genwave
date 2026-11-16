namespace GenWave.Memory;

/// <summary>One claim the model pulled out of a copy (SPEC F218.17): its text and who it is about.</summary>
/// <param name="Claim">The first-person personal claim, one line.</param>
/// <param name="About"><c>self</c> or the name of another persona the claim involves; blank reads as self.</param>
public sealed record ExtractedClaim(string Claim, string About);
