# Ecosystem Registry

An **ecosystem id** is the stable identifier shared by an analyzer's source directory, its output directory, and its evidence (`tool.ecosystem`). Ids are lowercase, hyphenated, and never renamed.

| Ecosystem id | Analyzer location | Entry point (`subject.entryPoint`) | Output directory |
|---|---|---|---|
| `dotnet` | `analyzers/dotnet` | `.sln` / `.slnx` file | `.scorecard/dotnet/` |
| `javascript-typescript` | `analyzers/javascript-typescript` | `package.json` | `.scorecard/javascript-typescript/` |
| `python` | `analyzers/python` (experimental) | `pyproject.toml`, explicit directory or `.py`/`.pyi` file | `.scorecard/python/` via skill; CLI requires explicit evidence path |
| `rust` | `analyzers/rust` (planned) | `Cargo.toml` | `.scorecard/rust/` |

## Output path rule

Every analyzer writes its default outputs under `.scorecard/<ecosystem-id>/`:

```text
.scorecard/<ecosystem-id>/metrics.csv
.scorecard/<ecosystem-id>/evidence.json
```

This keeps polyglot repositories collision-free: each analyzer owns its subdirectory, and consumers discover evidence by globbing `.scorecard/*/evidence.json`.

The experimental Python CLI writes only evidence JSON to a required new output path. The skill uses `.scorecard/python/runs/<run-id>/evidence.json` and validates run identity through its inspection; root-level files are not a freshness signal. Python measurements and an experimental preview are embedded in evidence v3; no dimension scores or CSV are emitted.

## `subject.variant`

`variant` captures the analyzer-specific build/config flavor: the build configuration (`Debug`/`Release`) for `dotnet`, `source` for `javascript-typescript` (compiler configuration is captured by `analysis.configurationFingerprint`). It is optional; omit it when the ecosystem has no meaningful variant.
