using GenWave.Host.Icons;

namespace GenWave.IconPackAuthor;

/// <summary>
/// One <see cref="PackAuthoringPipeline.Run"/> outcome. Closed hierarchy (private base constructor) —
/// mirrors <c>IconPackValidationResult</c>'s own shape, one layer upstream of it — so a caller
/// (<see cref="Program"/>, <see cref="SelfTest"/>) switches over it exhaustively.
/// </summary>
public abstract record PackAuthoringOutcome
{
    private PackAuthoringOutcome() { }

    /// <summary>Every mapped glyph converted AND the emitted document passed the real
    /// <c>IconPackDefinitionParser.Validate</c> (the "zero drift" proof — this is what
    /// <see cref="CanonicalJson"/> was built from, byte for byte). <see cref="IgnoredNames"/> mirrors
    /// <c>IconPackValidationResult.Valid.IgnoredNames</c>: mapped names outside
    /// <c>IconNameContract.Names</c> — legal, just inert (SPEC F130.2).</summary>
    public sealed record Success(string CanonicalJson, IconPackDefinition Definition, IReadOnlyList<string> IgnoredNames) : PackAuthoringOutcome;

    /// <summary>The run did not produce a pack. <see cref="Reasons"/> names every offending glyph (or
    /// mapping problem) collected in one pass — STORY-338 AC1's own "fails naming the offending glyph,"
    /// applied to a whole batch rather than stopping at the first failure.</summary>
    public sealed record Failure(IReadOnlyList<string> Reasons) : PackAuthoringOutcome;
}
