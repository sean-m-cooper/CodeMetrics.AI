# Python match-membership policy verification

Date: 2026-10-10. Implemented the approved narrow adjustment from the [calibration study](../python-complexity-calibration.md). The historical study is preserved at checkpoint `b6f953b` and its original JSON is unchanged.

The development Python adapter is now `0.3.0-experimental`, measurement policy `python/owned-function-complexity-v2`, with evidence ruleset `python-2026-10-10-match-membership-preview`. Source-facts schema v2 and evidence schema v3 are unchanged. The individual ladder, population weighting, selection policy, and unavailable dimension scores are unchanged. Nothing was published.

Whole-case OR lists of value/singleton patterns or classes without positional/keyword subpatterns receive one case increment, including an optional whole-subject `as` binding. Guard and body decisions remain counted. Destructuring alternatives, ORs inside structural patterns, and separate cases retain their counts. No developer intent or runtime equivalence is inferred.

## Fresh corpus results

The runner verified the four pinned clean source checkouts, accounted for every original owner exactly once, and compared each retained body's complete decision breakdown with the original trusted analyzer plus the independently implemented offline candidate. [Machine-readable verification](python-match-v2-2026-10-10.json) includes pins, source and function digests, and exact changes.

| Project | Implementations | Changed bodies | Rounded preview |
| --- | ---: | ---: | ---: |
| Requests | 246 | 0 | 7.0 |
| Starlette | 508 | 0 | 7.1 |
| HTTPie | 464 | 0 | 6.8 |
| Django | 9,748 | 2 | 5.6 |

Django `normalize_json` changes CC 12→8 and `normalize_choices` changes 20→17. The CC ≥ 10 review-lead count decreases from 480 to 479 in Django. Every other function measurement, source selection, declaration exclusion, and rounded preview is preserved. This is a measurement-policy change on identical source, not evidence of code improvement or cross-language comparability.

The frozen corpus baseline now names its measurement policy. Its only measurement changes are Django's full function-facts digest and review-lead count. Historical reports retain their original v1 policy and values; the offline candidate rejects v2 facts to prevent applying the discount twice.

## Verification

- 26 Python unit tests, including 32 authored owners with literal, singleton, class, guarded, as-bound, structural, nested, and separate-case patterns.
- Two population-policy tests and seven shared conformance tests.
- Ten installed-package Python evidence tests, including exact v2 counts, provenance, mixed-version rejection, freshness, and failure behavior.
- JS/TS regression suite: 255/256 passed initially; one dependency integration case exceeded the five-second timeout. Its entire 22-test file passed on an isolated rerun at the unchanged timeout (the affected case took 2.7 seconds). No JS/TS scoring code changed.
- Companion scorecard skill: 25 Python/runtime/catalog/offline-harness tests passed, including eight Python integration subtests; structural validation passed. The preferred adapter is updated to 0.3.0-experimental and reporting guidance distinguishes v1/v2. The older 0.2.0 entry remains a historically tested version.

The installed skill checks used the local npm 0.5.0 development archive with SHA-256 `b523807c4f3f25422e899b75d19a9e580031eb92a8a7dc15581826a7d01ee2e8`. Published npm 0.5.0 does not contain this Python workflow; an explicit local archive is still required. The skill and analyzer changes are local development work pending a coordinated release.

To verify current corpus baselines, export the trusted original analyzer and run `analyzers/python/corpus.py` using the commands in the [adapter guide](../../../analyzers/python/README.md#pinned-corpus), without `--record`. To reproduce the historical calibration study, use checkpoint `b6f953b`.
