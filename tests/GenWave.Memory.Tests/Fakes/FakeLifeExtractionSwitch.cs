using GenWave.Core.Abstractions;

namespace GenWave.Memory.Tests.Fakes;

/// <summary>An <see cref="ILifeExtractionSwitch"/> that is on unless built off; flip <see cref="IsEnabled"/> mid-test.</summary>
public sealed class FakeLifeExtractionSwitch(bool enabled = true) : ILifeExtractionSwitch
{
    public bool IsEnabled { get; set; } = enabled;
}
