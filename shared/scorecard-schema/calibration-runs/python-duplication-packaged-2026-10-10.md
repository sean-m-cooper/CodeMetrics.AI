# Packaged Python duplication evidence verification

Date: 2026-10-10. The offline proof was committed first as `1a97d50`. This follow-up integrates it into the development npm archive and companion code-scorecard skill. No package was published.

## Contract and behavior

The Python adapter version is `0.4.0-experimental`, evidence ruleset `python-2026-10-10-duplication-preview`. Complexity measurement policy remains `python/owned-function-complexity-v2`; duplication policy remains `python/exact-function-body-v1`. Source-facts v2 and evidence v3 retain their versions. The new `dimensions.codeQuality.duplication` extension embeds the review report; raw source facts remain a separate field.

One isolated Python process measures source selection once, then passes that population to duplication analysis. Duplication rereads selected files against the measured source digests. Its run/audit IDs match the enclosing invocation, and the evidence configuration fingerprint includes the duplication configuration. The packaged minimum is fixed at five statements. The standalone CLI retains its configurable minimum.

Every duplicate group produces one informational `exactBodyDuplicationReview` finding, with every occurrence in `observations`. SARIF exports that finding as a note and retains occurrence data. Group count and matched-function count remain distinct. All nine dimension scores remain unavailable; the method-complexity preview stays separate. No duplication percentage, score, deduction, or overall is introduced.

The validator checks policy/producer/configuration, invocation IDs, source selection, member/file/line identities, source digests, group membership, population partitioning, statement thresholds, exclusions, and summary accounting. It rejects repeated members/groups and forged score fields. This validates consistency of measured evidence; it does not reparse source or independently prove semantic equivalence. A duplication failure with complete complexity still makes the combined analysis incomplete and withholds the preview and all findings. Failed duplication reports have no groups or summary.

The companion skill now prefers this adapter, reports exact-body counts within their search boundary, preserves exclusions and historical policies, and requires source/context review before suggesting extraction. Older evidence lacking the extension is unassessed, not a zero-duplication result. The explicit-local-archive requirement remains because published npm 0.5.0 does not contain this workflow.

## Installed corpus verification

The [machine-readable record](python-duplication-packaged-2026-10-10.json) preserves the local archive SHA-256, pinned revisions, distinct run IDs, shared audit ID, and results. The archive was installed into an isolated tooling directory. Both analysis and evidence inspection used the installed CLI, with caller-supplied expected IDs and source roots. Every group occurrence and summary matched the offline report exactly; all checkouts remained clean and pinned.

| Project | Duplicate groups | Matched bodies | Complexity preview |
| --- | ---: | ---: | ---: |
| Requests | 0 | 0 | 7.0 |
| Starlette | 0 | 0 | 7.1 |
| HTTPie | 0 | 0 | 6.8 |
| Django | 12 | 24 | 5.6 |

The archive is npm `codemetrics-ai` 0.5.0 **development content**, SHA-256 `212f1b1f0658881595b3b2873bdd0394557229edff792781c8e2b73d103f3494`. It is not interchangeable with the published archive bearing that npm version. The Python adapter identifies its own experimental version independently.

## Checks

- 39 Python unit tests, including the combined measurement process and propagation of duplication-budget failure.
- 12 installed-package Python evidence tests, including grouping, SARIF, stale embedded IDs, invalid source locations, repeated members, forged counts, partial parse, and duplication-only failure.
- Nine shared conformance/population-policy tests.
- All 256 JS/TS regression tests passed at the existing timeout settings.
- 26 companion Python/runtime/catalog/offline-harness tests, including nine Python integration subtests; skill structural validation passed. These are executable integration and structural checks, not a live-model evaluation of generated reports.
- Four installed corpus runs with validated inspections, preserved complexity previews, and exact agreement with the offline groups.

Reproduce using a freshly built local archive and a new output directory:

```powershell
node analyzers/python/package-smoke.mjs
node analyzers/python/packaged-corpus.mjs --package <local-archive.tgz> --repos E:/repos --output TestResults/python-packaged-new
```

The [adapter guide](../../../analyzers/python/README.md) and [duplication boundary](../../../analyzers/python/DUPLICATION.md) describe invocation, interpretation, and limitations.
