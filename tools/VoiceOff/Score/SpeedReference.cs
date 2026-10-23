using System.Globalization;
using GenWave.Tts;

namespace GenWave.VoiceOff.Score;

/// <summary>SPEC F214.7: the budget reference lines beside the scored table. Speed never affects a verdict.</summary>
internal static class SpeedReference
{
    internal const int TypicalLineSeconds = 20;

    internal static string Shipped()
    {
        int budget = new TtsOptions().RenderBudgetSeconds;
        return $"Speed reference: shipped budget {budget.ToString(CultureInfo.InvariantCulture)} s for a {TypicalLineSeconds} s line ⇒ rtf {Rtf(budget)}";
    }

    internal static string YourBox(int budgetSeconds) =>
        $"Your box: {budgetSeconds.ToString(CultureInfo.InvariantCulture)} s for a {TypicalLineSeconds} s line ⇒ rtf {Rtf(budgetSeconds)}";

    private static string Rtf(int budgetSeconds) =>
        ((double)budgetSeconds / TypicalLineSeconds).ToString("0.##", CultureInfo.InvariantCulture);
}
