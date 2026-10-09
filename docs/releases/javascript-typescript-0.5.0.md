# codemetrics-ai 0.5.0

This release adds contextual Error Handling scoring for JavaScript and TypeScript.
The .NET analyzer remains 2.3.1. Method Complexity, Maintainability, Async/Blocking
Usage, and raw metrics retain their previous policies. Architecture and
decomposition remain unscored.

## Error Handling

Each distinct handler body counts once, with all uses retained. The analyzer
recognizes explicit failure outcomes, source-backed reporting wrappers,
caller-supplied error callbacks, guarded optional-result contracts, and documented
intent. Names alone and nonempty code do not establish handling. These are
bounded source signals, not proof of correct recovery or absence of defects.

For a fully assessed population, the score is `10 - 10 * unexplained / assessed`,
capped at 9 when any unexplained swallowing exists and rounded half up to one
decimal. Unknown bodies or unresolved rejection callbacks withhold the entire
dimension score. No handlers means unavailable, not 10. Unknowns do not become
failures or successful handling by assumption.

Source-only calibration uses the same six pinned selections as 0.4.0. Express's
two handlers now qualify through callback propagation and a guarded optional
result. Larger selections can remain unavailable despite improved recognition.
The [policy](../../shared/scorecard-schema/javascript-typescript-error-handling-policy.md)
documents recognized contracts and limits; the
[release calibration](../../shared/scorecard-schema/calibration-runs/javascript-typescript-handler-contracts-2026-10-09.md)
records the populations, source pins, and invariants.

## Migration

Install `codemetrics-ai@0.5.0`. Schema v3 is unchanged. The ruleset is
`javascript-typescript-2026-10-09-handler-contracts` and Error Handling scope/policy
is `failure-disposition-v2`. Generate fresh baselines, including when moving from
the unpublished v1 prototype. Policy changes alone do not show source improvement.

Consumers must honor the emitted dimension status, scope, and score. Read
`dispositionEvidence` for assessed/unknown body counts and unresolved use counts;
do not reconstruct scores from `unexplainedEmptyHandlers` or CSV. Historical 0.4.0
evidence stays unscored. A partial overall averages only scored dimensions and
must disclose its denominator. JS/TS and .NET scores remain uncalibrated against
each other.

The coordinated code-scorecard skill update supports both historical evidence and
0.5.0, prefers the new analyzer/inspector, and retains the .NET 2.3.1 pin. Existing
exact repository pins are honored rather than silently upgraded.

## Release checks

Validation covers analyzer tests, packaged CLI inspection/comparison/gates/SARIF,
architecture fixtures, shared calibration, the pinned public-source corpus, and
the skill's packaged integration. GitHub CI checks Node 20/22, .NET on Windows and
Linux, and the skill integration on both platforms before the release tag is pushed.
