# Python owned-statement and nesting study

Date: 2026-10-13. This is a first measurement and calibration review, not a scored Python assessment. [Machine-readable evidence](python-decomposition-2026-10-13.json) records pinned revisions, producer and implementation identities, complete population digests, distribution counts, and eight source-reviewed cases. [Measurement policy](../../../analyzers/python/DECOMPOSITION.md) defines the boundaries.

## Observed populations

All four source selections match the existing frozen file and function facts, including every complexity measurement. There are 10,966 selected implementations; 10,825 have statement-based measurements and 141 lambdas remain unavailable for this metric. These totals are accounting only: Django dominates the sample, so calibration should compare projects separately.

| Project | Measured bodies | Unavailable lambdas | Statements p50 | p90 | p95 | Maximum | Nesting p95 | Maximum |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Requests | 246 | 0 | 3 | 14 | 22 | 56 | 3 | 5 |
| Starlette | 507 | 1 | 3 | 15 | 20 | 54 | 3 | 5 |
| HTTPie | 460 | 4 | 4 | 13 | 18 | 60 | 3 | 5 |
| Django | 9,612 | 136 | 3 | 12 | 19 | 135 | 3 | 9 |

Quantiles use nearest rank over measured bodies in the production population. Missing measurements do not become zeros. Test, support, and generated overrides remain separate populations. Physical line spans are locations, not decomposition measurements.

## What the source review adds

| Reviewed body | Statements | Nesting | CC | Interpretation from inspected source |
| --- | ---: | ---: | ---: | --- |
| Requests `HTTPDigestAuth.build_digest_header` | 56 | 1 | 19 | Large but shallow protocol construction; five nested helpers remain separately owned. |
| Requests `RequestsCookieJar._find_no_duplicates` | 11 | 5 | 9 | Small body with deeply nested matching/conflict checks. Size alone misses this structure. |
| Starlette `FileResponse._handle_multiple_ranges` | 20 | 5 | 5 | Resource scope, nested range/chunk iteration, and an EOF guard explain the depth. |
| HTTPie `raw_main` | 60 | 4 | 26 | CLI setup and several explicit failure outcomes; deleting handlers would sacrifice behavior. |
| Django SQLite `register` | 58 | 1 | 3 | Mostly a function/aggregate registration catalogue, with capability/version checks. |
| Django `Options.__init__` | 41 | 0 | 1 | Model metadata initialization, consisting of 41 assignments. |
| Django `templatize` | 135 | 7 | 51 | Translation/comment/token state and gettext output make this a substantial behavioral review lead. |
| Django `MigrationAutodetector._build_migration_list` | 50 | 9 | 23 | Dependency iteration and progress/cycle logic; comments explain important algorithmic constraints. |

The JSON record contains immutable source links, body hashes, and the exact statement-kind and excluded-syntax breakdowns. These are purposively selected examples reviewed by one assistant, not independent maintainability ratings, defect labels, or proof of safe extraction.

## Calibration conclusion

**Keep size and nesting separate.** Their rankings reveal different structures. A raw statement count is reproducible and useful, but a high value is not sufficient evidence of poor decomposition. Registration and initialization routines are concrete counterexamples to an automatic length-only deduction. Likewise, resource and cleanup scopes can be deliberately nested; flattening them is not necessarily beneficial.

For the next informational review policy, **more than 30 owned statements** and **at least four block levels** are reasonable candidates to evaluate. They are not enabled as findings or score thresholds in this implementation. The following counts show review volume under alternative boundaries:

| Project | Statements >20 | >30 | >40 | Nesting ≥4 | ≥5 | ≥6 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Requests | 14 | 8 | 2 | 7 | 2 | 0 |
| Starlette | 24 | 7 | 4 | 7 | 1 | 0 |
| HTTPie | 15 | 6 | 3 | 7 | 3 | 0 |
| Django | 397 | 166 | 90 | 221 | 83 | 25 |

These two signals must not automatically create two penalties for the same body. Nesting should remain explanatory context while a decomposition scoring proposal is evaluated against explicitly reviewed responsibilities and the project's documented-intent policy. Do not exempt every constructor or registration-named function by name, infer a rationale from comment quantity, or replace the accepted CC ladder. The present evidence supports a review capability; it does not yet justify enabling a supported Python Code Quality score.

Expression size remains an explicit blind spot: a large literal or comprehension may still be a single statement. Its decisions continue to contribute to CC, but neither this statement count nor block depth measures all reading effort. Moving work into nested declarations or class initializers is not evidence of an improvement; such regions are excluded by the documented ownership boundary.

## Delivery and verification

- Added standalone `decomposition.py`, exposing per-owner counts, separate per-population distributions, invocation IDs, source hashes, and explicit failure behavior.
- Added `decomposition_corpus.py`, which requires clean pinned repositories, verifies frozen source/complexity populations, and emits size, nesting, and large-low-CC rankings plus diagnostic threshold counts.
- **78 Python tests passed**, including 14 new decomposition tests. **Nine shared conformance/calibration checks passed.** Existing Python CI test discovery includes the new tests.
- Two complete corpus runs produced identical decomposition rows and summaries. Existing source facts were not mutated, and all four frozen complexity populations remained identical.
- This capability is standalone and unscored. No npm wrapper, scorecard skill contract, coverage policy, or existing score was changed. No external project source was modified or executed.

Full outputs are under `TestResults/python-decomposition-2026-10-13-verified/`; SHA-256 values are recorded in the JSON study. Reproduce with a fresh output directory:

```powershell
py -3.12 analyzers/python/decomposition_corpus.py --repos E:/repos --output TestResults/python-decomposition-new
python -m unittest discover -s analyzers/python/tests -v
node --test shared/source-facts/conformance.test.mjs analyzers/python/tests/calibration-policy.test.mjs
```

For all collection-related tests to execute, the test interpreter must be Python 3.12 with coverage.py installed. Decomposition itself uses only the standard library and existing source parser helpers.
