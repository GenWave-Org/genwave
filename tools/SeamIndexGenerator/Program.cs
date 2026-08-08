using GenWave.SeamIndexGenerator;

// WebApplicationFactory<Program>'s own content-root discovery (SeamCompositionSnapshot) looks for
// `MvcTestingAppManifest.json` via a RELATIVE path check against the process's current directory —
// exactly the file this project's own build drops next to its output assembly (verified: it maps
// "GenWave.Host, Version=..." to src/GenWave.Host's real path). `dotnet test` conventionally runs
// with the test assembly's own output directory as the working directory (why the identical
// mechanism just works for every GenWave.Host.Tests spec); a plain `dotnet run`/`dotnet exec`
// console entry point makes no such promise, so this pins it explicitly — portable regardless of
// where the tool is invoked from.
Directory.SetCurrentDirectory(AppContext.BaseDirectory);

// T217 (STORY-294 review, F2): optional output path so tools/check-seam-index.sh can generate to a
// scratch file and byte-diff it against the tracked SEAMS.md WITHOUT ever writing the tracked file
// itself — no-arg default behavior (write the committed root SEAMS.md) is unchanged, exactly what
// T216 shipped and what a plain `dotnet run --project tools/SeamIndexGenerator` still does.
var path = args.Length > 0 ? args[0] : Path.Combine(RepoRoot.Find(), "SEAMS.md");
var markdown = SeamIndexDocument.Generate();

File.WriteAllText(path, markdown);
Console.WriteLine($"Wrote {path} ({markdown.Length} chars).");
