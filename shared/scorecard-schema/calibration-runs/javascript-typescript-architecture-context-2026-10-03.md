# JS/TS architecture context verification

This follows the [first module graph pass](javascript-typescript-module-graph-2026-10-02.md).
Architecture remains unscored in the unpublished 0.3.0 development package.
No score thresholds or existing complexity, maintainability or async policies
changed. The graph ruleset is still `javascript-typescript-2026-10-02-module-graph`;
the recorded bundle hash identifies this subsequent development revision.

## Labeled counterexamples

The [authored fixture catalogue](../../calibration/javascript-typescript-architecture.json)
contains nine graph expectations. Eight also have observed Node loading outcomes;
the type-only example is analyzed statically without emission or execution.
Only these repository-authored examples were executed, never public-corpus code.

| Example | Observed cyclic groups | Observed runtime outcome |
| --- | ---: | --- |
| Public re-export file | 0 | Three exports available |
| Acyclic service composition | 0 | Expected result |
| Mutually referring explicit types | 0 | Not executed; two type-only references |
| ESM cycle, binding read after initialization | 1 | Expected result |
| ESM cycle, binding read during initialization | 1 | ReferenceError before initialization |
| ESM cycle, immediately invoked function | 1 | ReferenceError before initialization |
| CommonJS cycle, export read after loading | 1 | Expected result |
| CommonJS cycle, early export snapshot | 1 | Captured undefined without throwing |
| Deferred dynamic import cycle | 1 | Expected result |

The six cyclic examples all have one two-module component and maximum fan-out
one, but three produce the expected value, two throw, and one captures an absent
export. A cycle count or density cannot distinguish these outcomes. A function
boundary is also insufficient: the immediately invoked example demonstrates
that nested code may execute during initialization.

These labels describe deliberately constructed examples, not a measured defect
rate or a new static diagnostic. The analyzer continues to report informational
source cycles with `executionOrderEstablished: false`. It does not execute a
user's project to assign a score, and successful loading of one entry point does
not establish safety for other entry points or configurations.

## Installed package versus workspace resolution

The new integration runner packs an authored package locally, installs its
tarball with npm offline and lifecycle scripts disabled, and adds a competing
same-named workspace version. Actual Node loading returns the installed version.

| Reference | Expected analyzer outcome |
| --- | --- |
| Installed package's public entry | External, ordinary TypeScript resolution |
| Subpath absent from installed exports | Unresolved |
| Export pointing to a missing installed build file | Unresolved |

This exposed a flaw in the initial virtual-workspace fallback: after failed
normal resolution, it could substitute the workspace version and report an
internal edge. The resolver now checks for the named installed package before
trying the virtual workspace. Private subpaths and missing installed build
targets remain gaps, even when a same-named workspace could provide them.
Three additional regressions cover both failures and successful installed-version
precedence. Existing tests retain coverage of workspace import/require export
conditions, duplicate names and missing workspace build outputs.

## Verification and reproduction

- All 132 unit/integration tests pass, including 18 graph tests.
- All nine labeled examples and the real offline installation check pass.
- Installed-analyzer package, schema, baseline gate, inspection and SARIF checks pass.
- The six shared score calibration fixtures pass without an additional baseline update.
- Fresh runs of all four pinned public selections preserve every dimension's
  evidence, graph observations, populations, filters and raw CSV from the first
  graph pass. The scope JSON contents match, but the files live in a new output
  directory, so configuration fingerprints differ. This explicit invariance
  check does not override the normal baseline compatibility gate.
- CI on Node 20/22 and the npm release workflow now include `test:architecture`.
  Local verification used Node 24.18.0; hosted CI has not been run in this local pass.

Run from the repository root after installing the analyzer's development dependencies:

```powershell
. E:/tools/Use-CalibrationDotNet.ps1
npm test --prefix analyzers/javascript-typescript
npm run test:package --prefix analyzers/javascript-typescript
npm run test:architecture --prefix analyzers/javascript-typescript
```

The architecture command builds the analyzer, reads the checked-in cases and
asserts their graph and loading outcomes. It also verifies installed-package
precedence. Default full evidence and summary output go to
`TestResults/js-ts-architecture-context/`. The
[recorded verification artifact](javascript-typescript-architecture-context-2026-10-03.json)
preserves the fixture hash, analyzer bundle hash, run IDs, evidence hashes,
runtime observations and installed-package results.

Before designing an Architecture ladder, the next useful measurement is to
separate implementation dependencies from public re-export surfaces in the
corpus. These results support keeping raw cycle counts and raw fan-out out of
automatic penalties until the meaning of each scored signal is established.
