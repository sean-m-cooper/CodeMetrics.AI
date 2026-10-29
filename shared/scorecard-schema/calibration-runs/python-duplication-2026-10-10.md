# Python exact-body duplication: first corpus review

Date: 2026-10-10. Offline duplication producer `0.1.0-experimental`, policy `python/exact-function-body-v1`, native Python 3.12. This adds a separate review artifact; it does not change source-facts contracts, packaged evidence, the scorecard skill, complexity measurements, or any score.

This is the historical offline checkpoint (`1a97d50`). The subsequent [packaged integration record](python-duplication-packaged-2026-10-10.md) documents its inclusion in the development CLI and skill without changing these measurements.

## Observed results

The runner checked the four clean [pinned selections](../../calibration/corpus/python-selection.json) against their existing source digests, selection fingerprints, and function population counts. It parsed target code without installing or executing it. Each project has a fresh run ID under a shared audit ID. The [machine-readable report](python-duplication-2026-10-10.json) retains implementation provenance, parser versions, configurations, counts, and every group occurrence.

| Project | Selected functions | Eligible bodies | Duplicate groups | Matched bodies |
| --- | ---: | ---: | ---: | ---: |
| Requests | 246 | 105 | 0 | 0 |
| Starlette | 508 | 162 | 0 | 0 |
| HTTPie | 464 | 178 | 0 | 0 |
| Django | 9,748 | 3,172 | 12 | 24 |

There are **3,617 eligible bodies out of 10,966 selected functions**. The minimum is five statement nodes, not five lines. Smaller bodies, lambdas, and parents containing nested definitions are explicitly excluded. Independently eligible nested implementations remain included. Production/test/support/generated populations and async/synchronous bodies are compared separately.

The 12 Django groups contain two occurrences each. All matched bodies have **five to seven statements**. No matches in the other projects means only that this exact whole-body search found none in its eligible population; it does not establish absence of renamed, partial, or other duplication. Raising the minimum above seven would hide all of these observed groups. That illustrates threshold sensitivity, not a reason to tune the threshold until a preferred result appears.

## Context review

All 24 reported bodies were read with their adjacent source. The judgments below are source-review interpretations, not analyzer outputs, defect labels, independent ratings, or verified safe refactorings. The machine report's relative paths and lines identify the exact source at each pinned revision.

| Matched bodies | Statements | Interpretation and review boundary |
| --- | ---: | --- |
| Database/file session `SessionStore.create` | 7 | Same key-generation/retry protocol across backends. Shared control flow may preserve a common contract while storage-specific behavior stays behind `self.save`. Duplication alone does not justify merging backend responsibilities. |
| `UniqueConstraint` / `Index._get_condition_sql` | 7 | Same condition-compilation sequence. A useful maintenance-consistency lead; inspect callers, ownership, and schema-editor assumptions before proposing a shared helper. |
| Python/XML serializer `queryset_iterator`, first branch | 7 | Same ordering and chunk-size logic in nested helpers. Repeated policy could drift; extraction must preserve the surrounding serializer behavior. |
| `PermissionsMixin` / `AnonymousUser.ahas_perms` | 6 | Parallel permission iteration with different receiver behavior. Identical control flow is not evidence of a permission defect. |
| Python/XML serializer `queryset_iterator`, second branch | 6 | Another shared ordering/chunk-size sequence. This is a distinct group, not a repeated count of the first branch. |
| Filesystem/in-memory storage `url` | 6 | Same URL assembly and validation. Potential shared-policy review, with backend boundaries and base-class responsibilities still relevant. |
| `Template` / `PartialTemplate.render` | 6 | Same context setup around receiver-specific rendering. Equal bodies do not prove identical object state or interchangeable classes. |
| MySQL/PostgreSQL client `runshell` | 5 | Matching signal-handler preservation around backend shell execution. Comments identify their separate clients; consistent platform behavior can be intentional. |
| `DateField` / `TimeField.__init__` | 5 | Common automatic-timestamp setup. Class hierarchy and `super()` behavior remain outside the body comparison. |
| PostgreSQL array/range `get_placeholder_sql` | 5 | Same cast/placeholder protocol using receiver-specific database types. A review lead, not a demonstrated need for another abstraction. |
| Signal dispatch `asend_and_wrap_exception` helpers | 5 | Same exception/logging wrapper in two call paths. Helpers capture surrounding values; a refactor needs closure/caller review. The catches are not classified as poor handling. |
| Inline/generic-inline form `has_changed` | 5 | Matching permission protections in nested classes. Preserve authorization behavior and captured permissions; do not remove checks to eliminate duplication. |

These observations support keeping this capability **unscored**. The measure establishes repeated structure within a documented boundary; whether an abstraction would improve the code depends on responsibilities, bindings, and required behavior that this parser does not establish. Comments are excluded from matching, but an explained design remains relevant to the human review and never incurs an automatic deduction here.

## Implementation and verification

The [offline CLI](../../../analyzers/python/DUPLICATION.md) preserves names/literal values, compares complete AST body structures, and groups occurrences once rather than generating overlapping pairs. It emits inspectable locations and source digests, records exclusions, and withholds all groups on parsing failures, changed source, identity drift, or exceeded budgets. Historical output is never overwritten. Complete empty populations remain explicitly empty without receiving credit.

**38 Python tests passed**, including ten initial duplication tests plus triple-occurrence accounting and statement-versus-line coverage. Existing source-facts/selection/calibration tests passed unchanged. Duplication tests also cover exact and negative matches, nested owners, population partitioning, async/synchronous separation, source drift, budgets, fresh IDs, no target execution, and CLI failures. Existing Python test discovery includes these tests in Windows/Linux CI. The four-project corpus is a manual pinned verification, not a statistically representative benchmark or a full scorecard.

Reproduce with a new output directory:

```powershell
py -3.12 analyzers/python/duplication_corpus.py --repos E:/repos --output TestResults/python-duplication-new
py -3.12 -m unittest discover -s analyzers/python/tests -v
```

Next integration step: expose this bounded review evidence through the packaged analyzer and skill, preserving its exclusions and unscored status. Broader clone detection and a duplication scoring policy require separate evidence and decisions; zero matches here must not fill either gap.
