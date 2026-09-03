namespace ExamplePlugin;

using System.Globalization;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// A minimal, self-contained <see cref="IContextProvider"/> — no HTTP, no external dependency of any
/// kind, just enough to prove the seam end to end. It rolls a die and hands the result back as a fact;
/// <c>GenWave.Context.ContextPipeline</c> is what turns that fact into an on-air segment or patter
/// line, exactly the way it treats the house's own Weather/History providers (see those two classes'
/// own remarks in <c>GenWave.Context</c> — this class deliberately mirrors their shape at a much
/// smaller scale).
/// </summary>
public sealed class DiceRollContextProvider : IContextProvider
{
    /// <summary>The die size when nothing overrides it — see <see cref="ResolveSides"/>.</summary>
    public const int DefaultSides = 6;

    /// <summary>The smallest accepted <c>Sides</c> value — see <see cref="ResolveSides"/>'s own
    /// remarks on why this (and <see cref="MaxSides"/>) exist at all.</summary>
    public const int MinSides = 2;

    /// <summary>The largest accepted <c>Sides</c> value (see <see cref="ResolveSides"/>'s own
    /// remarks): a configured value above this — <c>int.MaxValue</c> included — falls back to
    /// <see cref="DefaultSides"/> rather than reaching <see cref="FetchAsync"/>'s own
    /// <c>sides + 1</c> at all, which would otherwise overflow into a negative
    /// <see cref="int"/> and make <see cref="Random.Next(int, int)"/> throw
    /// <see cref="ArgumentOutOfRangeException"/> — a fetch-time fault this class's own docs promise
    /// never happens for a bad <c>Sides</c> setting.</summary>
    public const int MaxSides = 1000;

    readonly Func<string, string?> settingReader;

    /// <param name="settingReader">
    /// Reads one setting beneath this plugin's own <c>Plugins:{slug}:</c> segment, or null when unset.
    /// Captured as a plain delegate — <see cref="IPluginHost.Setting(string)"/> itself — rather than
    /// the whole <see cref="IPluginHost"/>: this provider only ever needs to ASK a question, never to
    /// register anything, so it never holds a reference wide enough to do more than that. See
    /// <see cref="DiceRollPlugin.Register"/> for where this delegate comes from.
    /// </param>
    public DiceRollContextProvider(Func<string, string?> settingReader)
    {
        ArgumentNullException.ThrowIfNull(settingReader);
        this.settingReader = settingReader;
    }

    public string Key => "example-dice";

    public Task<ContextContent?> FetchAsync(CancellationToken ct)
    {
        // Unused on purpose, not by oversight: this provider does no I/O and no long-running work
        // (Random.Shared.Next is synchronous, in-memory) — there is nothing here a caller could
        // usefully cancel. A REAL provider that calls out (HTTP, disk) must honor ct exactly the
        // way WeatherContextProvider/HistoryContextProvider both do.
        var sides = ResolveSides();
        var roll = Random.Shared.Next(1, sides + 1);
        var fact = $"The house die just landed on {roll.ToString(CultureInfo.InvariantCulture)} (a d{sides.ToString(CultureInfo.InvariantCulture)}).";

        // A short shelf life on purpose: unlike weather or history, a fresh roll is cheap and the
        // whole point is that it changes often. FreshUntil only bounds how long the pipeline may
        // reuse THIS content before fetching again — it says nothing about how often a segment or
        // patter line actually airs (that cadence is Context:example-dice:* config, read by the
        // pipeline, not this class).
        var content = new ContextContent([fact], DateTimeOffset.UtcNow.AddMinutes(1));
        return Task.FromResult<ContextContent?>(content);
    }

    /// <summary>
    /// Reads the optional <c>Plugins:{slug}:Sides</c> knob (SPEC F157.2) — a blank/unset value, one
    /// that fails to parse as an integer, or one outside <see cref="MinSides"/>–<see cref="MaxSides"/>
    /// (inclusive) all fall back to <see cref="DefaultSides"/> rather than throwing: a plugin's own
    /// config is env/compose-only in v1 (no live-reload path), so treating anything unreadable OR
    /// unreasonable as "not configured" — never a boot-time or fetch-time fault — is the same
    /// fail-soft posture <c>WeatherContextProvider</c>/<c>HistoryContextProvider</c> both take on
    /// their own optional settings. The upper bound is load-bearing, not decorative: a die with
    /// <c>int.MaxValue</c> sides would overflow <see cref="FetchAsync"/>'s own <c>sides + 1</c> into
    /// a negative <see cref="int"/>, and <see cref="Random.Next(int, int)"/> throws on a max below
    /// its min — exactly the fetch-time fault this method's own contract rules out.
    /// </summary>
    int ResolveSides()
    {
        var configured = settingReader("Sides");
        if (int.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sides)
            && sides is >= MinSides and <= MaxSides)
        {
            return sides;
        }

        return DefaultSides;
    }
}
