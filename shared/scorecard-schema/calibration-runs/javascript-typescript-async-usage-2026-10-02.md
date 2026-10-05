# JS/TS async usage development verification

The first JS/TS improvement pass extends React-only observations with bounded
standard-Promise executor checks and unscored async Array.forEach review leads.
The implementation and limits are in the [async policy](../javascript-typescript-async-policy.md).
This is local development work, not a published npm release or cross-ecosystem
calibration result.

## Verification

- The original 40 tests passed before implementation. The updated suite passes
  58 tests, including 18 new positive, negative, ownership, applicability,
  failed-analysis and gate/provenance cases.
- All six pinned JS/TS calibration fixtures retain their scores, populations and
  finding-category counts. Their baseline diff changes only the ruleset to
  `javascript-typescript-2026-10-02-async-usage`.
- The reviewed React snapshot preserves every raw metric, finding fingerprint and
  location. Intentional changes are the async-applicability marker, classification
  metadata, and missing-dependency-array review leads changing from warning to info.
  The snapshot also passed without update mode after review.
- The isolated installed npm package passes React and non-React analysis, schema
  validation, comparison/gates, SARIF and invalid-input checks. Removing an async
  Promise executor changes its scoped score from 6 to 10 while an async forEach
  review lead remains visible and excluded from penalties.
- A self-analysis completed and validated: 16 source files, 137 functions. Its
  async dimension is skipped because no applicable async/React surface was
  observed; it is not awarded a clean async score.

Local logs and self-run artifacts are under
`E:/repos/CodeMetrics.AI/TestResults/js-ts-foundations/`.

## Remaining evaluation

The existing six calibration fixtures do not exercise the new promise categories;
the new unit and installed-package cases supply that coverage. Representative
Node, React and TypeScript-library corpus evaluation is still needed before
claiming broader calibration. Complexity/MI ladders, .NET policies and the shared
evidence schema are unchanged. No percentage-based async ladder is implemented.
