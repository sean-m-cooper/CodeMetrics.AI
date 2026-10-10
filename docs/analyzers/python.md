# Python Analyzer

Python has an experimental complexity-only adapter at `analyzers/python/source_facts.py`. It uses Python 3.12's native AST and emits versioned function facts consumed by the same population engine as JS/TS.

## Location

```text
analyzers/python/
```

## Planned Scope

- Python packages and applications.
- Backend frameworks such as FastAPI and Django.
- Script-heavy automation repositories.
- AI, data, and tooling codebases.

## Current Outputs

See the [Python adapter guide](../../analyzers/python/README.md) for commands, decision-count policy, exclusions, and failure behavior, and the [shared contract](../../shared/source-facts/README.md) for the language extension workflow.

The adapter now classifies file populations and declaration-only bodies, records selection provenance in source-facts v2, and has pinned regression selections for Requests, Starlette, HTTPie, and Django. Path rules are explicit conventions with customer overrides; they do not claim complete runtime reachability or semantic resolution.

The development npm package now includes `codemetrics-python`, which runs the Python 3.12 parser and shared engine and emits evidence v3 with embedded source facts. The code-scorecard skill routes Python manifests, explicit files, and directories through the packaged CLI, validates current run/audit identities, and records failed attempts. This integration requires an explicitly supplied local npm archive until publication; the adapter remains version `0.2.0-experimental`.

There are no calibrated Python dimension scores, overall grade, metrics CSV, or score gates. Complete runs retain all nine dimensions as skipped/unavailable and show the optional complexity preview separately. Incomplete runs provide diagnostics only. Python/JS/.NET scores must not be averaged or compared as equivalent measurements. The first corpus checks measurement and population selection; broader Python calibration remains a prerequisite for production scoring.
