using System.Diagnostics;
using System.Text;
using GenWave.Host.Icons;
using GenWave.Host.Tests.Support;

namespace GenWave.IconPackAuthor;

/// <summary>
/// The whole author pipeline — source SVGs + a name mapping → a <c>Validate</c>-proven
/// <c>gw-icon-pack</c> document (PLAN T305, STORY-338 AC1). Pure aside from reading the mapped SVG
/// files themselves: no output I/O here, so <see cref="Program"/>'s <c>author</c> command and
/// <see cref="SelfTest"/>'s scenarios both drive it identically, whether the result is about to be
/// written to disk or just asserted against.
///
/// <para>
/// <b>WHOLE-RUN REJECT, NOT PER-GLYPH WITHHOLD</b> — mirrors <c>IconPackDefinitionParser</c>'s own
/// "WHOLE-DOCUMENT REJECT, NOT PER-ICON WITHHOLD" posture one layer upstream of it (see that type's
/// own remarks): one bad glyph in a mapped set fails the ENTIRE run, every offending glyph named,
/// rather than silently shipping the good ones alone — exactly the "fail loudly, never silently strip
/// semantics" instruction PLAN T305 was built under. Every failing glyph's reason is collected, not
/// just the first, so a curator fixing a batch sees the whole picture in one pass.
/// </para>
/// </summary>
public static class PackAuthoringPipeline
{
    public static PackAuthoringOutcome Run(
        string sourceDir, IReadOnlyDictionary<string, string> mapping, string? fillOverride, double? strokeWidthOverride)
    {
        var reasons = new List<string>();
        var successes = new List<(string SourceFile, string IconName, GlyphConversionResult.Success Result)>();
        var claimedBy = new Dictionary<string, string>(StringComparer.Ordinal); // icon name -> first claiming source file

        // Filename-sorted: deterministic processing order, so "the first glyph that states a style
        // opinion" (PackStyleInference) never depends on a Dictionary's own enumeration order.
        foreach (var (sourceFile, iconName) in mapping.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var sourcePath = Path.Combine(sourceDir, sourceFile);
            if (!File.Exists(sourcePath))
            {
                reasons.Add($"{sourceFile}: source file not found under {sourceDir}");
                continue;
            }

            switch (SvgGlyphConverter.Convert(sourcePath))
            {
                case GlyphConversionResult.Success success:
                    if (claimedBy.TryGetValue(iconName, out var firstClaimant))
                    {
                        reasons.Add(
                            $"{sourceFile}: mapped to icon name '{iconName}', already claimed by {firstClaimant} " +
                            "— two source files cannot share one icon name");
                        break;
                    }

                    claimedBy[iconName] = sourceFile;
                    successes.Add((sourceFile, iconName, success));
                    break;

                case GlyphConversionResult.Failure failure:
                    reasons.Add(failure.Reason);
                    break;
            }
        }

        if (reasons.Count > 0)
            return new PackAuthoringOutcome.Failure(reasons);

        var style = PackStyleInference.Resolve(
            successes.Select(entry => entry.Result.StyleHint).ToList(), fillOverride, strokeWidthOverride);
        var icons = successes.ToDictionary(
            entry => entry.IconName, entry => entry.Result.Elements, StringComparer.Ordinal);
        var definition = new IconPackDefinition(style, icons);

        // The "zero drift" proof: build the model, serialize it through the REAL canonical serializer,
        // then validate THOSE bytes through the REAL parser — the exact round trip
        // Api.IconPackController.Install runs at install time, run here offline instead.
        var canonicalJson = IconPackAuthoringGateway.Serialize(definition);
        var canonicalBytes = Encoding.UTF8.GetBytes(canonicalJson);

        return IconPackAuthoringGateway.Validate(canonicalBytes) switch
        {
            IconPackValidationResult.Valid valid => new PackAuthoringOutcome.Success(canonicalJson, valid.Definition, valid.IgnoredNames),
            IconPackValidationResult.Invalid invalid =>
                new PackAuthoringOutcome.Failure([FormatValidationFailure(invalid.Reason, claimedBy)]),
            var unhandled => throw new UnreachableException($"Unhandled {nameof(IconPackValidationResult)} case: {unhandled}"),
        };
    }

    /// <summary>The real parser's <see cref="IconPackValidationResult.Invalid.Reason"/> names the
    /// offending icon by its NAME (e.g. <c>icon 'restore' declares no elements</c>) — it has no notion
    /// of source files at all, that mapping only exists here in the authoring pipeline. Per-glyph
    /// contract (STORY-338 AC1: every glyph failure names the offending source file) demands a curator
    /// be able to go straight from that name back to the SVG on disk that produced it, so this appends
    /// the full name→file mapping for the run rather than trying to regex the one name back out of an
    /// arbitrary parser message (a style-block failure like a bad <c>strokeWidth</c> names no icon at
    /// all, so the full mapping is the only form that covers every <see cref="IconPackValidationResult.Invalid"/>
    /// shape the parser can return).</summary>
    static string FormatValidationFailure(string reason, IReadOnlyDictionary<string, string> claimedBy)
    {
        var mapping = string.Join(", ", claimedBy
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"'{pair.Key}' <- {pair.Value}"));

        return $"emitted pack failed the real IconPackDefinitionParser: {reason} (icon name -> source file: {mapping})";
    }
}
