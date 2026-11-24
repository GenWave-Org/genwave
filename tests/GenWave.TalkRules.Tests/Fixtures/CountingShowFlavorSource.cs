using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>A show-flavour source with one due line (or none) that counts how often it was asked, and can be told to throw.</summary>
internal sealed class CountingShowFlavorSource(ShowFlavorFact? due) : IShowFlavorLineSource
{
    public int Takes { get; private set; }

    public bool Throws { get; init; }

    public ShowFlavorFact? TryTakeDueShowLine()
    {
        Takes++;
        return Throws ? throw new InvalidOperationException("fixture show source failure") : due;
    }
}
