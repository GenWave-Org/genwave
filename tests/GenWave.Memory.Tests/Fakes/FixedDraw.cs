namespace GenWave.Memory.Tests.Fakes;

/// <summary>A <see cref="Random"/> whose draw is fixed, so a spec pins the pass or fail of the callback draw.</summary>
public sealed class FixedDraw(double value) : Random
{
    public int Draws { get; private set; }

    public override double NextDouble()
    {
        Draws++;
        return value;
    }
}
