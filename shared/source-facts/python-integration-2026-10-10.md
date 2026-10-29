# Python packaging and skill integration verification

Date: 2026-10-10. Local Windows verification of the experimental Python adapter (`0.2.0-experimental`), the development `codemetrics-ai` npm bundle (`0.5.0`), and the companion code-scorecard skill checkout. Nothing was published by this pass. Published npm 0.5.0 does not include these additions.

## Delivered behavior

- `codemetrics-python` accepts a directory, `pyproject.toml`, or Python source file. The packaged Python 3.12 parser runs in isolated standard-library mode without importing or executing target code. There is no target install/build step or separate Python dependency installation.
- Standard evidence v3 carries fresh run/audit IDs, embedded source-facts v2, selection inventory, diagnostics, informational CC review findings, and an explicitly experimental preview computed by the canonical shared engine.
- All nine dimensions are unscored. Empty selections receive no preview; incomplete analysis retains diagnostic facts but no usable preview or findings. CSV, overall grading, calibrated comparisons, and score gates remain unsupported.
- The shared validator checks embedded facts and provenance, reproduces preview/population results, and rejects promoted dimension scores. The existing expected-ID checks reject stale findings copied to a new output path.
- The companion helper discovers Python manifests, supports explicit source scopes and population overrides, and writes run/inspection artifacts through the existing isolated tooling workflow. It requires an explicit local npm archive until a coordinated release.
- Documentation explains population boundaries, types being unmeasured, developer-intent limits, and separation of preview from scores. The skill does not duplicate scoring arithmetic.

## Verification

| Check | Result |
| --- | --- |
| Python AST and population tests | 23 passed |
| Shared producer/contract conformance | 7 passed |
| JS/TS regression suite | 256 passed |
| Installed Python evidence workflow | 9 passed |
| Companion skill integration, runtime, rule-catalog, and offline evaluation-harness tests | 40 passed, none skipped |
| Installed JS/TS package smoke | Passed |
| Frozen JS/TS calibration fixtures | 6 passed |
| Skill structural validator | Passed |
| Both working-tree whitespace checks | Passed |

The first JS/TS regression attempt placed temporary fixtures inside this Git checkout and failed 18 repository-root/path expectations. Moving test temp directories to `E:/temp/codemetrics-tests` restored all 256 tests without changes to JS/TS measurement code. Subsequent installed-package and integration verification used that external temporary root. No live-model skill-report evaluation was run; the offline harness checks do not establish model reporting behavior.

The installed Python CLI was also rerun against all four clean, pinned selections in [the corpus manifest](../calibration/corpus/python-selection.json). Full function facts and selection inventories exactly matched the earlier verified documents. Every resulting evidence file passed the packaged inspector. No source or selection changes were made in these projects.

| Project | Implementations | Experimental preview | Evidence run ID |
| --- | ---: | ---: | --- |
| Requests | 246 | 7.0 | `d930dc2c-d593-4c99-a792-608a5521b6cf` |
| Starlette | 508 | 7.1 | `c2832103-44db-4793-a114-784207e78815` |
| HTTPie | 464 | 6.8 | `685b5575-f0e3-4fad-9194-06f23d2f9bd9` |
| Django | 9,748 | 5.6 | `1d7c0b96-c2ac-43a9-a11b-f387a57b4860` |

These direct CLI runs use the same UUID for run and audit; skill-driven polyglot runs have distinct per-ecosystem run IDs under a shared audit ID. These previews are not overall grades or calibrated cross-language scores.

Local artifacts are under `TestResults/python-integration/`: `packages/`, `corpus/` (evidence, inspections, summary), and `js-calibration.json`. The archive used by the full skill suite and corpus rerun has SHA-256 `812258de577dfff5fdd14582621c446c7d13d80c0fba431a14ecc1cb0a42e5be`.

## Release boundary

CI is configured to test Python packaging on Windows and Linux; this record confirms local Windows execution only. The companion skill's existing released source pin remains unchanged while this coordinated work is uncommitted. Its new CI step explicitly skips Python against a source revision lacking the command, rather than claiming older-package coverage. Advance the source pin and publish a new npm version together after the changes are committed and remotely available; only then remove the skill's local-package requirement. Python calibration, additional dimensions, coverage/CRAP, and duplication remain separate work.
