namespace GenWave.Tts;

/// <summary>
/// The two roles a <see cref="CrosstalkScriptWriter"/>-generated exchange ever casts (SPEC F127.1,
/// F127.2, STORY-326) — never a third. <see cref="Host"/> is the on-air DJ; <see cref="Neighbor"/>
/// is the schedule-adjacent "drop-in" persona a later task (PLAN T284's <c>CrosstalkPlanner</c>)
/// resolves from the grid. This writer never decides WHO fills either role — it only ever receives
/// two already-cast <see cref="GenWave.Core.Domain.PersonaCard"/>s on <see cref="CrosstalkExchangeRequest"/>
/// and tags each generated line with which one spoke it.
/// </summary>
public enum CrosstalkSpeaker
{
    /// <summary>The on-air host persona.</summary>
    Host,

    /// <summary>The schedule-adjacent drop-in persona (SPEC F127.2).</summary>
    Neighbor,
}
