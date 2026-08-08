using System.Reflection;
using System.Runtime.CompilerServices;

namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// L4-immutability's detector (STORY-291 AC2): every publicly-visible type in
/// <c>GenWave.Abstractions</c> carries no publicly settable state (ARCHITECTURE.md "Architecture
/// governance": "its types are records/interfaces with no mutable public state"). Plain reflection
/// over the caller-supplied type list, not ArchUnitNET: this is a MEMBER-shape question (is this
/// accessor <c>init</c> or an ordinary <c>set</c>?), not a dependency-graph one, and
/// <see cref="System.Reflection"/> already exposes exactly the bit that answers it — the
/// <see cref="IsExternalInit"/> required custom modifier the compiler stamps on an <c>init</c>
/// accessor's return parameter — with no extra library.
///
/// Takes <see cref="IEnumerable{Type}"/> rather than an <see cref="Assembly"/> so the production
/// fact (subjects = <c>GenWave.Abstractions</c>'s own exported types) and the hermetic fixture proof
/// (subjects = a short, explicit fixture list) share the exact same detector function — the same
/// "one function, two subject sets" shape <see cref="HttpClientSeams.FindViolations"/> and
/// <see cref="PostgresConfinement.FindViolations"/> already use for L3 and L2.
///
/// <b>Scope: accessor/field shape, not property TYPE — shallow, not deep, immutability.</b> This
/// detector only asks whether the MEMBER itself is publicly settable; it never inspects what a
/// get-only or init-only member's own type exposes. A hypothetical <c>public List&lt;string&gt;
/// Tags { get; init; }</c> would pass — the property is init-only, but the caller-visible
/// <c>List&lt;string&gt;</c> instance it hands out is itself mutable (<c>.Add</c>,
/// <c>.Clear</c>, ...), a real hole a stricter law would also want closed. Verified at T213: zero
/// public members anywhere in <c>GenWave.Abstractions</c> are typed as a mutable collection today
/// (every collection-shaped member already uses <c>IReadOnlyList</c>/<c>IReadOnlyCollection</c>) —
/// so this is a documented boundary of what L4-immutability checks, not a live hole.
/// </summary>
internal static class AbstractionsImmutability
{
    private const BindingFlags OwnPublicMembers =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>Every mutable-public-state violation across <paramref name="subjects"/>. Enums are
    /// skipped outright: an enum's members compile to public static literal fields plus one
    /// compiler-generated instance field (<c>value__</c>) that reflection reports as public even
    /// though the language gives no syntax to ever set it — flagging it would be a detector bug, not
    /// a real finding (the exact false-positive the design notes call out).</summary>
    public static IReadOnlyList<LawViolation> FindViolations(IEnumerable<Type> subjects)
    {
        var violations = new List<LawViolation>();

        foreach (var type in subjects)
        {
            if (type.IsEnum)
                continue;

            violations.AddRange(FindPropertyViolations(type));
            violations.AddRange(FindFieldViolations(type));
        }

        return violations;
    }

    /// <summary>A property violates the law when its own setter is both PUBLIC (a privately-gated
    /// <c>{ get; private set; }</c> exposes no publicly mutable state at all) and NOT <c>init</c>.
    /// <c>Member</c> is <c>Type.Property</c>, not just the type — the design notes' "type-level
    /// granularity is available here, use it" — so two violating members on the same type each get
    /// their own exemption slot instead of one blanket entry silencing both.</summary>
    private static IEnumerable<LawViolation> FindPropertyViolations(Type type) =>
        type.GetProperties(OwnPublicMembers)
            .Where(property => property.SetMethod is { IsPublic: true } setMethod && !IsInitOnly(setMethod))
            .Select(property => new LawViolation(
                LawId.L4Immutability,
                $"{type.FullName ?? type.Name}.{property.Name}",
                "public property has a publicly settable (non-init) accessor"));

    /// <summary>A field violates the law when it is public and neither a compile-time constant
    /// (<c>IsLiteral</c>, e.g. <c>const</c>) nor <c>readonly</c> (<c>IsInitOnly</c>, true for both
    /// instance and <c>static readonly</c> fields) — the two allowed public-field shapes the design
    /// notes name explicitly.</summary>
    private static IEnumerable<LawViolation> FindFieldViolations(Type type) =>
        type.GetFields(OwnPublicMembers)
            .Where(field => !field.IsLiteral && !field.IsInitOnly)
            .Select(field => new LawViolation(
                LawId.L4Immutability,
                $"{type.FullName ?? type.Name}.{field.Name}",
                "public field is mutable (neither const nor readonly)"));

    /// <summary>Whether <paramref name="setMethod"/> is an <c>init</c> accessor rather than an
    /// ordinary <c>set</c> — the only reflectable trace of the <c>init</c> keyword is this required
    /// custom modifier on the accessor's return parameter; there is no <c>PropertyInfo</c> flag for
    /// it.</summary>
    private static bool IsInitOnly(MethodInfo setMethod) =>
        setMethod.ReturnParameter.GetRequiredCustomModifiers()
            .Any(modifier => modifier == typeof(IsExternalInit));
}
