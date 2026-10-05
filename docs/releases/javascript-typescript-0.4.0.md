# codemetrics-ai 0.4.0

This release improves JS/TS function scoring and adds contextual evidence for
module architecture, decomposition, and error handling. The .NET analyzer remains
at 2.3.1. JS/TS and .NET scores are not calibrated against each other.

## Scored measurements

- Method Complexity uses each function's own body, excluding nested bodies and
  erased TypeScript syntax. One worst function receives 40% of the weight and the
  remaining mean 60%. Decision breakdowns explain the observed CC.
- Maintainability uses 40% of the weakest fifth's mean and 60% of the remaining
  mean. A function belongs to one group only.
- Async/Blocking Usage scores affected eligible function/module owners, counting
  each owner once. Review leads contribute no penalty. The percentage ladder is
  product policy, not a runtime-speed measurement. A score of 10 may include
  findings below 1%; no eligible owners means unscored.

## Contextual evidence without new scores

- Architecture reports dependency resolution/scope gaps, cycles, fan-in/out, and
  separate implementation/re-export views with overlap preserved.
- Decomposition v2 ranks owned executable statements, including one implicit
  return for concise bodies. Nested functions are separate; lines are context.
- Error-handling v2 follows bounded selected-source declarations/const aliases
  and imports. Shared implementations count once with their uses attached.
  Documented empty handlers acknowledge intent; unresolved references are gaps.

These three assessments remain unscored. Experimental decomposition previews are
not package scoring policies and must not contribute to an overall score. Other
unsupported dimensions remain unavailable. No finding alone proves a defect or
requires decomposing a legitimate orchestration role.

## Migration from 0.3.0

Install `codemetrics-ai@0.4.0`. The schema remains v3; the released ruleset is
`javascript-typescript-2026-10-04-local-handlers`. Generate fresh baselines: package,
ruleset, scope, or measurement differences must not be reported as code improvement.
Incompatible comparisons cannot enable gates. Preserve historical evidence rather
than rewriting its scores. Raw CSV remains unchanged and cannot reconstruct the
new aggregate scores or prove freshness.

Consumers should report scoped scores with their populations, preserve run/audit
IDs, honor unavailable/failed dimensions, and read extension versions before
comparing decomposition or handler populations. The npm package also supplies the
shared evidence inspector used for .NET; the coordinated skill update verifies
that compatibility and retains the .NET 2.3.1 pin.

## Validation

Release verification covers the analyzer suite, installed-package inspection,
comparison gates and SARIF, architecture context fixtures, shared calibration,
and the scorecard skill's packaged integration. Source-only corpus evidence spans
Express, Zod, TanStack Query Core/React Query, Excalidraw's app, and Uptime Kuma's
server. No runtime-performance or complete security/testing assessment follows
from these selected-source runs.

The October 5 release-candidate checks passed: 184 analyzer tests, the installed
package smoke test, 11 architecture context cases plus offline package-resolution
checks, and all six JS/TS calibration fixtures. Calibration changed only the
package-version metadata. The coordinated skill passed 16 offline checks and
13 packaged integration tests, including unchanged .NET 2.3.1 and historical v2
inspection. One synthetic generated JS/TS report passed all five factual review
criteria; older live-model cases were not rerun. Cross-platform CI and publication
are tracked by the release PR and tag workflows.

See the [analyzer README](../../analyzers/javascript-typescript/README.md),
[statement calibration](../../shared/scorecard-schema/calibration-runs/javascript-typescript-statements-2026-10-04.md),
and [consumer integration contract](../evidence-workflows.md).
