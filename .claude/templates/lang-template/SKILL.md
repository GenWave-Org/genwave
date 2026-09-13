---
name: lang-template
description: TEMPLATE — copy this folder to create a language-specific best-practices skill (Python, Go, Rust, Ruby, Elixir, etc.). Rename the folder, update the frontmatter, fill in the sections, then delete this template.
---

# [Language Name] Best Practices

> 🛠️ **This is a template.** Copy this folder to
> `.claude/skills/<language>-best-practices/`, rename it, and fill in.
> Then delete this file (`lang-template/`). The `description` line above
> controls when Claude surfaces this skill — be specific about *when* it
> applies.

## 🎯 Why: Design for Change

The goal of writing software is to be able to **change it safely**. Fill
the sections below with the language idioms that make the *next* edit
small: strict types, narrow error paths, intent-revealing names, stable
module seams. If a convention you're tempted to add doesn't shrink the
next diff, leave it out.

## How to use this template

1. Copy: `cp -r lang-template python-best-practices`
2. Rewrite the frontmatter `name` and `description` (concrete triggers:
   "use when writing Python, reviewing a `.py` file, or…").
3. Fill in the sections below.
4. Delete the original `lang-template/` so it doesn't load by accident.

---

## Compiler / runtime settings

[Strictness flags, version pins, the one-file config you always start with.]

## Type system & modeling

[Discriminated unions / sum types / branded types / value objects — whatever
your language gives you. Show the idiomatic shape.]

## Error handling

[Exceptions vs. Result types vs. tuples. State the project rule, not the
language's options.]

## Null / nil / None hygiene

[How to model "missing." How to never accept `None` at a function boundary.]

## Async / concurrency rules

[Promises / async-await / goroutines / channels / fibers — and the rules
about cancellation, timeouts, leaks.]

## Module & file conventions

[Naming, file layout, what goes in one file vs. many.]

## Testing conventions

[Test runner, file naming, fixture style, what shape an assertion takes.]

## Standard rules for every type / class / module

[e.g. every class exposes `__repr__` + `to_dict`. Every module exports a
typed public API. Whatever's load-bearing for you.]

## Templates

Drop ready-to-copy files in `templates/`:

- `templates/strict.config.ext` — the project-default compiler/linter config
- `templates/result.ext` — your Result/Either type
- `templates/value-object.ext` — value-object pattern
- `templates/entity.ext` — entity pattern
- `templates/app-error.ext` — error hierarchy

## References

Drop deeper-dive markdown in `references/`:

- `references/idioms.md` — language-specific idioms
- `references/anti-patterns.md` — what to avoid and why
