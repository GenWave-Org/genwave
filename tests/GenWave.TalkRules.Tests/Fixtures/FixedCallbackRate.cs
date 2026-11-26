using GenWave.Core.Abstractions;

namespace GenWave.TalkRules.Tests.Fixtures;

internal sealed class FixedCallbackRate(int perHour) : ICallbackRate
{
    public int CallbacksPerHour => perHour;
}
