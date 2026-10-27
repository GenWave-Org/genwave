namespace GenWave.VoiceOff.Text;

/// <summary>The file spelling of each <see cref="GauntletRegister"/>.</summary>
internal static class RegisterKeys
{
    internal static readonly IReadOnlyList<GauntletRegister> All =
        [GauntletRegister.Bubbly, GauntletRegister.Laconic, GauntletRegister.LateNight];

    internal static string Key(GauntletRegister register) => register switch
    {
        GauntletRegister.Bubbly => "bubbly",
        GauntletRegister.Laconic => "laconic",
        GauntletRegister.LateNight => "late-night",
        _ => throw new ArgumentOutOfRangeException(nameof(register), register, "Unknown register."),
    };

    internal static bool TryParse(string? key, out GauntletRegister register)
    {
        foreach (GauntletRegister candidate in All)
        {
            if (string.Equals(Key(candidate), key, StringComparison.Ordinal))
            {
                register = candidate;
                return true;
            }
        }

        register = default;
        return false;
    }
}
