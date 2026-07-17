# Contributing to GenWave

Thanks for wanting to make GenWave better. Bug reports, fixes, and features are all welcome — a short issue describing what you want to change is the best first step for anything bigger than a small fix.

## 📜 Contributor License Agreement (required)

GenWave Home is AGPL-3.0-only and always will be. Its development is funded by GenWave Business, a commercially licensed edition built on the same core. That model only works if the maintainer holds sufficient rights in every line of the core — so **every external contribution requires agreeing to the [Contributor License Agreement](CLA.md) before it can be merged**.

Signing is lightweight and one-time: a bot prompts on your first pull request and records your agreement in a comment. No paperwork. You keep full rights to use your own contributions for any other purpose.

## 🤖 AI-assisted development

GenWave is built openly with AI assistance — as a force multiplier for the people building it, not a replacement for them. Design decisions, reviews, and sign-offs are human; the repository's `.claude/` toolkit is part of the codebase and you're welcome to use it. You may use AI tools in your own contributions too, with the same deal we hold ourselves to: **you are responsible for what you submit.** It must meet the review bar, and you must have the right to contribute it under the CLA.

## 🛠️ Development

```bash
dotnet build GenWave.sln                                  # build
dotnet test GenWave.sln --filter "Category!=Integration"  # unit tests (no Docker)
dotnet test GenWave.sln                                   # full suite (Docker + ffmpeg)
cd admin-ui && npx tsc --noEmit && npm test && npm run build  # admin UI checks
```

See the [README](README.md) for prerequisites and how to run the full stack.

## ✅ Pull requests

- One concern per PR; conventional-commit style messages (`feat:`, `fix:`, `docs:`, `chore:`).
- Build and tests green, zero compiler warnings.
- Match the surrounding code's conventions — nullable reference types, one type per file, no `!` null-forgiving operator in production code.
- Behavior changes need a test that fails without them.

## 🔐 Security issues

Please do not open public issues for suspected vulnerabilities — report them privately to the maintainer.
