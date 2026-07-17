## What & why

<!-- One concern per PR. Link the issue this addresses. -->

Closes #

## Checklist

- [ ] `dotnet build GenWave.sln` — zero warnings
- [ ] `dotnet test GenWave.sln --filter "Category!=Integration"` green
- [ ] Admin UI touched? `npx tsc --noEmit && npm test && npm run build` green in `admin-ui/`
- [ ] Behavior change carries a test that fails without it
- [ ] I have read [CONTRIBUTING.md](../CONTRIBUTING.md) — the CLA bot will prompt on your first PR
