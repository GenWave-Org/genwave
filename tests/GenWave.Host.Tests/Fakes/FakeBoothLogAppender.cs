using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IBoothLogAppender"/> double — records every <see cref="AppendAsync"/> call's
/// <see cref="BoothLogAppendRequest"/> instead of touching Postgres. The shared home for what had
/// drifted into three near-verbatim per-file copies (Story217_BoothLogPickStamp.cs's own
/// <c>FakeBoothLogAppender</c>, Story358_AnnouncementAirConfirmation.cs's own <c>FakeBoothLogAppender</c>,
/// Story343_AnnouncementLifecycleSmoke.cs's own differently-named <c>RecordingBoothLogAppender</c>) —
/// the SAME <c>SimulatedPortStartupFilter</c> consolidation precedent (STORY-278/T190) applied here
/// (PLAN T343 review carry-forward C5, built PLAN T344).
///
/// Thread-safe by construction (a lock around <see cref="Calls"/> plus a <see cref="SemaphoreSlim"/>
/// release per append): Story217's own consumer drives this through a REAL background hosted service
/// and needs to await an async append landing rather than racing it (<see cref="WaitForCallsAsync"/>);
/// the two simpler consumers (Story343, Story358) drive their own drain services directly and just
/// read <see cref="Calls"/> synchronously after their own await completes, never touching the wait
/// helper — this fake serves both idioms without either one paying for what it doesn't use.
/// </summary>
sealed class FakeBoothLogAppender : IBoothLogAppender
{
    readonly SemaphoreSlim appended = new(0);

    public List<BoothLogAppendRequest> Calls { get; } = [];

    public Task AppendAsync(BoothLogAppendRequest request, CancellationToken ct)
    {
        lock (Calls) Calls.Add(request);
        appended.Release();
        return Task.CompletedTask;
    }

    /// <summary>Awaits until at least <paramref name="count"/> calls have landed, or
    /// <paramref name="timeout"/> elapses — Story217's own consumer, which drives real events through
    /// a background hosted service and cannot know synchronously when its own drain has caught up.</summary>
    public async Task WaitForCallsAsync(int count, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        for (var i = 0; i < count; i++)
            await appended.WaitAsync(cts.Token);
    }
}
