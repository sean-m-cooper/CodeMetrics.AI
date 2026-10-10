# JS/TS dependency calibration, 2026-10-09

The npm-first inventory and optional registry observations are implemented as
**unscored evidence**. This corpus does not yet support a numerical dependency
policy: only one of the six selected projects has a supported npm lockfile.
The next coverage priority is pnpm, followed by Yarn. Missing coverage does not
establish poor dependency management.

The [policy](../javascript-typescript-dependency-policy.md) defines counting,
availability, intent boundaries, and request limits. The [machine-readable report](javascript-typescript-dependencies-2026-10-09.json)
records pinned source revisions, manifest declarations, lockfile hashes, fresh
run/audit IDs, configuration/evidence hashes, inventory notices, and the assessed
live metadata observations with response hashes. Artifacts were generated with
development source labeled `0.5.0-dev`; published 0.5.0 remains unchanged.

| Selected source | Inventory result | Package/version/source groups | Install locations | Explanation |
| --- | --- | ---: | ---: | --- |
| Express | Unavailable | — | — | No npm lockfile in the pinned selection's repository |
| Zod v4 | Unsupported manager | — | — | Declares `nub@0.8.3` |
| TanStack Query Core | Unsupported manager | — | — | Declares pnpm |
| TanStack React Query | Unsupported manager | — | — | Declares pnpm |
| Excalidraw app | Unsupported manager | — | — | Declares Yarn |
| Uptime Kuma server | Inventory with one gap | 1,182 | 1,303 | One unresolved required peer reference |

Unavailable rows are not zero-dependency projects. Their declared dependencies
are preserved even though resolved versions cannot be assessed by this adapter.

## Uptime Kuma context

The one gap is `@hey-api/client-fetch` requesting peer `@hey-api/openapi-ts < 2`,
without a resolved target in the lockfile traversal. This is a coverage observation,
not a conclusion that the application fails to build or run.

Calibration corrected two misleading first-pass assumptions:

- Absent optional peers are legitimate. Their absence is recorded as a notice,
  while a missing required child of an installed optional parent still produces
  a gap.
- Locked ranges and identities can intentionally differ from the original
  requests. This project explicitly replaces `@aws-sdk/credential-providers`,
  `@azure/identity`, and `@azure/keyvault-keys` with `useless-module` through npm
  overrides. The analyzer retains the replacement package and original uses.
  It neither discards those packages nor calculates replacement upgrades using
  the original package's unrelated range.

The 1,303 locations are represented by 1,182 grouped package/version/source
records. Production and development reachability overlap; they are not independent
populations to sum or score twice. No target dependencies were installed and no
application code was executed.

## Live metadata scope

This run limits queries to **25 distinct package names**, prioritizing direct uses,
with a 30-second scheduling budget per selection. Uptime Kuma has 25 package/version
groups with both installed-version metadata and advisory assessment available;
the remaining groups are explicitly unavailable under this bounded sample.

The sampled observations report one newer version within a direct declared range,
zero deprecation messages, and zero matching advisories. These are time-dependent
results for the queried subset only. They do not establish that the entire project
has no deprecated or vulnerable dependencies, and they produce no score.

Deterministic fixtures independently exercise positive deprecations/advisories,
exact pins, prereleases, duplicate advisory responses, malformed/error feeds,
private-source exclusions, workspace references, aliases, override replacements,
stale declarations, optional peers, response bounds, and independent availability
of advisory versus package metadata. Real-source samples are not substitutes for
those positive and negative controls.

## Invariants and reproduction

Validation passed: 232 analyzer tests (including 22 dependency tests), installed
package smoke checks, eleven architecture context fixtures, six shared calibration
fixtures, sixteen offline skill checks, and fourteen packaged skill integration
checks with the new dependency case required and exercised. Skill frontmatter
validation passed. No live-model report evaluation or release was performed.

All six pinned selections retain byte-identical raw CSV and equal evidence for
every dimension except Dependency Management compared with the saved 0.5.0 corpus.
Shared calibration retains its frozen score distributions and accuracy labels;
only the new ruleset metadata changes. Architecture and decomposition remain
unscored, and cross-ecosystem equivalence is not claimed.

From `analyzers/javascript-typescript`, after building and reproducing the earlier
corpus inputs if necessary:

```powershell
npm test
npm run test:package
node scripts/dependency-corpus.mjs E:/repos E:/repos/CodeMetrics.AI/TestResults/js-ts-0.5-release/corpus E:/repos/CodeMetrics.AI/TestResults/js-ts-context-calibration/corpus E:/repos/CodeMetrics.AI/TestResults/js-ts-dependencies/corpus --metadata
```

Omit `--metadata` for a fully offline inventory pass. Use the host's normal trusted
CA configuration when necessary; failed TLS validation remains unavailable, never
a reason to disable certificate verification. New rulesets and different network
modes require compatible fresh baselines.

**Recommendation:** expand package-manager coverage and review meaningful
dependency outcomes before proposing numerical weights. Informational observations
already provide value; the current evidence does not justify a nine-dimension
score by assumption.
