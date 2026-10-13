# Python complexity calibration review

Date: 2026-10-10. **Historical v1 calibration study.** At study time, the development adapter used `python/owned-function-complexity-v1`; Python dimension scores remain unavailable and its preview remains experimental. No cross-language calibration is claimed.

Implementation follow-up: the approved narrow adjustment is now measurement policy `python/owned-function-complexity-v2`, adapter `0.3.0-experimental`. The [verification record](calibration-runs/python-match-v2-2026-10-10.md) records the exact changes. The original study and machine-readable results below are preserved. The subsequently accepted [Method Complexity baseline](../../analyzers/python/METHOD_COMPLEXITY.md) promotes the adjusted production-function metric to the first supported Python dimension in adapter 0.7.0.

## Recommendation

Keep the accepted individual anchors **CC 3/5/10/20/40 → 10/8/6/4/0**, linear interpolation, one-worst 40% / remaining-mean 60% weighting, and decimal half-up rounding. The reviewed Python constructs do not justify relaxing the ladder or discounting all comprehensions or exception handlers.

Before promoting Python Method Complexity to a supported dimension score, make one focused measurement revision: treat a whole-case OR list of simple non-destructuring patterns as one membership test. Retain the original measurement in historical evidence and version the new policy. Do not silently reinterpret earlier facts or baseline scores.

This pass implements the **offline candidate**, controlled examples, corpus replay, and review record needed to assess that revision. It does not change production measurement, score availability, the skill, or frozen corpus baselines. The outcome is a bounded proposal supported by inspected source, not permission to tune scores until these libraries look good.

## Evidence and sampling limits

The [pinned corpus](../calibration/corpus/python-selection.json) supplies 10,966 selected implementation bodies in four projects. The study freshly parsed all four clean checkouts and verified the complete function-facts digest and selection fingerprint against the frozen baselines. It then reread selected source with matching content hashes. No target code was installed, imported, or executed.

The [review manifest](../calibration/corpus/python-complexity-review.json) records 20 purposively selected bodies covering all six CC bands, the individual-score anchors, comprehensions, chained comparisons, exception translation/recovery, match patterns, and orchestration. Source identities, pinned links, body hashes, decision breakdowns, and contextual judgments are in the [machine-readable report](calibration-runs/python-complexity-2026-10-10.json). These judgments are not independent ratings, defect labels, or evidence that refactoring is safe. Django supplies most measured functions; report projects separately rather than treating a pooled average as representative of Python.

| Project | Implementations | CC 1–5 | CC 6–10 | CC 11–20 | CC 21+ | Current preview |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Requests | 246 | 198 | 35 | 12 | 1 | 7.0 |
| Starlette | 508 | 454 | 35 | 17 | 2 | 7.1 |
| HTTPie | 464 | 410 | 42 | 8 | 4 | 6.8 |
| Django | 9,748 | 8,546 | 805 | 308 | 89 | 5.6 |

The bands classify branching, not bugs. The current informational review threshold is **CC ≥ 10**, whereas the accepted **high** band starts at **11**. Those are different counts; never label every informational lead a high-complexity function.

## What the source review showed

**Small comprehensions already fare well.** Requests' `default_hooks` is CC 2; Starlette's two-comprehension `Middleware.__repr__` is CC 3. Both receive an individual preview assessment of 10. HTTPie's `HTTPResponse.headers` is CC 6 because its generators separate ordinary headers from Set-Cookie expansion, including nested iteration and filtering. Ignoring every comprehension would erase this work. Authored expanded-loop pairs have the same counts as the equivalent iteration/filter examples.

**Exception branches carry control-flow information without establishing bad handling.** HTTPie's `read_raw_config` is CC 4 with three distinct failure outcomes. Requests' `Response.json` is CC 8 and documents its decode fallback. Django's `Atomic.__exit__` is CC 21 because commit/rollback failures and connection restoration require many paths. These can be necessary and well-designed while remaining demanding to understand. The policy must not recommend deleting handlers to improve a number. Cleanup-only `finally` and sequential `await` examples add no decisions by themselves.

**The ladder reflects the stated hotspot policy.** Requests' multipart encoding at CC 21, HTTPie's CLI orchestration at CC 26, and Django's `Model._save_table` at CC 50 contain materially more branching than the low-band samples. This supports retaining a strong hotspot contribution. It does not prove they contain defects, that their business responsibilities are wrong, or that splitting them would reduce total maintenance cost. Low-CC delegators also do not measure their callees.

**CC does not distinguish flat dispatch from deep nesting.** HTTPie's status-code mapping is readable and flat at CC 8; Django's `partial_func` is CC 2 with a structured pattern. Complex pattern matching can hide runtime mechanics that the syntax policy does not enumerate. These limits argue for explicit measurement scope and source review, not an automatic exemption for every `match`.

## Proposed OR-membership boundary

Python tries OR subpatterns in order until one matches; guards are separate expressions evaluated after a successful pattern and can reject a case. These runtime facts do not dictate a unique source-complexity convention. Our recommendation is a product choice for consistent treatment of declarative membership lists. [Python 3.12 pattern and guard semantics](https://docs.python.org/3.12/reference/compound_stmts.html#or-patterns)

The offline candidate discounts only extra alternatives in a **whole-case** `MatchOr` whose children are literal/value patterns, singleton patterns, or class patterns without positional/keyword subpatterns. A surrounding `as` binding may capture the same whole subject. The case still contributes one increment; guards, boolean expressions, body branches, loops, and handlers retain their increments.

Structural alternatives that destructure or bind different parts remain counted. ORs nested inside structural patterns remain counted. Separate cases with distinct outcomes remain counted. No body-equivalence inference, runtime type resolution, name-based whitelist, or judgment of developer intent is used. This is not a promise that arbitrary `match` statements can be rewritten to membership checks without changing behavior.

| Reviewed function | Current CC | Candidate CC | Explanation |
| --- | ---: | ---: | --- |
| Django `normalize_json` | 12 | 8 | Five scalar patterns share one return; four alternative increments are removed. Recursive comprehensions and decoding failure handling remain. |
| Django `normalize_choices` | 20 | 17 | Four passthrough type patterns share one return; three alternative increments are removed. Depth guards, lazy evaluation, iterator safeguards and fallback remain. |

Only these **two of 10,966** corpus bodies change under this candidate. All four rounded previews remain unchanged. The recommendation is about consistency for the affected developer, not increasing aggregate scores. Only Django has `match` in this selected corpus (four functions), so broader real-world pattern evidence is still needed before generalizing beyond this narrow boundary.

## Sensitivity checks

All calculations use the canonical shared engine. The two category removals below are deliberately broad diagnostic experiments and are **rejected**, not proposed production settings.

| Project | Current 40/60 | Ordinary mean only | Remove all handlers | Remove all comprehensions | Narrow OR-membership candidate |
| --- | ---: | ---: | ---: | ---: | ---: |
| Requests | 7.0 | 9.1 | 7.1 | 7.0 | 7.0 |
| Starlette | 7.1 | 9.5 | 7.2 | 7.1 | 7.1 |
| HTTPie | 6.8 | 9.4 | 7.0 | 6.8 | 6.8 |
| Django | 5.6 | 9.4 | 5.7 | 5.7 | 5.6 |

A plain mean hides the known tail. In a controlled population of 99 CC-3 functions and one CC-40 function it gives 9.9, whereas the accepted weighting gives 6.0. One CC-10 function among 99 CC-3 functions gives 8.4; one CC-21 function gives 7.5, unchanged by adding 900 trivial functions. Additional fixed-population hotspots reduce the underlying score even when one-decimal displays tie. These checks support the previously accepted product expectations rather than an empirically established defect prediction.

Chained comparisons retain one increment per additional comparator. Python specifies short-circuit behavior comparable to the expanded conjunction while evaluating intermediate expressions once. The paired fixture protects the current counting convention. [Python 3.12 comparison semantics](https://docs.python.org/3.12/reference/expressions.html#comparisons)

## Reproduction and verification

Replay the historical study in a checkout of commit `b6f953b`, which contains its v1 analyzer, baseline, and offline candidate. The current `corpus.py` runner verifies v2 separately.

```powershell
py -3.12 analyzers/python/calibration.py --repos E:/repos --output TestResults/python-calibration-new
py -3.12 -m unittest discover -s analyzers/python/tests -v
node --test analyzers/python/tests/calibration-policy.test.mjs
node --test shared/source-facts/conformance.test.mjs
```

The output directory must be new. The runner fails on checkout drift, source changes during the read, production fact/preview drift, or missing reviewed bodies. It writes clearly labeled candidate facts only under that new study directory. The `calibration_preview.py` module is not imported or bundled by the production CLI.

Verification passed: **26 Python tests**, including 32 authored fixture owners; **two policy tests** covering 11 population scenarios plus severity/padding/rounding properties; **seven shared conformance tests**; and four fresh pinned corpus selections. Tests compile authored patterns to check syntax restrictions without executing bodies. Fixture and scenario checks run in CI; the external corpus study remains a manual check. This does not establish cross-language comparability or statistical calibration on unseen projects.
