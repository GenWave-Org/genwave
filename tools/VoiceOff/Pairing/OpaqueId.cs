using System.Security.Cryptography;

namespace GenWave.VoiceOff.Pairing;

/// <summary>A clip or pair id: 16 random bytes as 32 lower-case hex characters. Never derived from the seed, a name, or an order.</summary>
internal static class OpaqueId
{
    private const int Bytes = 16;

    internal static string New() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(Bytes));
}
