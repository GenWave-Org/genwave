namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// <see cref="LawParity.Compare"/>'s outcome (STORY-293 AC2): which law ids the suite knows about but
/// CONTRIBUTING's table doesn't (a dropped row), and which ids the table declares but the suite
/// doesn't (a phantom row) — both directions, per AC2's "every law id present in one is present in
/// the other".
/// </summary>
/// <param name="MissingFromDoc">Suite law ids with no matching table row.</param>
/// <param name="ExtraInDoc">Table rows whose id names no real suite law.</param>
internal sealed record LawParityResult(IReadOnlyList<string> MissingFromDoc, IReadOnlyList<string> ExtraInDoc)
{
    public bool IsClean => MissingFromDoc.Count == 0 && ExtraInDoc.Count == 0;
}
