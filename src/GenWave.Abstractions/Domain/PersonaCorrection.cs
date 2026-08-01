namespace GenWave.Core.Domain;

/// <summary>
/// One card-authored pronunciation correction (SPEC F71.1, F97.4): replace <see cref="From"/> with
/// <see cref="To"/> wherever it appears in booth-bound text. Mirrors <c>GenWave.Tts.SpeechCorrection</c>'s
/// <c>{From, To}</c> shape by deliberate instruction (SPEC F71.1) rather than by shared type — this
/// project (the MIT contract surface, zero dependencies) cannot reference <c>GenWave.Tts</c>, and this
/// is the portable, exported card shape rather than the live compiled-and-matched runtime one. This
/// card's corrections merge <b>over</b> station corrections at render (F97.4 amends the original
/// station-wins precedence F71.7 shipped). The exact invariant: <b>no station rule ever pre-empts a
/// card rule</b> — every card correction gets its turn on the text before any station correction
/// runs. A card correction can still lose an overlap, but only to ANOTHER card correction, never to
/// a station one. So a card author can rely on their correction being tried first, and what competes
/// for the INPUT TEXT is the rest of their own card rather than the operator's settings — but a
/// station correction running afterwards can still rewrite the text a card correction produced
/// (card <c>MacLeod->muh-CLOUD</c> then station <c>CLOUD->KLOWD</c> yields <c>muh-KLOWD</c>).
/// </summary>
public sealed record PersonaCorrection(string From, string To);
