namespace GenWave.Core.Domain;

/// <summary>
/// The one test for "this media can still air": the row exists, is eligible and is not never_play. Gone, switched off or
/// never_play is operator intent, so a re-render has nothing to replace. Shared by the ad worker's stale pick, the ad swap
/// guard, the ad and seeder stale counts (SPEC F219.23) and the seeder sweep, so they can never disagree.
/// </summary>
public static class AdminMediaAirability
{
    public static bool CanStillAir(AdminMediaDto? row) => row is { Eligible: true, NeverPlay: false };
}
