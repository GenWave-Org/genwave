namespace GenWave.Host.Tests.Support;

/// <summary>
/// Startup sweep for the stragglers (gh-#710, STORY-441, PLAN T479): removes every directory
/// directly under <c>root</c> whose name starts with one of <see cref="Prefixes"/> and whose last
/// write is older than <c>olderThan</c>. Run once per Host.Tests process from a module initializer.
/// </summary>
/// <remarks>Skeleton at plan time — throws until T479 lands.</remarks>
internal static class TempSweep
{
    public static readonly string[] Prefixes = ["gw-", "genwave-pawire-", "story343-env-", "gh332-"];

    /// <summary>Returns the number of directories removed.</summary>
    public static int Run(string root, TimeSpan olderThan) =>
        throw new NotImplementedException("pending: T479 — TempSweep.Run (STORY-441)");
}
