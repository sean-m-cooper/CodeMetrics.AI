# pnpm dependency inventory calibration, 2026-10-09

The development analyzer now reads pnpm v9 project inventories. This extends the
earlier [npm dependency pass](javascript-typescript-dependencies-2026-10-09.md);
it does not introduce a dependency score or change any source-scoring policy.
Version remains `0.5.0-dev` for these development artifacts. Nothing was published.

## Pinned corpus results

| Selection | Grouped package/version/source identities | Locations | Inventory |
| --- | ---: | ---: | --- |
| TanStack Query Core | 265 | 265 | Complete, zero gaps |
| TanStack React Query | 537 | 544 | Complete, zero gaps |
| Uptime Kuma server | 1,182 | 1,303 | Same single unresolved required-peer reference |
| Express | 0 | 0 | No supported lockfile |
| Zod v4 | 0 | 0 | Declared `nub` manager unsupported |
| Excalidraw app | 0 | 0 | Yarn unsupported |

TanStack is pinned to `29859ae60c8dca0a5cdbf8abccc775b655cf43e2`.
Its pnpm 12.4.2 lockfile contains two YAML documents. The adapter selects document
2, the project graph, and explicitly excludes document 1's environment tooling.
Reading only the first document would have missed the application dependencies.

Query Core has one workspace package and 264 other/unknown-source identities;
React Query has three workspace packages and 534 other/unknown-source identities.
All Query Core dependencies in this selection are development-reachable. React
Query has one production-reachable workspace package and 537 development-reachable
groups, with overlap. These counts must not be added or treated as runtime exposure.
The seven extra React Query locations retain peer contexts without adding seven
independent package/version groups.

The lock uses integrity-only records, which do not prove public-registry
provenance. This rerun was offline. Registry/advisory assessment is `notRequested`,
not clean or zero vulnerabilities. No target dependencies were installed, no
lockfiles regenerated, and no target lifecycle scripts or applications ran.

## Verification

- All 256 analyzer tests passed; the final environment-document guard was then
  checked with all 46 dependency regressions, including 24 pnpm cases.
- Installed-package smoke checks passed, including two-document/catalog inventory,
  unavailable implicit-registry metadata, inspection, and input-overwrite rejection.
- All six shared calibration fixtures passed without baseline changes in this pass.
- All six pinned public-source selections completed. Every other dimension and
  raw CSV matched the released 0.5.0 source results exactly.
- All four non-pnpm package inventories matched the prior dependency pass. Uptime
  Kuma's single gap and contextual notices also matched exactly.
- Skill validation passed: 16 offline checks, 15 packaged integration checks
  with no skips, and the skill structure validator. No live-model evaluation was
  performed; published compatibility pins remain unchanged.

Machine-readable pins, run IDs, hashes, population details, and validation are in
[the companion JSON](javascript-typescript-pnpm-2026-10-09.json). Local full evidence
and logs are under `TestResults/js-ts-pnpm/`. The
[dependency policy](../javascript-typescript-dependency-policy.md) defines supported
formats, grouping, catalog/override handling, and evidence limits.
