using System.Diagnostics;
using System.Text;

namespace GenWave.Orchestration;

/// <summary>
/// The plan phase's whole output for one break (SPEC F187): the unit ordinal and context it was
/// built from, the render budget it read once (SPEC F188.3), and its slots in kick order.
/// </summary>
/// <param name="UnitOrdinal">Which unit (music item pull) this break precedes.</param>
/// <param name="Context">The <see cref="BreakContext"/> the plan was built from.</param>
/// <param name="RenderBudget">
/// The render budget stamped once at plan time (SPEC F188.3); the render phase never re-reads the
/// budget provider itself.
/// </param>
/// <param name="Slots">The plan's slots, numbered from 1 in kick order (SPEC F187.2).</param>
public sealed record BreakPlan(
    int UnitOrdinal,
    BreakContext Context,
    TimeSpan RenderBudget,
    IReadOnlyList<PlannedSlot> Slots)
{
    /// <summary>
    /// Renders one line per slot: <c>#&lt;ordinal&gt; &lt;Kind&gt; &lt;source&gt;
    /// speaker=&lt;name|station&gt; res=&lt;kind:key&gt;|-</c> (SPEC F187.5). Lines are joined with
    /// <c>\n</c>; a slot-less plan renders exactly <c>(empty)</c>.
    /// </summary>
    public string ToTrace()
    {
        if (Slots.Count == 0)
        {
            return "(empty)";
        }

        var trace = new StringBuilder();
        for (var i = 0; i < Slots.Count; i++)
        {
            if (i > 0)
            {
                trace.Append('\n');
            }

            trace.Append(TraceLine(Slots[i]));
        }

        return trace.ToString();
    }

    static string TraceLine(PlannedSlot slot)
    {
        var reservation = slot.Reservation is { } r ? $"{r.Kind}:{r.Key}" : "-";
        return $"#{slot.Ordinal} {slot.Kind} {SourceToken(slot.Source)} speaker={Speaker(slot.Source)} res={reservation}";
    }

    // Exhaustive without a discard arm (SPEC F187.1): the final arm names the base type itself,
    // loudly, rather than a silent `_` — the only shape Roslyn will accept as a build-breaking
    // guard for a class hierarchy switch (an enum or `_`-based switch can never be proven exhaustive
    // by the compiler, so this is the closest the language gets to F187.1's letter).
    static string SourceToken(SlotSource source) => source switch
    {
        RenderSource => "render",
        VerbatimSource => "verbatim",
        ReadySource => "ready",
        SlotSource => throw new UnreachableException($"Unhandled {nameof(SlotSource)} case: {source.GetType()}"),
    };

    // SPEC F187.4: a Render/Verbatim slot's speaker is its stamped SpeakerSnapshot's own name when
    // one was resolved (PLAN T527), falling back to the request's own PersonaName for a caller that
    // passes no ISpeakerSnapshotSource (SPEC F189.6, Speaker stays null) — a Ready slot carries none.
    static string Speaker(SlotSource source) => source switch
    {
        RenderSource render => render.Request.Speaker?.PersonaName ?? render.Request.PersonaName ?? "station",
        VerbatimSource verbatim => verbatim.Request.Speaker?.PersonaName ?? verbatim.Request.PersonaName ?? "station",
        ReadySource => "-",
        SlotSource => throw new UnreachableException($"Unhandled {nameof(SlotSource)} case: {source.GetType()}"),
    };
}
