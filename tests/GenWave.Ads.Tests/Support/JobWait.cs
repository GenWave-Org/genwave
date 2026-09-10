namespace GenWave.Ads.Tests.Support;

/// <summary>
/// The one bounded-wait mechanism every <see cref="AdSpotJobService"/> unit spec polls a condition
/// through (PLAN T441) — never a bare <c>Task.Delay</c>, and never a throwing poll a genuine bug could
/// turn into a crash instead of a red assertion. Returns <see langword="false"/> on a timeout rather
/// than throwing, so a caller's own <c>Assert.True</c> on the result is what carries the claim (the
/// <c>AdSpotJobTestHelpers.TryPollUntilAsync</c> precedent one project over, in Host.Tests).
/// </summary>
internal static class JobWait
{
    static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(25);

    public static async Task<bool> TryWaitUntilAsync(Func<bool> condition, TimeSpan deadline, TimeSpan? interval = null)
    {
        var pollInterval = interval ?? DefaultInterval;
        var deadlineAt = DateTime.UtcNow + deadline;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadlineAt)
                return false;

            await Task.Delay(pollInterval);
        }

        return true;
    }
}
