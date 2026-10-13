# Python coverage provenance validation: Requests

Date: 2026-10-12. This validates collection and source binding, not CRAP thresholds or a Requests scorecard. [Machine-readable evidence](python-coverage-provenance-2026-10-12.json) records input hashes, dependency versions, and example function measurements.

## Scope and result

- Project: Requests, revision `611c6162cbc4ac2020a2f91c7cfa4f3abf9bbb60`, clean checkout at `E:/repos/requests`.
- Source population: `src/requests/**`, the same production selection used by the existing Python corpus.
- Executed tests: `tests/test_utils.py`, `tests/test_structures.py`, and `tests/test_hooks.py` only.
- Isolated Python 3.12 environment at `E:/temp/codemetrics-requests-coverage`; coverage.py 7.6.1 and pytest 8.3.3. Full dependency versions are in the JSON record and archived environment file.
- Result: **255 passed, 1 skipped, 6 deprecation warnings**, exit 0.
- Review: **246 selected functions, 238 source-bound estimates, 8 unavailable**. Unavailable reasons: 4 nested definitions, 1 shared definition/body line, and 3 functions in an absent report file. No conditional estimates remain in this bound review.
- Collection: `45404943-bcab-4092-8a28-12dd2f2244fb`. Selected-source snapshot matches before tests, after report generation, and at review time.

This test slice does not cover the entire library. An unexecuted body in this report means it was not covered by these selected tests; it is not a claim that Requests has no tests for it. Neither estimates nor unavailable bodies affect dimension scores.

## Example observations

| Function in `utils.py` | CC | Covered / executable body lines | Line-based CRAP estimate |
| --- | ---: | ---: | ---: |
| `get_netrc_auth` | 16 | 21 / 30 | 22.912 |
| `super_len` | 16 | 30 / 32 | 16.0625 |
| `guess_json_utf` | 11 | 21 / 21 | 11 |
| `get_unicode_from_response` | 6 | 0 / 14 | 42 |

These demonstrate the arithmetic and its limits: full coverage leaves complexity intact, and incomplete test selection can produce high estimates. They are review evidence, not defect diagnoses or refactoring requirements.

## Checks and fixes

The first real execution exposed an incidental cache-directory creation being treated as source drift. Snapshots now bind selected source files, population/selection rules, and declaration exclusions; ignored cache creation is not source drift. The next attempt exposed pytest's successful `IntEnum` exit being misclassified as failure. The worker now honors Python's integer exit semantics, with a regression fixture.

The successful third collection used fresh data and output. Appending a newline to a copy of its report caused review to fail with `Coverage report differs from the collected report.` The failed review contained no function estimates and no summary. No real project source was modified for this check. Authored integration/unit tests independently check persistent source edits, same-line-count edits, additions/removals, selection/parser mismatch, failed tests, malformed receipts, timeouts, and output reuse. **63 Python tests passed**, including all three collection integration tests.

Local pre/post receipts are unsigned and cannot detect edits reverted during a test run. They bind selected source, not dependency state or assertion quality. The timestamps remain historical; a later review does not imply rerunning tests. The first collector does not aggregate subprocess coverage.

## Reproduce

With the pinned Requests checkout and the documented isolated environment prepared, choose a **new output directory**:

```powershell
py -3.12 analyzers/python/coverage_collect.py E:/repos/requests --include 'src/requests/**' --python E:/temp/codemetrics-requests-coverage/Scripts/python.exe --output-directory E:/temp/requests-coverage-new --timeout 180 -- -q tests/test_utils.py tests/test_structures.py tests/test_hooks.py
py -3.12 analyzers/python/coverage_review.py E:/repos/requests --include 'src/requests/**' --coverage-json E:/temp/requests-coverage-new/coverage.json --collection-manifest E:/temp/requests-coverage-new/provenance.json --output TestResults/requests-coverage-review-new.json
```

Full artifacts are archived locally under `TestResults/python-coverage-requests-2026-10-12/`: coverage JSON, collection receipt, successful review, substitution rejection, test log, and environment inventory. Their SHA-256 values are recorded in the checked-in JSON summary. Failed development attempts remain in separate `E:/temp/requests-coverage-provenance-2026-10-12*` directories; they were never promoted or overwritten. The full artifacts are intentionally not repository baselines.
