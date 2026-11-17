using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests.Fakes;

/// <summary>A fixed <see cref="ICallbackRateReader"/> answer for status-endpoint specs; no numbers means no offers yet.</summary>
public sealed class FakeCallbackRateReader(CallbackRate? rate = null) : ICallbackRateReader
{
    readonly CallbackRate? rate = rate ?? new CallbackRate(0, 0, 0);

    public Task<CallbackRate?> ReadAsync(CancellationToken ct) => Task.FromResult(rate);
}
