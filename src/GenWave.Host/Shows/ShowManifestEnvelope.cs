namespace GenWave.Host.Shows;

using GenWave.Abstractions.Playout;

/// <summary>
/// A show import manifest's optional <c>envelope</c> object (SPEC F152.6, STORY-373, PLAN T363) — the
/// show-manifest schema **1.1** addition riding alongside <see cref="ShowManifest"/>'s own 1.0
/// <c>{name, tagline, flavor}</c> fields. Carries <see cref="Rotation"/> ONLY — the identical single
/// field <c>station.show.envelope</c> itself is narrowed to (SPEC F115.2's dormant-envelope-keys law,
/// SPEC F152.3/PLAN T360's own one-field carve-out) — every other key a manifest's own <c>envelope</c>
/// object might carry is read by nothing and silently ignored (forward compat, SPEC F152.6).
///
/// <para>
/// <see cref="ShowManifestParser.Parse"/> only ever constructs this type with a validated, NON-null
/// <see cref="Rotation"/> (at least one bound set, <c>maxPlays</c> ≥ 0, <c>notAiredWithinDays</c>
/// 1–3650 — the identical three SPEC F152.1/F152.5 rules <c>ShowRotationController.ParseRotationBody</c>
/// enforces at the PUT edge, mirrored here at the import edge). A manifest with no <c>envelope</c> key,
/// an <c>envelope</c> with no <c>rotation</c> key, or an explicit <c>envelope.rotation: null</c> all
/// parse to a <see langword="null"/> <see cref="ShowManifest.Envelope"/> instead of an instance of this
/// type carrying a null <see cref="Rotation"/> — collapsing "no rotation opinion" to ONE representable
/// state (<c>Envelope is null</c>), never two.
/// </para>
/// </summary>
public sealed record ShowManifestEnvelope(RotationPredicate Rotation);
