using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Ads.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IBoothLogAppender"/> double (gh-#865) — records every append instead of
/// touching Postgres; <see cref="ThrowOnAppend"/> makes every append fail, for the "a booth-log
/// failure never fails the tick" scenario.
/// </summary>
sealed class FakeBoothLogAppender : IBoothLogAppender
{
    public List<BoothLogAppendRequest> Calls { get; } = [];

    public bool ThrowOnAppend { get; set; }

    public Task AppendAsync(BoothLogAppendRequest request, CancellationToken ct)
    {
        if (ThrowOnAppend)
            throw new InvalidOperationException("booth log unavailable");
        Calls.Add(request);
        return Task.CompletedTask;
    }
}
