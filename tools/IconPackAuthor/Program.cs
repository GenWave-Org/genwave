// tools/IconPackAuthor — PLAN T305 (SPEC F130.7, STORY-338 AC1).
//
// Offline icon-authoring script: SVG source set + name mapping -> a schema-valid <slug>.icon.json
// (whitelist-conforming, proven through the REAL GenWave.Host.Icons.IconPackDefinitionParser/
// IconPackDefinitionSerializer — see GenWave.Host.Tests.Support.IconPackAuthoringGateway's own remarks
// for why this reaches those two `internal` types without touching src/) or a per-glyph failure naming
// the offending construct. Never an app surface — this never runs inside the shipped api image.
//
// Usage:
//   dotnet run --project tools/IconPackAuthor -- author --source <dir> --mapping <file> --out <dir>
//       --slug <slug> --license <text> --source-url <url> [--version <text>]
//       [--fill none|currentColor] [--stroke-width <n>] [--author <text>] [--description <text>]
//   dotnet run --project tools/IconPackAuthor -- validate <path-to-slug.icon.json>
//   dotnet run --project tools/IconPackAuthor -- self-test

using System.Diagnostics;
using GenWave.Host.Icons;
using GenWave.Host.Tests.Support;
using GenWave.IconPackAuthor;

if (args.Length == 0)
{
    PrintUsage();
    return 2;
}

var command = args[0];
var rest = args.Skip(1).ToArray();

try
{
    switch (command)
    {
        case "author": return RunAuthor(rest);
        case "validate": return RunValidate(rest);
        case "self-test": return SelfTest.Run();
        default: throw new IconPackAuthoringUsageException($"unknown command '{command}' (expected 'author', 'validate', or 'self-test')");
    }
}
catch (IconPackAuthoringUsageException ex)
{
    Console.Error.WriteLine($"Usage error: {ex.Message}");
    PrintUsage();
    return 2;
}

static int RunAuthor(string[] args)
{
    var options = IconPackAuthoringOptions.Parse(args);

    if (!Directory.Exists(options.SourceDir))
    {
        Console.Error.WriteLine($"No such source directory: {options.SourceDir}");
        return 2;
    }

    var mapping = NameMapping.Load(options.MappingPath);
    var outcome = PackAuthoringPipeline.Run(options.SourceDir, mapping, options.FillOverride, options.StrokeWidthOverride);

    switch (outcome)
    {
        case PackAuthoringOutcome.Failure failure:
            Console.Error.WriteLine(
                $"Icon pack authoring FAILED ({failure.Reasons.Count} glyph{(failure.Reasons.Count == 1 ? "" : "s")}):");
            foreach (var reason in failure.Reasons)
                Console.Error.WriteLine($"  - {reason}");
            return 1;

        case PackAuthoringOutcome.Success success:
            Directory.CreateDirectory(options.OutputDir);
            var iconPath = Path.Combine(options.OutputDir, $"{options.Slug}.icon.json");
            var metaPath = Path.Combine(options.OutputDir, $"{options.Slug}.meta.json");

            File.WriteAllText(iconPath, success.CanonicalJson);
            File.WriteAllText(metaPath, IconPackMetaSkeleton.Build(
                options.MetaAuthor, options.MetaDescription, options.License, options.SourceUrl,
                options.Version, DateOnly.FromDateTime(DateTime.UtcNow)));

            Console.WriteLine(
                $"Wrote {iconPath} ({success.Definition.Icons.Count} icon(s), " +
                $"style=fill:{success.Definition.Style.Fill} strokeWidth:{success.Definition.Style.StrokeWidth:0.###}).");
            Console.WriteLine($"Wrote {metaPath} (draft — PLAN T312 finishes it).");

            if (success.IgnoredNames.Count > 0)
            {
                Console.WriteLine(
                    $"WARN: {success.IgnoredNames.Count} name(s) outside the icon-name contract " +
                    $"(installed, never rendered by any UI slot today): {string.Join(", ", success.IgnoredNames)}");
            }

            return 0;

        default:
            throw new UnreachableException($"Unhandled {nameof(PackAuthoringOutcome)} case.");
    }
}

// Independent proof, separate from `author`'s own internal self-check (PackAuthoringPipeline already
// validates what it just built before ever writing a byte): reads bytes BACK OFF DISK — closing the
// gap between "the in-memory canonical JSON validated" and "the bytes File.WriteAllText actually
// wrote validate too" — and runs them through the exact same real IconPackDefinitionParser.Validate a
// curator's PR would ultimately be re-validated by at install time (Api.IconPackController.Install).
// Also the one place a curator can check a HAND-EDITED pack (T312) before opening a PR.
static int RunValidate(string[] args)
{
    if (args is not [var path])
        throw new IconPackAuthoringUsageException("validate <path-to-slug.icon.json>");

    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"No such file: {path}");
        return 2;
    }

    switch (IconPackAuthoringGateway.Validate(File.ReadAllBytes(path)))
    {
        case IconPackValidationResult.Valid valid:
            Console.WriteLine(
                $"VALID — {path}: {valid.Definition.Icons.Count} icon(s), " +
                $"style=fill:{valid.Definition.Style.Fill} strokeWidth:{valid.Definition.Style.StrokeWidth:0.###}.");
            if (valid.IgnoredNames.Count > 0)
                Console.WriteLine($"  names outside the icon-name contract: {string.Join(", ", valid.IgnoredNames)}");
            return 0;

        case IconPackValidationResult.Invalid invalid:
            Console.Error.WriteLine($"INVALID — {path}: {invalid.Reason}");
            return 1;

        default:
            throw new UnreachableException($"Unhandled {nameof(IconPackValidationResult)} case.");
    }
}

static void PrintUsage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine($"  dotnet run --project tools/IconPackAuthor -- {IconPackAuthoringOptions.UsageText}");
    Console.Error.WriteLine("  dotnet run --project tools/IconPackAuthor -- validate <path-to-slug.icon.json>");
    Console.Error.WriteLine("  dotnet run --project tools/IconPackAuthor -- self-test");
}
