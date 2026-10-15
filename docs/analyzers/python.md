# Python Analyzer

Python has an experimental source-facts adapter at `analyzers/python/source_facts.py`. It uses Python 3.12's native AST and emits versioned function facts consumed by the same population engine as JS/TS, alongside separate review evidence.

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

The standalone [exception-handler reviewer](../../analyzers/python/ERROR_HANDLING.md) classifies selected `except`/`except*` suites, preserves local documentation, and records owned control-transfer syntax. Its [first corpus review](../../shared/scorecard-schema/calibration-runs/python-error-handling-2026-10-13.md) shows why no-op counts alone cannot justify deductions. It is not yet packaged or consumed by the skill; Error Handling remains unscored.

The packaged [decomposition reviewer](../../analyzers/python/DECOMPOSITION.md) reports owned statement counts and maximum block nesting without scores. Its first pinned corpus study keeps size and nesting separate, excludes nested-body double counting, and documents long low-CC registration/initialization routines that caution against automatic length penalties. The npm adapter embeds it at `dimensions.codeQuality.decomposition`; the skill reports separate size/nesting distributions and review candidates without deductions.

The packaged [coverage and CRAP review](../../analyzers/python/COVERAGE.md) imports coverage.py JSON for per-function estimates at `codeQuality.coverageReview`. It distinguishes missing coverage from explicitly uncovered lines and withholds ambiguous attribution. The separate `codemetrics-python-collect` command executes a caller-selected test module and binds new coverage to before/after source snapshots. Analysis rejects supplied provenance that no longer matches; imports without provenance remain conditional. The skill presents this evidence without executing tests implicitly or assigning dimension scores. The collector has been validated on a three-module Requests test slice and a [Starlette async test selection](../../shared/scorecard-schema/calibration-runs/python-coverage-starlette-2026-10-13.md), including rejection of its failed full-suite receipt.

The [duplication review](../../analyzers/python/DUPLICATION.md) adds exact whole-body review leads, with a five-statement default minimum and explicit exclusions. It is now embedded in the development packaged CLI evidence and presented by the scorecard skill; it assigns no score.

See the [Python adapter guide](../../analyzers/python/README.md) for commands, decision-count policy, exclusions, and failure behavior, and the [shared contract](../../shared/source-facts/README.md) for the language extension workflow.

The adapter now classifies file populations and declaration-only bodies, records selection provenance in source-facts v2, and has pinned regression selections for Requests, Starlette, HTTPie, and Django. Path rules are explicit conventions with customer overrides; they do not claim complete runtime reachability or semantic resolution.

The development npm package includes `codemetrics-python`, which runs the Python 3.12 parser and shared engine and emits evidence v3 with embedded source facts and review evidence. The code-scorecard skill routes Python manifests, explicit files, and directories through the packaged CLI, validates current run/audit identities, and records failed attempts. This integration requires an explicitly supplied local npm archive until publication; the adapter is `0.7.0`, with the unchanged source-facts producer `0.4.0-experimental`.

[Method Complexity](../../analyzers/python/METHOD_COMPLEXITY.md) is Python's first supported scored dimension. It uses selected production implementations, the accepted CC ladder, and one-worst 40% / remainder-mean 60% weighting with decimal half-up rounding. The other eight dimensions and all decomposition, duplication, and coverage/CRAP observations remain unscored. Empty populations are unavailable; incomplete runs provide diagnostics only. A reported aggregate is explicitly a partial assessment with 1/9 dimensions scored. Matching supported policies can compare scores; historical previews cannot. No cross-language score equivalence is claimed.

The subsequent [complexity calibration study](../../shared/scorecard-schema/python-complexity-calibration.md) recommends retaining the ladder and weighting, with a focused OR-membership measurement revision before enabling a supported Method Complexity score. The approved OR-membership adjustment is now measurement policy `python/owned-function-complexity-v2`; guards, body logic, and structural alternatives remain counted. Broad category-removal experiments remain rejected and offline. The accepted baseline now supports Method Complexity scoring without changing raw measurements.

The [packaged decomposition validation](../../shared/scorecard-schema/calibration-runs/python-decomposition-package-2026-10-13.md) records installed-package and skill checks, historical compatibility, and exact matches against all four pinned source selections.

The [Method Complexity promotion record](../../shared/scorecard-schema/calibration-runs/python-method-complexity-2026-10-13.md) documents accepted scoring scope, installed corpus results, compatibility, and skill verification.
