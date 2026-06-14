# Ecosystem Registry

An **ecosystem id** is the stable identifier shared by an analyzer's source directory, its output directory, and its evidence (`tool.ecosystem`). Ids are lowercase, hyphenated, and never renamed.

| Ecosystem id | Analyzer location | Entry point (`subject.entryPoint`) | Output directory |
|---|---|---|---|
| `dotnet` | `analyzers/dotnet` | `.sln` / `.slnx` file | `.scorecard/dotnet/` |
| `javascript-typescript` | `analyzers/javascript-typescript` | `package.json` | `.scorecard/javascript-typescript/` |
| `python` | `analyzers/python` (planned) | `pyproject.toml` | `.scorecard/python/` |
| `rust` | `analyzers/rust` (planned) | `Cargo.toml` | `.scorecard/rust/` |

## Output path rule

Every analyzer writes its default outputs under `.scorecard/<ecosystem-id>/`:

```text
.scorecard/<ecosystem-id>/metrics.csv
.scorecard/<ecosystem-id>/evidence.json
```

This keeps polyglot repositories collision-free: each analyzer owns its subdirectory, and consumers discover evidence by globbing `.scorecard/*/evidence.json`.

## `subject.variant`

`variant` captures the analyzer-specific build/config flavor: the build configuration (`Debug`/`Release`) for `dotnet`, the tsconfig path for `javascript-typescript`. It is optional; omit it when the ecosystem has no meaningful variant.
