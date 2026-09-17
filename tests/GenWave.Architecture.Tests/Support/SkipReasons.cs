using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GenWave.Architecture.Tests.Support;

/// <summary>How a <c>Skip</c> reason classifies under the skip-prefix law (SPEC F182.1, STORY-443).</summary>
internal enum SkipClass
{
    /// <summary><c>pending: T&lt;n&gt;</c> — a planned task turns it green.</summary>
    Pending,
    /// <summary><c>manual:</c> — evidence only a person listening to the stream can produce.</summary>
    Manual,
    /// <summary><c>gate:</c> — covered by a <c>stack_gate.sh</c> assertion; deleted by T507.</summary>
    Gate,
    /// <summary><c>obsolete:</c> — the task or behaviour is gone; deleted by T507.</summary>
    Obsolete,
    /// <summary>None of the four prefixes.</summary>
    Violation,
}

/// <summary>One skipped fact as the source scan sees it.</summary>
internal sealed record SkipReason(string File, string Fact, string Reason, SkipClass Class);

/// <summary>
/// Source scan behind the skip-prefix law (SPEC F182.1, STORY-443, PLAN T505): every
/// <c>[Fact(Skip = …)]</c>/<c>[Theory(Skip = …)]</c> under <c>tests/</c>, plus every
/// <c>Skip = …</c> assignment inside a custom <see cref="FactAttribute"/>/<see cref="TheoryAttribute"/>
/// subclass's own constructor (<c>RequiresRealEspeakNgAttribute</c> is the real example), with
/// identifiers resolved by C# lookup order from the usage site — the nearest enclosing type's own
/// <c>const string</c> fields, then each outer type in turn (the compilation-unit fallback is
/// defensive only: a top-level const is a local statement, unreachable from any type) — never a
/// sibling type's const of the same name. Classified by <see cref="Classify"/>.
///
/// Built on <c>Microsoft.CodeAnalysis.CSharp</c> (already pinned for the solution — see
/// <c>GenWave.Architecture.Tests.csproj</c>'s own T211 note) rather than a text scan: a real syntax
/// tree tells a string literal from a comment or a raw string's contents for free, so a <c>Skip</c>-
/// shaped substring inside prose or sample source (Story193/Story230's own inline comments; a raw
/// string literal like <c>@"""/api/"</c>) is never mistaken for a real attribute argument.
/// </summary>
internal static class SkipReasons
{
    private static readonly Regex PendingPrefix = new(@"^pending: T\d+", RegexOptions.Compiled);
    private static readonly Regex ManualPrefix = new(@"^manual:", RegexOptions.Compiled);
    private static readonly Regex GatePrefix = new(@"^gate:", RegexOptions.Compiled);
    private static readonly Regex ObsoletePrefix = new(@"^obsolete:", RegexOptions.Compiled);

    /// <summary>Classifies a resolved <c>Skip</c> reason under SPEC F182.1's four allowed prefixes,
    /// or <see cref="SkipClass.Violation"/> when none match.</summary>
    public static SkipClass Classify(string reason) => reason switch
    {
        _ when PendingPrefix.IsMatch(reason) => SkipClass.Pending,
        _ when ManualPrefix.IsMatch(reason) => SkipClass.Manual,
        _ when GatePrefix.IsMatch(reason) => SkipClass.Gate,
        _ when ObsoletePrefix.IsMatch(reason) => SkipClass.Obsolete,
        _ => SkipClass.Violation,
    };

    /// <summary>Every <c>Skip</c> usage under <paramref name="root"/> (recursive, <c>*.cs</c>,
    /// <c>bin/</c> and <c>obj/</c> segments excluded), ordered by file then by position within the
    /// file.</summary>
    public static IReadOnlyList<SkipReason> Scan(string root)
    {
        var results = new List<SkipReason>();

        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsUnderBuildOutput(path))
            .OrderBy(path => path, StringComparer.Ordinal);

        foreach (var file in files)
        {
            var relativeFile = Path.GetRelativePath(root, file).Replace('\\', '/');
            ScanFile(File.ReadAllText(file), relativeFile, results);
        }

        return results;
    }

    /// <summary>A count per <see cref="SkipClass"/> — every class gets an entry, zero included — so
    /// the gate report's manual-evidence line (SPEC F182.3) can print "0 obsolete" rather than a
    /// missing key.</summary>
    public static IReadOnlyDictionary<SkipClass, int> Counts(IEnumerable<SkipReason> reasons)
    {
        var counts = Enum.GetValues<SkipClass>().ToDictionary(skipClass => skipClass, _ => 0);
        foreach (var reason in reasons)
        {
            counts[reason.Class]++;
        }

        return counts;
    }

    private static bool IsUnderBuildOutput(string path) =>
        path.Split('/', '\\').Any(segment => segment is "bin" or "obj");

    private static void ScanFile(string source, string relativeFile, List<SkipReason> results)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();

        foreach (var (expression, site) in FindSkipUsages(root))
        {
            var fact = FactNameFor(site, relativeFile);
            var resolved = ResolveExpression(expression, new HashSet<string>(StringComparer.Ordinal));
            var reason = resolved ?? expression.ToString();
            var skipClass = resolved is null ? SkipClass.Violation : Classify(resolved);

            results.Add(new SkipReason(relativeFile, fact, reason, skipClass));
        }
    }

    /// <summary>Every <c>Skip</c> usage in the file, in source order: a <c>NameEquals</c> argument
    /// named <c>Skip</c> on a <c>[Fact]</c>/<c>[Theory]</c> (or a custom subclass whose name ends in
    /// <c>FactAttribute</c>/<c>TheoryAttribute</c>) attribute, and a plain assignment to the
    /// identifier <c>Skip</c> anywhere else (a custom attribute's own constructor setting its
    /// inherited <see cref="FactAttribute.Skip"/> property).</summary>
    private static IEnumerable<(ExpressionSyntax Expression, SyntaxNode Site)> FindSkipUsages(CompilationUnitSyntax root)
    {
        var usages = new List<(ExpressionSyntax Expression, SyntaxNode Site, int Position)>();

        foreach (var attribute in root.DescendantNodes().OfType<AttributeSyntax>())
        {
            if (attribute.ArgumentList is null || !IsFactOrTheoryAttribute(attribute))
            {
                continue;
            }

            foreach (var argument in attribute.ArgumentList.Arguments)
            {
                if (argument.NameEquals?.Name.Identifier.Text == "Skip")
                {
                    usages.Add((argument.Expression, attribute, attribute.SpanStart));
                }
            }
        }

        foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Left is IdentifierNameSyntax { Identifier.Text: "Skip" })
            {
                usages.Add((assignment.Right, assignment, assignment.SpanStart));
            }
        }

        return usages.OrderBy(usage => usage.Position).Select(usage => (usage.Expression, usage.Site));
    }

    private static bool IsFactOrTheoryAttribute(AttributeSyntax attribute)
    {
        var name = attribute.Name switch
        {
            SimpleNameSyntax simple => simple.Identifier.Text,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            _ => attribute.Name.ToString(),
        };

        return name is "Fact" or "Theory"
            || name.EndsWith("FactAttribute", StringComparison.Ordinal)
            || name.EndsWith("TheoryAttribute", StringComparison.Ordinal);
    }

    /// <summary>The enclosing method's name, or — for a <c>Skip</c> assignment with no enclosing
    /// method (a custom attribute's own constructor) — the enclosing type's name.</summary>
    private static string FactNameFor(SyntaxNode site, string relativeFile)
    {
        var method = site.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (method is not null)
        {
            return method.Identifier.Text;
        }

        var type = site.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (type is not null)
        {
            return type.Identifier.Text;
        }

        throw new InvalidOperationException(
            $"{relativeFile}: a Skip usage sits outside both a method and a type declaration.");
    }

    /// <summary>Finds the <c>const string</c> declaration named <paramref name="name"/> that a C#
    /// compiler would bind to from <paramref name="site"/>: the nearest enclosing type's own
    /// members first, then each outer type in turn (a nested class sees its containing class's
    /// consts unqualified). The compilation-unit fallback is defensive only: a top-level const parses
    /// as a local statement, never a member, so it cannot match. Never looks inside a sibling type —
    /// two classes in the same file each declaring <c>const string Skip</c> stay independent,
    /// matching real C# name lookup (there is no unqualified access across unrelated types).</summary>
    private static ExpressionSyntax? FindConstDeclaration(string name, SyntaxNode site)
    {
        foreach (var type in site.Ancestors().OfType<TypeDeclarationSyntax>())
        {
            var found = FindConstInMembers(name, type.Members);
            if (found is not null)
            {
                return found;
            }
        }

        return FindConstInMembers(name, site.SyntaxTree.GetCompilationUnitRoot().Members);
    }

    private static ExpressionSyntax? FindConstInMembers(string name, SyntaxList<MemberDeclarationSyntax> members)
    {
        foreach (var member in members)
        {
            if (member is not FieldDeclarationSyntax field || !field.Modifiers.Any(SyntaxKind.ConstKeyword))
            {
                continue;
            }

            foreach (var variable in field.Declaration.Variables)
            {
                if (variable.Identifier.Text == name && variable.Initializer is not null)
                {
                    return variable.Initializer.Value;
                }
            }
        }

        return null;
    }

    /// <summary>Resolves a <c>Skip</c> value expression — a string literal (regular, verbatim, or
    /// raw), a <c>+</c>-concatenation of literals and/or identifiers, or a bare identifier — into its
    /// final string. An identifier is resolved via <see cref="FindConstDeclaration"/> from the
    /// identifier's own position in the tree, so a const-to-const reference inside another const's
    /// initializer resolves lexically from that initializer's site, not from the original <c>Skip</c>
    /// usage. Returns <c>null</c> for anything it cannot resolve (an identifier with no reachable
    /// declaration, a cycle, a non-string expression) so the caller can fall back to the raw
    /// expression text as the violation's reason.</summary>
    private static string? ResolveExpression(ExpressionSyntax expression, HashSet<string> resolving)
    {
        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                return ResolveExpression(parenthesized.Expression, resolving);

            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
                return literal.Token.ValueText;

            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression):
                var left = ResolveExpression(binary.Left, resolving);
                var right = ResolveExpression(binary.Right, resolving);
                return left is null || right is null ? null : left + right;

            case IdentifierNameSyntax identifier:
                var name = identifier.Identifier.Text;
                if (!resolving.Add(name))
                {
                    return null;
                }

                var declaration = FindConstDeclaration(name, identifier);
                var resolved = declaration is null ? null : ResolveExpression(declaration, resolving);
                resolving.Remove(name);
                return resolved;

            default:
                return null;
        }
    }
}
