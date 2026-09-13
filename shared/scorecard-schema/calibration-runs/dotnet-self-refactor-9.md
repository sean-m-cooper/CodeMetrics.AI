# Self-refactor pass 9: controller authorization responsibilities

This pass follows commit `6df6755` on `codex/public-corpus-fidelity`. It separates authorization discovery, assessment and finding construction without changing security rules or scoring.

## Changes

`ControllerAuthorizationAnalysis` gathers included member symbols with one semantic model per tree and retains one controller symbol with its first included declaration. Project-wide Authorize use remains the gate for assessment. Controller assessment checks inherited class intent and counts eligible, unannotated actions in one pass. `SecurityProbe` constructs the existing findings from those observations.

The first declaration and traversal order, partial-symbol aggregation, attribute aliases, class hierarchy checks, public instance action eligibility, and zero-action review behavior remain unchanged. This is not expanded runtime authorization analysis. A missing-intent finding still acknowledges that global filters or fallback policy may protect the endpoint.

## Verification

- **755 .NET tests passed**, with no failures or skips. Four new cases cover partial controllers across files in both traversal orders, exact finding locations/messages/counts, mixed explicit action intent, ignored non-actions/static/private/property members, inherited intent, empty controllers, and a project gate declared after controllers or only in generated source.
- **52 security and authorization tests passed against the previous packaged analyzer DLL**, including all four new cases, in an isolated test-output copy.
- Six pinned .NET calibration fixtures passed without recording baseline changes.
- Full .NET formatting and Git whitespace checks passed.
- The previous packaged analyzer and the current analyzer evaluated identical current production source: 132 types and 725 raw members. Complete evidence matches except fresh IDs and generated timestamp; raw CSV is byte-identical. This comparison skips live dependency queries.
- A separate fresh helper audit includes live dependency checks. It is complete, usable and catalog-validated, with matching invocation/evidence IDs and no analysis diagnostics. One production project was analyzed out of two loaded units; the test project was excluded from production metrics.

Compatibility is established on these tests, fixtures and source comparison. The full public corpus remains deferred to the release checkpoint unless measurement behavior changes.

## Self-scorecard

All rounded dimension scores are unchanged. Overall **8.4, partial assessment, 9/9 dimensions scored**. All scores below are deterministic within their stated static scope; no coverage report was supplied, and runtime behavior and comprehensive human review remain outside the assessment.

| Dimension | Score | Scope and evidence |
|---|---:|---|
| Architecture & SOLID | 8.6 | Static coupling/project structure; four coupling hotspots in 84 eligible types |
| Complexity & Decomposition | 6.3 | Method complexity 7.8; Decomposition 4.7 |
| Testing | 10 | Static test signals; no coverage file |
| Security | 10 | Static patterns and vulnerability observations; no findings |
| Error Handling | 10 | Static exception patterns; six informational findings, no errors/warnings |
| Documentation | 8 | Presence/content signals; one stale marker |
| Dependency Management | 6 | Seven included outdated package occurrences; six have unknown framework compatibility |
| Performance & Async | 10 | Static async/blocking patterns; no findings |
| Maintainability | 6.8 | Own MI for 1,120 distinct executable functions |

Maintainability's unrounded aggregate moves from **6.7710 to 6.7536**, with the function population changing from 1,125 to 1,120. The former authorization method's MI was 38.50. The coordinator now measures 78.63, project discovery 54.82, action assessment 58.31, and finding construction 56.82. Extracted helpers remain in the measured population. This is a responsibility and traversal improvement, not an aggregate score improvement. No policy, threshold or baseline was adjusted.

Live dependency observations differ from the prior audit despite unchanged dependency source. Seven distinct packages each have one included outdated occurrence across two projects, all net10.0: Microsoft.Build, Microsoft.Build.Framework, Microsoft.Build.Utilities.Core and Microsoft.NET.StringTools 18.9.6 → 18.10.1; System.CommandLine 2.0.11 → 2.0.12; xunit.v3 4.0.0 → 4.0.1; Microsoft.NET.Test.Sdk 18.9.0 → 18.10.0. Only the Test SDK candidate has confirmed compatible assets in this run. The other six remain unknown, and no incompatible candidates were excluded. The run reports no vulnerabilities, deprecated packages, unsupported frameworks or version drift. These are review candidates, not established safe upgrades, and are not attributed to the authorization refactor.

## Next priority

**Current context, reviewed candidate:** `DependencyProbe.RunDotnetListAsync` at line 470 mixes command setup/lifetime, cancellation/timeout handling, report validation and diagnostic conversion. Separate execution from package-report validation to make those failure paths independently testable while preserving incomplete-result behavior. This recommendation comes from the current source review, not from treating the unknown compatibility observations as a diagnosed subprocess defect.

## Provenance

- Tool: local unpublished 2.3.0, schema v3, ruleset `dotnet-2026-09-12-function-maintainability`, calibration `baseline`, Release.
- Package SHA-256: `a54e8ae16a1da1e295ada3631c494eb7a723c0cdfcda8c509c8b717d0160e397`.
- Audit/run: `c874116b-2873-4674-8245-804fcc21f2e7` / `1d241fb9-7cbe-43ce-bc7d-bfe6ed102320`.
- Configuration fingerprint: `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`.
- [Fresh evidence](<E:/repos/CodeMetrics.AI/.scorecard/dotnet/runs/1d241fb9-7cbe-43ce-bc7d-bfe6ed102320/evidence.json>).
- [Validation and score comparison](<E:/repos/CodeMetrics.AI/TestResults/self-refactor-9/verification.json>).
- [Behavior comparison identities](<E:/repos/CodeMetrics.AI/TestResults/self-refactor-9/contract-verification.json>).

Packages, caches and artifacts remain on E:. No package was published.
