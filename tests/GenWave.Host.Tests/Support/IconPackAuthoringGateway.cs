using GenWave.Host.Icons;

namespace GenWave.Host.Tests.Support;

/// <summary>
/// PLAN T305's one doorway into <see cref="IconPackDefinitionParser"/>/<see cref="IconPackDefinitionSerializer"/>
/// — mirrors <see cref="SeamCompositionSnapshot"/>'s own rationale for existing in THIS assembly rather
/// than in <c>tools/</c> directly: both types are <c>internal</c>, and <c>GenWave.Host.csproj</c>'s one
/// <c>InternalsVisibleTo</c> names exactly <c>GenWave.Host.Tests</c>. <c>tools/IconPackAuthor</c> must
/// not touch <c>src/</c> (a new <c>InternalsVisibleTo</c> entry would count) — SEE that project's own
/// remarks on WHY the offline authoring script validates its own output through the REAL parser rather
/// than a second, independently-maintained copy of SPEC F130.1's whitelist/grammar: the whole point is
/// zero drift between what T302 built and what a future glyph pack must satisfy, and re-typing the
/// grammar into a script would be exactly the kind of second copy PLAN T302's own review note (BUILDER
/// CAUGHT A SPEC BUG — F130.1's literal grammar formed a <c>+</c>..<c>e</c> regex range) warns future
/// implementers away from.
///
/// <para>
/// Everything this type exposes is a plain pass-through: <see cref="Validate"/> and
/// <see cref="Serialize"/> are the exact same two calls <see cref="Api.IconPackController.Install"/>
/// makes at install time (build the model → <c>Serialize</c> the canonical form → <c>Validate</c> the
/// canonical bytes) — the authoring script runs the identical round trip offline, so a pack it emits is
/// PROVEN installable before it ever reaches a PR.
/// </para>
/// </summary>
public static class IconPackAuthoringGateway
{
    /// <summary>Runs <paramref name="json"/> through the REAL <see cref="IconPackDefinitionParser.Validate"/>
    /// — the same call <see cref="Api.IconPackController.Install"/> makes against a fetched catalog
    /// manifest.</summary>
    public static IconPackValidationResult Validate(byte[] json) => IconPackDefinitionParser.Validate(json);

    /// <summary>Runs <paramref name="definition"/> through the REAL <see cref="IconPackDefinitionSerializer.Serialize"/>
    /// — the ONLY canonical <c>gw-icon-pack</c> document shape (ordinal-sorted icon keys, an explicit
    /// <c>schemaVersion</c>), exactly as <see cref="Api.IconPackController.Install"/> persists it.</summary>
    public static string Serialize(IconPackDefinition definition) => IconPackDefinitionSerializer.Serialize(definition);
}
