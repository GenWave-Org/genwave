using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests.Fakes;

/// <summary>A fixed <see cref="IRepeatNumbersReader"/> answer for status-endpoint specs; no numbers means an empty panel.</summary>
public sealed class FakeRepeatNumbersReader(RepeatNumbers? numbers = null) : IRepeatNumbersReader
{
    readonly RepeatNumbers? numbers = numbers ?? new RepeatNumbers([], []);

    public Task<RepeatNumbers?> ReadAsync(CancellationToken ct) => Task.FromResult(numbers);
}
