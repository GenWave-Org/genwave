---
name: csharp-best-practices
description: >-
  Idiomatic modern C# (.NET 9) conventions and best practices: nullable
  reference types everywhere with NO null-forgiving `!` operator in
  production code, records for values/DTOs, pattern matching and switch
  exhaustiveness, async/await rules (CancellationToken propagation, no
  `async void`, no `.Result`/`.Wait()`), constructor dependency injection,
  explicit error handling with specific exception types, file-scoped
  namespaces, and the house rules: one type per file (no exceptions), no
  underscore prefixes on fields, and zero compiler warnings. Use when
  writing new C#, reviewing a `.cs` file, setting up a new project,
  modeling a domain type, or answering "what is the idiomatic C# way to
  do this" questions. Ships ready-to-copy templates for value objects,
  entities, Result, an exception hierarchy, Directory.Build.props, and
  .editorconfig.
---

# C# Best Practices & Idioms

How to write C# that the compiler can actually protect: enable nullable
reference types and never silence them, model illegal states out of
existence with records and exhaustive switches, propagate cancellation,
and keep errors loud and specific.

## 🎯 Why: Design for Change

The goal of writing software is to be able to **change it safely**.
Nullable annotations, sealed records, exhaustive pattern matches, and
specific exception types turn the compiler into a change-detector: when
a shape moves, every caller lights up. The `!` operator, `#pragma
warning disable`, swallowed exceptions, and `object`-typed bags are
change-hiders — they make the next edit *look* safe while it isn't.

This skill is about *idioms and conventions*. For OO design pressure use
`solid-principles` and `design-principles`; for named patterns use
`gof-patterns`; for ASP.NET Core structure use `aspnetcore-patterns`.
This skill does not re-explain those.

## How to use this skill

1. Match the symptom in the decision guide to a rule.
2. Open `references/idioms.md` (or `references/async-patterns.md` for
   anything async) for that rule's full rationale, a bad example, the
   idiomatic refactor, and when *not* to apply it.
3. Copy the closest file from `templates/` and adapt it. The templates
   already encode the conventions below so you start compliant instead
   of retrofitting.
4. Apply the smallest change that removes real pain. KISS and YAGNI win
   ties — but the *defaults* here (nullable enabled, no `!`, no
   `async void`, warnings as errors) are non-negotiable because their
   failure mode is silent.

## House rules (non-negotiable, from DEVELOPMENT_BELIEFS.md)

These are hard conventions in this codebase, not suggestions:

1. **No `!` null-forgiving operator** in production code, ever. The only
   place `!` is acceptable is inside a unit test that is deliberately
   exercising a null path. If the compiler thinks something can be null,
   either prove it can't (restructure, pattern-match, guard) or handle
   the null. `!` is a lie to the compiler that becomes a
   `NullReferenceException` at 3 AM.
2. **One type per file.** One class/interface/enum/struct/record == one
   file. Scope doesn't matter, size doesn't matter. No exceptions.
3. **No underscore prefixes** on private fields. `private readonly
   ILogger logger;` not `_logger`. Disambiguate with `this.` in
   constructors.
4. **Zero compiler warnings.** No work is complete while a warning
   remains. Line-level `#pragma warning disable` only with a comment
   explaining why it is required; file-level pragmas require explicit
   sign-off and full comments.
5. **Don't swallow errors.** Empty `catch` blocks and `catch (Exception)
   { return null; }` are findings. Catch the specific exception you can
   actually handle; let the rest propagate; log with context at the
   boundary that owns the decision.
6. **Keep it small.** Files under ~300 lines, methods under ~30 lines,
   parameters ≤3 where reasonable (use a record to group them past
   that).

## Decision guide

| Symptom / question | Rule | Where |
|---|---|---|
| Compiler says "may be null" and you're tempted by `!` | Guard, pattern-match, or restructure — never `!` | idioms.md §1 |
| Class that's just data (DTO, message, config shape) | `sealed record`, positional or init-only | idioms.md §2 |
| Domain value with rules (Email, Duration, StreamUrl) | Value object record with factory validation | idioms.md §2, templates/ValueObject.cs |
| Object can be in contradictory states | Model states as a closed type hierarchy + exhaustive `switch` | idioms.md §3 |
| `switch` silently misses a new case | Switch *expression* + no discard arm on closed sets | idioms.md §4 |
| Throwing for an expected, recoverable failure | Return `Result<T>` | idioms.md §5, templates/Result.cs |
| `catch (Exception ex) { }` or log-and-continue | Catch specific types; rethrow or wrap with context | idioms.md §6, templates/AppException.cs |
| Two `string`/`int` IDs got swapped at a call site | Strongly-typed ID (readonly record struct) | idioms.md §7 |
| `string` comparisons, casing, culture surprises | Explicit `StringComparison`; invariant for machine data | idioms.md §8 |
| LINQ chain nobody can read / multiple enumeration | Materialize once; prefer clarity over cleverness | idioms.md §9 |
| Mutable collection exposed from a class | Return `IReadOnlyList<T>` / expose immutably | idioms.md §10 |
| `public string Name;` or settable-from-anywhere | Properties; `init` or `private set`; constructor invariants | idioms.md §11 |
| New service class wiring | Constructor DI, no service locator, no `new` for dependencies | idioms.md §12 |
| Magic strings/numbers sprinkled around | `const`/`static readonly`/enum — but only when used >once | idioms.md §13 |
| `async void`, `.Result`, `.Wait()`, missing `CancellationToken` | Async rules | async-patterns.md |
| Background work, channels, `IAsyncEnumerable`, timers | Async rules | async-patterns.md |
| New project / csproj settings | Strict baseline | templates/Directory.Build.props, templates/.editorconfig |

## Testing conventions (xUnit)

- xUnit is the test framework: `[Fact]` for single cases, `[Theory]` +
  `[InlineData]`/`[MemberData]` for parameterized ones.
- Test naming: `MethodOrBehavior_Condition_ExpectedOutcome` or plain
  descriptive sentences — pick what the project already uses and match.
- **Test with real things where possible.** Don't go mock crazy: if a
  mock takes longer to build than the thing under test, use the real
  implementation, an in-memory equivalent, or a tiny fake. Mocks define
  test reality, not production reality.
- If you implement or change program logic, you write or update tests in
  the same task. Not after. Not "in a follow-up."
- The `!` operator is permitted in tests only where the test is
  deliberately passing null to verify guard behavior.

## Reference files

- `references/idioms.md` — every rule above with motivation, a bad
  example, the idiomatic refactor, C#-specific notes, and when the rule
  is over-engineering.
- `references/async-patterns.md` — async/await, cancellation,
  `IAsyncEnumerable`, channels, `BackgroundService` pitfalls, timeouts,
  and the deadlock/`async void` failure modes in depth.

## Templates (copy and adapt)

| File | Use case |
|---|---|
| `templates/Directory.Build.props` | Drop at solution root: nullable on, warnings as errors, implicit usings, latest lang version |
| `templates/.editorconfig` | Naming rules (no underscores), style enforcement, analyzer severities |
| `templates/ValueObject.cs` | Immutable validated value (Email, Money) as a sealed record with factory |
| `templates/Entity.cs` | Domain entity with identity and guarded invariants |
| `templates/Result.cs` | `Result<T>` for expected failures + helpers |
| `templates/AppException.cs` | Exception hierarchy base: specific, contextual, serializable-safe |

## Non-negotiable defaults

These are silent-failure rules; apply them everywhere from day one:

1. `<Nullable>enable</Nullable>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in every project.
2. No `!` null-forgiving operator outside unit tests.
3. No `async void` (except event handlers, which should be one line into an async Task method).
4. Every async method that does I/O accepts and propagates a `CancellationToken`.
5. No `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` on a hot path — async all the way down.
6. External/untrusted data is validated at the boundary into a typed value, never blindly deserialized into a domain object.
7. One type per file; no underscore field prefixes; zero warnings.
