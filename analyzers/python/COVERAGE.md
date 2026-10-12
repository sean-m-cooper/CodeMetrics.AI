# Experimental Python coverage and CRAP review

This offline first pass imports an existing coverage.py JSON report and produces per-function **conditional review evidence**. It does not run tests, install target dependencies, or execute target code. It is not yet bundled in `codemetrics-python` or consumed by the scorecard skill. Source-facts v2, packaged adapter versions, complexity previews, and all nine unscored Python dimensions remain unchanged.

```powershell
# Run from the CodeMetrics.AI checkout; Python 3.12 is required.
py -3.12 analyzers/python/coverage_review.py E:/repos/my-project --include 'src/**' --coverage-json E:/repos/my-project/coverage.json --output TestResults/python-coverage-review.json
```

The target is a directory or Python source file. A file uses its parent as the root. Coverage file keys must be relative to this exact root. Generate reports from that root with your existing coverage workflow; do not move a report from another checkout and assume it still matches. Both `/` and Windows `\` separators are accepted, with case-sensitive exact path matching. Absolute paths and parent traversal are rejected. There is no basename or suffix matching. Extra report files are listed in `summary.unmatchedReportFiles`; selected files absent from the report stay unavailable.

The CLI accepts the source analyzer's include/exclude and population flags, plus optional UUID `--run-id` and `--audit-id`. Output must be a new `.json` file with an existing parent. Without `--coverage-json`, it emits explicit unavailable coverage for each function. A specified but missing or malformed report is a failure, not an absent optional measurement.

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

**coverage.py JSON does not bind its observations to source hashes.** Every imported report therefore has `sourceBinding: unverified` and `freshness: unverified`. Every numeric estimate has `disposition: conditional-review-only`. A parseable timestamp, a matching filename, or a new review run ID cannot establish current coverage. Even unchanged line counts cannot detect a same-size source edit. The importer also cannot prove that automated assertions, rather than manual execution, produced the report.

The artifact records the exact report's SHA-256, reported timestamp/version/format, fresh review identities, current source hashes, parser and complexity policy, and selection fingerprint. Source rereads must match the current population pass. These checks establish what inputs were reviewed and consistency between current reads; they do not authenticate the historical test run. A collection-time provenance workflow is needed before promoting this evidence to verified current coverage.

`status: complete` means the offline import and analysis completed, not that coverage is fresh, complete, or usable for scoring. A parse/read failure, duplicate or conflicting JSON/path/line data, unsupported format, selected-file line beyond the current source, source drift during analysis, or resource-limit breach makes the report incomplete. Incomplete output has diagnostics, no function results, and no summary. Exit codes are 0 for complete and 2 for failure. Resource budgets are 32 MiB of report JSON, 10,000 report files, 100,000 selected functions, and the existing source-selection budgets.

## Verification and next step

The [authored fixture](tests/fixtures/coverage/README.md) includes a real coverage.py 7.6.1 format-3 report generated on Python 3.12, including the never-called one-line function trap. Tests also cover format-2 field compatibility, formula boundaries, absent reports/files, exclusions, source selection, exact paths, stale-shaped inputs, malformed input, current-source drift, fresh IDs, and output preservation. Tests consume the frozen report without installing coverage.py or executing fixture code.

```powershell
py -3.12 -m unittest discover -s analyzers/python/tests -v
```

Next: establish collection-time provenance and review real test-generated coverage on a small pinned corpus before packaging this evidence or considering calibration. No coverage percentages or CRAP results are inferred for Requests, Starlette, HTTPie, or Django from source alone.
