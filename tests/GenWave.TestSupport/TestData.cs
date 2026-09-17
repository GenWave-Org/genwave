using GenWave.Core.Domain;
using GenWave.TestSupport.Fakes;

namespace GenWave.TestSupport;

/// <summary>
/// Small scripted-value factories shared across Orchestrator specs (moved out of
/// <c>GenWave.Orchestration.Tests.Fakes.ProductionChainHarness</c> at PLAN T511 so
/// <see cref="OrchestratorBuilder"/>'s own defaults can use them too, without either side
/// depending on the other).
/// </summary>
public static class TestData
{
    /// <summary>Builds a persona with the given id/name/voice; every other field a fixed,
    /// arbitrary-but-stable value no spec has ever needed to vary.</summary>
    public static Persona MakePersona(long id, string name, string voice)
    {
        var now = DateTime.UnixEpoch;
        return new Persona(id, name, "", "", voice, now, now);
    }

    /// <summary>Builds a playable <see cref="MediaReference"/> for the given id; every other
    /// field a fixed, arbitrary-but-stable value no spec has ever needed to vary.</summary>
    public static MediaReference MakeTrackRef(string id) => new(
        MediaId: id,
        Locator: $"/media/{id}.mp3",
        Title: $"Track {id}",
        Loudness: new Loudness(-23.0, -1.0, true),
        DurationMs: null,
        SampleRate: null,
        Channels: null,
        BitrateKbps: null,
        Artist: null,
        Album: null,
        Genre: null,
        Year: null);

    /// <summary>A <see cref="FakePersonaStore"/> seeded with exactly one persona — the common
    /// single-DJ-station shape most specs need.</summary>
    public static FakePersonaStore OneDjStore(long id, string name, string voice)
    {
        var store = new FakePersonaStore();
        store.Add(MakePersona(id, name, voice));
        return store;
    }
}
