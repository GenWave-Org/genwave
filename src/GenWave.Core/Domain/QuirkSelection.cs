namespace GenWave.Core.Domain;

/// <summary>
/// What <see cref="Abstractions.IQuirkSelector"/> picked for one break (SPEC F71.3, F218.7).
/// </summary>
/// <param name="Lines">The 2-3 quirk texts to show the model, in card order.</param>
/// <param name="CappedLines">
/// The subset of <paramref name="Lines"/> that are <c>maxPerShift</c> quirks chosen this break. On
/// air they are spent (rows written before the selector returns); on the preview path
/// (<c>spend: false</c>) they are only listed, nothing recorded. T687 reads this to annotate the
/// prompt with the "land it" line for exactly these quirks.
/// </param>
public sealed record QuirkSelection(IReadOnlyList<string> Lines, IReadOnlyList<string> CappedLines)
{
    /// <summary>No quirks this break.</summary>
    public static QuirkSelection None { get; } = new([], []);
}
