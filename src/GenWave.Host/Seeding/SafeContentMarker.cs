namespace GenWave.Host.Seeding;

/// <summary>
/// What a seeded file was voiced with (SPEC F219.21/F219.22): the engine and the voice id resolved for it. Stored
/// readable, not hashed — the ledger is for diagnosis too — and compared with plain equality: any difference from
/// the marker the station would render with now means the file is stale.
/// </summary>
public static class SafeContentMarker
{
    public static string For(string engine, string voice) => $"{engine}\n{voice}";
}
