# Python Method Complexity promotion - 2026-10-13

Adapter **0.7.0** promotes Method Complexity to Python's first supported scored dimension under the accepted [product policy](../../../analyzers/python/METHOD_COMPLEXITY.md). `codeQuality` is displayed as **Method Complexity**; the other eight dimensions remain unavailable. Statement count, block nesting, duplication, coverage, and CRAP remain unscored review evidence.

The policy uses selected production implementations exactly once, the accepted CC 3/5/10/20/40 ladder mapping to 10/8/6/4/0, linear interpolation, 40% on one worst function plus 60% on the remaining mean, singleton 100% weighting, and decimal half-up rounding. Included test/support/generated bodies do not enter the score. An empty production population is unavailable. Lambda complexity remains eligible even when decomposition or coverage is unavailable. These are product decisions for complexity coaching, not defect prediction or proof of safe extraction.

Evidence records `python/method-complexity-worst-40-remainder-60/v1`, ruleset `python-2026-10-13-method-complexity-v1`, and `calibration: baseline`. Raw measurement policy remains `python/owned-function-complexity-v2`; source-facts producer remains `0.4.0-experimental`. No cross-language calibration is claimed.

## Installed corpus results

| Selection | Production implementations | Method Complexity | Previous experimental preview |
| --- | ---: | ---: | ---: |
| Requests | 246 | 7.0 | 7.0 |
| Starlette | 508 | 7.1 | 7.1 |
| HTTPie | 464 | 6.8 | 6.8 |
| Django | 9,748 | 5.6 | 5.6 |

All four clean checkouts matched their pinned revisions. Every function fact and source-selection record matched the previous installed 0.6.0-experimental runs. Complexity contributions were identical because these selections contain only production bodies. Decomposition, duplication, and coverage review reports matched after excluding fresh invocation IDs and generation timestamps. No target code was installed or executed for the corpus checks.

Promotion changes the supported contract, not these measurements or numbers. Historical 0.5/0.6 reports remain valid and unscored; their previews cannot be reinterpreted as dimension scores. Comparisons against those policies are rejected. Matching supported current versions, configurations, selections, and scopes can compare and gate Method Complexity. Empty populations and incomplete runs cannot gate. Any reported aggregate must say **partial assessment, 1/9 dimensions scored** and equals Method Complexity.

## Verification

- **79 Python tests**, including collection checks in the existing coverage.py environment, passed with no skips.
- **256 JS/TS tests** and **9 shared conformance/calibration checks** passed.
- **20 installed-package evidence tests** passed, including anchors, weighting, exact midpoint rounding, population exclusion, empty/incomplete handling, unscored review evidence, historical compatibility, version-downgrade rejection, and score gates.
- **30 skill integration/runtime/catalog/offline-harness checks**, including 13 Python integration subtests, passed. The helper's regression test returned `gate-failed` with compatible score delta -6. Structural skill validation passed.
- The new analyzer-generated `python-method-complexity` reporting fixture was validated with the installed evidence validator. Its six rubric criteria cover the 1/9 scope, four production bodies versus one excluded test, weighted attribution, unscored duplicates/size, and unavailable coverage/lambda decomposition. No live-model generation or judging was run.

## Development artifacts

Local archive: `TestResults/python-method-complexity-2026-10-13-verified/codemetrics-ai-0.5.0.tgz`

SHA-256: `cbcefe9391efba58957cc6388f35a5e964340e2582b9dff0a13c3ab8182d0f3c`

The same directory retains the isolated install, four evidence files, `validate-corpus.mjs`, and `corpus-validation.json`. The [machine-readable validation record](python-method-complexity-2026-10-13.json) records revisions, IDs, evidence hashes, and checks. The enclosing npm development manifest remains 0.5.0; published npm 0.5.0 lacks the Python workflow. The skill requires an explicit local archive until coordinated publication. This work has not been pushed or published.
