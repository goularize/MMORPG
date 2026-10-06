<!-- Target branch: `develop`. Only release (develop -> main) and hotfix PRs target `main`. Title: Conventional Commit, e.g. `feat(server/combat): add crit rolls`. -->

## Description
<!-- Provide a clear, concise summary of the changes made and the motivation behind them. -->

Refs #(issue) <!-- topic PRs into develop use Refs; the release PR into main uses Closes -->

## Type of Change
- [ ] Bug fix (non-breaking change which fixes an issue)
- [ ] New feature (non-breaking change which adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to not work as expected)
- [ ] Code refactor / Performance optimization
- [ ] Documentation update
- [ ] Tests (unit, integration, or simulation tests)

## Architectural Compliance Checklist
- [ ] **Shared Code Integrity**: All network packets, OpCodes, enums, and shared data models are located in `Shared/` and **never duplicated** between client and server.
- [ ] **Authoritative Game Loop**: Network packet handlers do **not** run physics, combat, or movement math directly; actions are queued or set as intentions and executed synchronously in `GameLogic.Update()`.
- [ ] **Zero-Duplication Linking**: Tested that Unity links against `Shared/` properly via directory junctions.
- [ ] **Security**: No secrets or hardcoded passwords; passwords use BCrypt hashing; all client inputs are validated on the server.
- [ ] **Tests**: New or modified logic is covered by unit or regression tests in `Tests/`.
- [ ] **Code Quality**: Follows C# 12 / .NET 8 idioms and passes `dotnet build` without warnings/errors.
