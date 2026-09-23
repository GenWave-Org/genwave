// STORY-464 — Phone shape lives in Core (gh-#700 · SPEC F197.1 · PLAN T544) — AC2 half
//
// AC1 (the four NANP shapes matched by GenWave.Core.PhoneShape) lives in
// GenWave.Core.Tests/Specs/Story464_PhoneNumbersAreSpokenAsDigits_Core.cs; that file's header explains
// why AC2 moved here (referencing GenWave.Ads from Core.Tests makes the GenWave.Loudness namespace
// shadow the unqualified `Loudness` domain type there — CS0118). This project already references
// GenWave.Ads for its own fitness laws with no such collision.

using System.Reflection;
using System.Text.RegularExpressions;
using GenWave.Architecture.Tests.Support;

namespace GenWave.Architecture.Tests.Specs;

/// <summary>
/// STORY-464 AC2 — <c>GenWave.Ads</c> keeps no <c>\d{3}</c> phone-shape regex of its own; it
/// references <c>GenWave.Core.PhoneShape</c> (SPEC F197.1) instead. Two independent facts, each its
/// own scan so a failure names exactly which one tripped: <see
/// cref="NoGeneratedRegexInAdsPatternsContainThreeDigitGroup"/> reflects over the COMPILED assembly
/// for any <c>[GeneratedRegex]</c> pattern containing <c>\d{3}</c> (public or not —
/// <c>BindingFlags.NonPublic</c> reaches <c>GenWave.Ads.PhoneShapeCheck</c>'s own internal members
/// with no <c>InternalsVisibleTo</c> needed), and <see
/// cref="NoAdsSourceFileContainsAThreeDigitGroupToken"/> reads every <c>.cs</c> file under
/// <c>src/GenWave.Ads</c> for a bare <c>\d{3}</c> token — the <c>ConstructorCallScan</c> precedent
/// (<c>Support/ConstructorCallScan.cs</c>, STORY-451/460) for scanning source text directly rather
/// than compiled attribute metadata, so a regression written as a plain <c>new Regex(...)</c> or
/// <c>Regex.IsMatch(...)</c> literal — not just a <c>[GeneratedRegex]</c> attribute — also reds this.
/// </summary>
public static class FeatureAdsKeepsNoPhoneRegexOfItsOwn
{
    [Fact]
    public static void NoGeneratedRegexInAdsPatternsContainThreeDigitGroup()
    {
        const BindingFlags AnyDeclaredMember =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        var offendingPatterns = ProductionAssemblies.Ads.GetTypes()
            .SelectMany(type => type.GetMethods(AnyDeclaredMember))
            .SelectMany(method => method.GetCustomAttributes<GeneratedRegexAttribute>())
            .Select(attribute => attribute.Pattern)
            .Where(pattern => pattern.Contains(@"\d{3}", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offendingPatterns);
    }

    [Fact]
    public static void NoAdsSourceFileContainsAThreeDigitGroupToken()
    {
        var offendingFiles = Directory
            .EnumerateFiles(Path.Combine(SolutionLocator.Root(), "src", "GenWave.Ads"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split('/', '\\').Any(segment => segment is "bin" or "obj"))
            .Where(path => File.ReadAllText(path).Contains(@"\d{3}", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offendingFiles);
    }
}
