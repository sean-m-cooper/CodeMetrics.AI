# Rust Analyzer

The Rust analyzer is planned for the next implementation wave.

## Location

```text
analyzers/rust/
```

## Expected Scope

- Rust crates and workspaces.
- CLI, backend, systems, and WebAssembly-oriented repositories.
- Cargo metadata and dependency analysis.

## Shared Outputs

The analyzer will produce `.scorecard/rust/metrics.csv` and `.scorecard/rust/evidence.json` using the shared scorecard contract.
