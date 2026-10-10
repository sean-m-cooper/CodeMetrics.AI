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

The adapter does not yet emit scorecard evidence v3, metrics CSV, or calibrated Python dimension scores. It is not integrated into the code-scorecard skill. Its optional score preview is experimental, and Python/JS/.NET scores must not be averaged or compared as equivalent measurements. Representative Python corpus calibration and production/test classification are prerequisites for production scoring.
