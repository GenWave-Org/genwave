namespace GenWave.Host.Icons;

/// <summary>
/// The pack-level style block of a validated icon pack definition (SPEC F130.1, STORY-337, PLAN
/// T302) — every icon in the pack renders under this single stroke width and fill mode; an
/// individual <see cref="IconElement"/> may still override fill/stroke, restricted to the exact same
/// two-token vocabulary. Only <see cref="IconPackDefinitionParser"/> constructs one, and only after
/// both <see cref="StrokeWidth"/> and <see cref="Fill"/> have passed their own SPEC-pinned gates.
/// </summary>
/// <param name="StrokeWidth">SVG <c>stroke-width</c>, constrained to <c>[0.5, 3]</c> (SPEC
/// F130.1).</param>
/// <param name="Fill">Either <c>"none"</c> or <c>"currentColor"</c> — the only two fill tokens this
/// schema can express (SPEC F130.1's "inexpressible by schema" ruling: no literal hue anywhere a pack
/// can reach).</param>
public sealed record IconPackStyle(double StrokeWidth, string Fill);
