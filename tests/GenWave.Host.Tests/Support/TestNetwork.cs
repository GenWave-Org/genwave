namespace GenWave.Host.Tests.Support;

/// <summary>
/// The one shared docker network every fixture container joins (gh-#649, STORY-440, PLAN T477):
/// <c>gw-test</c>, created once and declared <c>external: true</c> by <c>db-compose.yaml</c> and
/// <c>kokoro-compose.yaml</c>, so a full suite run stops allocating a project network per fixture
/// instance and exhausting the daemon's address pools. Linked into GenWave.MediaLibrary.Tests as a
/// compile item — one type, two assemblies.
/// </summary>
/// <remarks>Skeleton at plan time — every member throws until T477 lands.</remarks>
internal static class TestNetwork
{
    public const string Name = "gw-test";

    /// <summary>Runs <c>docker network create gw-test</c> through <paramref name="dockerExecutable"/>.
    /// An "already exists" failure is success; any other non-zero exit throws with the stderr text.</summary>
    public static void Ensure(string dockerExecutable = "docker") =>
        throw new NotImplementedException("pending: T477 — TestNetwork.Ensure (STORY-440)");
}
