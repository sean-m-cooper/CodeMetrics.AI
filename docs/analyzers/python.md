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

The standalone [decomposition reviewer](../../analyzers/python/DECOMPOSITION.md) reports owned statement counts and maximum block nesting without scores. Its first pinned corpus study keeps size and nesting separate, excludes nested-body double counting, and documents long low-CC registration/initialization routines that caution against automatic length penalties. This evidence is not yet packaged or integrated into the skill.

The packaged [coverage and CRAP review](../../analyzers/python/COVERAGE.md) imports coverage.py JSON for per-function estimates at `codeQuality.coverageReview`. It distinguishes missing coverage from explicitly uncovered lines and withholds ambiguous attribution. The separate `codemetrics-python-collect` command executes a caller-selected test module and binds new coverage to before/after source snapshots. Analysis rejects supplied provenance that no longer matches; imports without provenance remain conditional. The skill presents this evidence without executing tests implicitly or assigning dimension scores. The collector has been validated on a three-module Requests test slice and a [Starlette async test selection](../../shared/scorecard-schema/calibration-runs/python-coverage-starlette-2026-10-13.md), including rejection of its failed full-suite receipt.

The [duplication review](../../analyzers/python/DUPLICATION.md) adds exact whole-body review leads, with a five-statement default minimum and explicit exclusions. It is now embedded in the development packaged CLI evidence and presented by the scorecard skill; it assigns no score.

See the [Python adapter guide](../../analyzers/python/README.md) for commands, decision-count policy, exclusions, and failure behavior, and the [shared contract](../../shared/source-facts/README.md) for the language extension workflow.

The adapter now classifies file populations and declaration-only bodies, records selection provenance in source-facts v2, and has pinned regression selections for Requests, Starlette, HTTPie, and Django. Path rules are explicit conventions with customer overrides; they do not claim complete runtime reachability or semantic resolution.

The development npm package includes `codemetrics-python`, which runs the Python 3.12 parser and shared engine and emits evidence v3 with embedded source facts and review evidence. The code-scorecard skill routes Python manifests, explicit files, and directories through the packaged CLI, validates current run/audit identities, and records failed attempts. This integration requires an explicitly supplied local npm archive until publication; the adapter is `0.5.0-experimental`, with the unchanged source-facts producer `0.4.0-experimental`.

There are no calibrated Python dimension scores, overall grade, metrics CSV, or score gates. Complete runs retain all nine dimensions as skipped/unavailable and show the optional complexity preview separately. Incomplete runs provide diagnostics only. Python/JS/.NET scores must not be averaged or compared as equivalent measurements. The first corpus checks measurement and population selection; broader Python calibration remains a prerequisite for production scoring.

The subsequent [complexity calibration study](../../shared/scorecard-schema/python-complexity-calibration.md) recommends retaining the ladder and weighting, with a focused OR-membership measurement revision before enabling a supported Method Complexity score. The approved OR-membership adjustment is now measurement policy `python/owned-function-complexity-v2`; guards, body logic, and structural alternatives remain counted. Broad category-removal experiments remain rejected and offline. Dimension scores remain unavailable.
