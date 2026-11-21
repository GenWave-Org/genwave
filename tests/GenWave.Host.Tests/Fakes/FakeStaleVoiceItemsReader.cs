using GenWave.Host.Seeding;

namespace GenWave.Host.Tests.Fakes;

/// <summary>A fixed <see cref="IStaleVoiceItemsReader"/> answer for status specs that do not care about the voice count.</summary>
public sealed class FakeStaleVoiceItemsReader(int? count = 0) : IStaleVoiceItemsReader
{
    public Task<int?> CountAsync(CancellationToken ct) => Task.FromResult(count);
}
