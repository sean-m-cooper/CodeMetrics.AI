# Experimental Python coverage and CRAP review

The reviewer imports a coverage.py JSON report and produces per-function review evidence. Without a collection receipt, estimates remain **conditional**. The separate, explicit collector runs a caller-selected test module and binds its newly generated report to source observations before and after execution. Neither command installs dependencies. The development package now embeds this workflow in `codemetrics-python` (adapter `0.5.0-experimental`) and exposes explicit test collection as `codemetrics-python-collect`. The scorecard skill imports the artifacts and reports their limitations. Source-facts v2 and its producer `0.4.0-experimental`, complexity previews, and all nine unscored Python dimensions remain unchanged. Published npm 0.5.0 does not include these commands; use a newly built local archive.

## Packaged workflow

```powershell
codemetrics-python --project E:/repos/my-project --include 'src/**' --coverage-json E:/temp/my-project-coverage/coverage.json --collection-manifest E:/temp/my-project-coverage/provenance.json --scorecard-output evidence.json
codemetrics-evidence --input evidence.json --inspect-output inspection.json
```

Coverage is optional. Evidence embeds the review at `dimensions.codeQuality.coverageReview`, sharing the exact source population and invocation IDs with complexity and duplication. Missing coverage produces explicit unavailable rows. Incomplete coverage makes the combined run incomplete and withholds all findings and the complexity preview. The shared validator checks identities, source membership/digests, line accounting, CRAP arithmetic, receipt consistency, and unavailable/conditional/source-bound summaries. It never executes tests or rereads current source when validating saved evidence; a historical evidence import retains only the binding established at generation time. Package validation is an integrity check, not cryptographic authentication of an unsigned receipt.

The helper forwards `--python-coverage-json` and `--python-collection-manifest`; it does not invoke the collector. All dimension scores and overall scores remain unavailable. There is no CSV or score gate.

The standalone reviewer remains available:

```powershell
# Run from the CodeMetrics.AI checkout; Python 3.12 is required.
py -3.12 analyzers/python/coverage_review.py E:/repos/my-project --include 'src/**' --coverage-json E:/repos/my-project/coverage.json --output TestResults/python-coverage-review.json
```

The target is a directory or Python source file. A file uses its parent as the root. Coverage file keys must be relative to this exact root. Generate reports from that root with your existing coverage workflow; do not move a report from another checkout and assume it still matches. Both `/` and Windows `\` separators are accepted, with case-sensitive exact path matching. Absolute paths and parent traversal are rejected. There is no basename or suffix matching. Extra report files are listed in `summary.unmatchedReportFiles`; selected files absent from the report stay unavailable.

The CLI accepts the source analyzer's include/exclude and population flags, plus optional UUID `--run-id` and `--audit-id`. Output must be a new `.json` file with an existing parent. Without `--coverage-json`, it emits explicit unavailable coverage for each function. A specified but missing or malformed report is a failure, not an absent optional measurement.

## Collect new coverage with source provenance

Use Python 3.12 with coverage.py and the project's test dependencies already installed in an isolated environment. Validation covers `coverage==7.6.1` and `coverage==7.16.1`. The collector requires a project directory, an explicit new output directory outside that project with an existing parent, and an interpreter path. It executes pytest by default; `--module` selects another Python test module. Arguments after `--` belong to that module.

```powershell
codemetrics-python-collect E:/repos/my-project --include 'src/**' --python E:/temp/my-project-tests/Scripts/python.exe --output-directory E:/temp/my-project-coverage-001 -- -q tests
codemetrics-python --project E:/repos/my-project --include 'src/**' --coverage-json E:/temp/my-project-coverage-001/coverage.json --collection-manifest E:/temp/my-project-coverage-001/provenance.json --scorecard-output TestResults/my-project-evidence.json
```

This is an explicit **test execution** workflow. The source analyzer and offline reviewer still never import or execute target code. Collector policy `python/coverage-collection-v1`:

1. Inventories and hashes selected source before invoking tests.
2. Creates new coverage data in a separate process with a unique bytecode cache, avoiding reuse of stale target bytecode. It never combines or binds pre-existing coverage data.
3. Runs the test module from the project root and records its command, exit status, Python/coverage versions, and output log. The default timeout is 1,800 seconds; `--timeout` must be positive and finite. A timeout terminates the collector's child process tree and records failure.
4. Generates fresh branch-enabled coverage JSON, then repeats the source snapshot. Selected file additions, removals, content changes, or population changes invalidate the collection. Ignored cache-directory creation does not.
5. Writes `provenance.json` with a collection UUID, start/end timestamps, source snapshot digests, selection rules/inventory, and the exact JSON report digest. Failed tests, missing tooling, failed reporting, source drift, and timeout leave an incomplete receipt that cannot authenticate review results.

The controlled coverage configuration sets `branch=True`, `config_file=False`, and source to the selected project root. Project coverage configuration and inherited `COVERAGE_*`/`PYTHON*` variables are not used. Pytest's automatic third-party plugin loading and inherited `PYTEST_ADDOPTS` are disabled to avoid competing coverage sessions; required plugins can be explicitly passed with `-p`. Project pytest configuration still applies. This first collector measures a **single process**; worker/subprocess coverage aggregation is unsupported. Coverage's version-specific default exclusions and inline pragmas still apply and remain visible in the report.

The offline reviewer accepts `--collection-manifest` only with a report. It checks successful completion, runtime/configuration, root, source policy/parser, exact report hash, and current selected source against both recorded snapshots. Any mismatch makes the entire review incomplete with no estimates; it never silently falls back to an unverified import. A valid binding is labeled `matched-pre-post-snapshots`, with freshness `source-matched`. `collectionProvenance.collectionId` identifies the test collection; new review run/audit IDs identify only the later review. No timestamp is refreshed to imply new tests.

Bound rows use `coverage.status: reported-source-matched` and `crapEstimate.disposition: review-only`; unbound rows retain `reported-unverified` and `conditional-review-only`. Summary counts separate `sourceBoundEstimates` from `conditionalEstimates`. The embedded review schema is 2, producer `0.3.0-experimental`, which embeds the collection receipt for offline integrity checks. Earlier standalone reports remain historical output. The line measurement policy and arithmetic are unchanged. The source checkout also retains `coverage_collect.py` and `coverage_review.py` for direct Python use.

## What is measured

Policy `python/coveragepy-line-crap-review-v1` combines current `python/owned-function-complexity-v2` measurements with reported executable **line** coverage. It reads coverage.py JSON formats 2 and 3 and uses file-level executed, missing, and excluded line sets. Function names and reported summary percentages are not trusted for mapping. Branch counts, contexts, and function/class summary regions are not used in the calculation.

The denominator is executed plus missing body lines, after removing coverage exclusions. Definition headers, decorators, parameters, annotations, and a leading docstring do not add coverage credit. Coverage exclusions remain visible and do not suppress complexity decisions. Multiline statements do not get credit for every physical line, and several statements sharing one line remain one coverage observation. This is a limitation of line coverage, not a replacement for statement-based size measurements.

Attribution is deliberately narrow:

- Ordinary multi-line functions, methods, and async functions can receive estimates.
- Lambdas and bodies sharing a definition/header line are unavailable. Definition execution is not proof of a call.
- Bodies sharing their first executable line with a leading docstring are unavailable.
- Parents containing nested functions, classes, or lambdas are unavailable. Independently selected nested function bodies can still receive estimates.
- Empty, docstring-only, or entirely excluded bodies without reportable lines are unavailable, not 0% or 100% covered.
- Missing coverage is unavailable. A body explicitly reported entirely missing is 0% covered.

For attributable bodies:

```text
conditional CRAP estimate = CC² × (1 − coveredLines / executableLines)³ + CC
```

For CC 10, coverage of 0%, 50%, and 100% produces 110, 22.5, and 10 respectively. Full coverage never removes the underlying complexity. Values are calculated from integer counts without rounding the coverage fraction first. No threshold, grade, project aggregate, score deduction, or test-quality claim is attached.

The [original CRAP proposal](https://www.artima.com/weblogs/viewpost.jsp?thread=215899) uses this arithmetic with basis-path coverage. Our executable-line substitution is explicitly labeled `line-coverage-proxy`; it does not establish equivalent calibration. The [coverage.py JSON documentation](https://coverage.readthedocs.io/en/latest/commands/cmd_json.html) describes report generation. Its [format-3 producer](https://github.com/nedbat/coveragepy/blob/7.6.1/coverage/jsonreport.py) records file line observations and region summaries; branch-enabled percentages can combine line and branch information. We calculate a line-only fraction instead of importing that percentage.

## Provenance and failure boundaries

**coverage.py JSON does not bind its observations to source hashes.** Imports without a valid collection receipt therefore retain `sourceBinding: unverified`, `freshness: unverified`, and conditional estimates. A parseable timestamp, a matching filename, or a new review run ID cannot establish current coverage. Even unchanged line counts cannot detect a same-size source edit. The importer also cannot prove that automated assertions, rather than manual execution, produced an arbitrary report.

The artifact records the exact report's SHA-256, reported timestamp/version/format, fresh review identities, current source hashes, parser and complexity policy, and selection fingerprint. Source rereads must match the current population pass. Source hashes use the existing decoded-text, normalized-newline convention; report hashes cover raw bytes. Optional collection provenance binds these inputs to the recorded test invocation through matching source observations.

Receipts are local and unsigned. They protect against accidental stale source/report reuse, not deliberate joint falsification of a report and receipt. Pre/post snapshots cannot detect source edits reverted during execution and are not an immutable filesystem. Binding covers selected source, not unselected tests, dependency/environment changes, or test assertion quality. `source-matched` means the selected source still matches that historical collection; it does not claim a recent test run or current runtime behavior. No coverage or CRAP measurement proves correctness.

`status: complete` means the offline import and analysis completed, not that coverage is fresh, complete, or usable for scoring. A parse/read failure, duplicate or conflicting JSON/path/line data, unsupported format, selected-file line beyond the current source, source drift during analysis, or resource-limit breach makes the report incomplete. Incomplete output has diagnostics, no function results, and no summary. Exit codes are 0 for complete and 2 for failure. Resource budgets are 32 MiB of report JSON, 10,000 report files, 100,000 selected functions, and the existing source-selection budgets.

## Verification and next step

The [authored fixture](tests/fixtures/coverage/README.md) includes a real coverage.py 7.6.1 format-3 report generated on Python 3.12, including the never-called one-line function trap. Offline tests cover format-2 field compatibility, formula boundaries, absent reports/files, exclusions, source selection, exact paths, malformed input, source drift, fresh IDs, and output preservation. Collection tests add same-size edits, report substitution, source additions/deletions, failed receipts, parser/selection mismatch, cache creation, and output reuse. Four integration tests execute only authored fixtures, covering a successful integer-enum exit, async/threaded body attribution, failures, mutation, and process timeout. CI defines coverage.py 7.6.1 and 7.16.1 on Ubuntu and Windows; without coverage.py, those four tests explicitly skip.

```powershell
# In a Python 3.12 environment with coverage==7.6.1 installed:
python -m unittest discover -s analyzers/python/tests -v
```

The [Requests validation](../../shared/scorecard-schema/calibration-runs/python-coverage-provenance-2026-10-12.md) exercised the collector against a pinned real checkout: 255 tests passed, one skipped, and 238 function estimates were source-bound. This was a three-module test slice, not a full Requests test-coverage assessment. The substituted real report was rejected. Package and skill integration retain these boundaries; broader test-suite/platform validation and any dimension-score calibration remain separate work.

The [Starlette validation](../../shared/scorecard-schema/calibration-runs/python-coverage-starlette-2026-10-13.md) exercised asyncio and Trio tests with coverage.py 7.16.1. Seven full-suite Windows failures reproduced without collection and correctly withheld estimates. A separately labeled subset passed 1,338 tests and produced 459 source-bound estimates; it does not represent a passing full-suite run. Both coverage versions passed all 64 Python regression tests locally on Windows.
