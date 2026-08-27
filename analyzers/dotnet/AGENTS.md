# Working on CodeMetrics.AI

This solution currently has two human/AI collaborators. Keep changes direct, evidence-backed, and proportionate to the problem.

## Before changing code

- Read `docs/architecture.md` for the analysis flow and invariants.
- Treat scorecard findings as leads, not automatic refactoring orders. Confirm that a change improves behavior or clarity.
- Preserve raw metrics and schema-v2 evidence compatibility unless the task explicitly changes the public contract.

## Implementation rules

- Prefer semantic Roslyn checks over identifier-only heuristics.
- Keep probes deterministic and read-only.
- Propagate cancellation through I/O and external commands.
- Bound network access and concurrency.
- Use category-specific suppression comments only for reviewed intentional patterns.
- Add a regression test for every false-positive correction.

## Verification commands

```powershell
dotnet restore analyzers/dotnet/CodeMetrics.AI.slnx
dotnet test --solution analyzers/dotnet/CodeMetrics.AI.slnx --configuration Release --no-restore --verbosity minimal
dotnet format analyzers/dotnet/CodeMetrics.AI.slnx --no-restore --verify-no-changes
```

Generated `.scorecard` evidence is local audit output and should not be committed.
